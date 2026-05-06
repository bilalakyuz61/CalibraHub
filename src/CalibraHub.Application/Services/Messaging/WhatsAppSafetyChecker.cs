using System.Security.Cryptography;
using System.Text;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Messaging;

/// <summary>
/// WhatsApp gönderim öncesi tüm safety kontrollerini yapan kapı.
/// Reddedilen mesajlar log'a "BlockReason" ile yazılır, audit'e tabidir.
/// </summary>
public sealed class WhatsAppSafetyChecker
{
    private readonly IWhatsAppSendLogRepository _logRepo;
    private readonly IWhatsAppSafetyRulesRepository _rulesRepo;
    private readonly ILogger<WhatsAppSafetyChecker> _logger;

    public WhatsAppSafetyChecker(
        IWhatsAppSendLogRepository logRepo,
        IWhatsAppSafetyRulesRepository rulesRepo,
        ILogger<WhatsAppSafetyChecker> logger)
    {
        _logRepo   = logRepo;
        _rulesRepo = rulesRepo;
        _logger    = logger;
    }

    /// <summary>
    /// Gönderime izin var mı? Reject ise sebep+suggested wait time döner.
    /// </summary>
    /// <param name="interactive">
    /// true: kullanici elle chat ekranindan gonderiyor — anti-spam limitleri atlanir.
    /// false: otomasyon/toplu gonderim — tum limitler uygulanir.
    /// </param>
    public async Task<SafetyCheckResult> CheckAsync(string toPhone, string messageText, CancellationToken cancellationToken, bool interactive = false)
    {
        var rules = await _rulesRepo.GetAsync(cancellationToken) ?? new WhatsAppSafetyRules();
        var nowUtc = DateTime.UtcNow;
        var nowLocal = DateTime.Now;
        var msgHash = ComputeHash(messageText);

        // Interactive chat: kullanici elle yaziyor → spam riski yok, tum rate limit'leri atla.
        // Audit log yine yazilir (caller tarafinda), ama gonderim hicbir limite takilmaz.
        if (interactive)
        {
            return Allow(rules, msgHash);
        }

        // 1) Sessiz saat kontrolü
        if (rules.RespectQuietHours && IsQuietHour(nowLocal.Hour, rules.QuietHoursStartHour, rules.QuietHoursEndHour))
        {
            return Reject($"Sessiz saatlerde gönderim yasak ({rules.QuietHoursStartHour:00}:00 - {rules.QuietHoursEndHour:00}:00).",
                          suggestedWaitMinutes: MinutesUntilHour(nowLocal, rules.QuietHoursEndHour));
        }

        // 2) Burst protection: son N kayıttaki ardışık başarısızlık
        var recent = await _logRepo.GetRecentLogsAsync(rules.MaxConsecutiveFailures, cancellationToken);
        var consecutiveFails = recent.TakeWhile(l => !l.Success && string.IsNullOrEmpty(l.BlockReason)).Count();
        if (consecutiveFails >= rules.MaxConsecutiveFailures)
        {
            var lastFail = recent.First().SentAt;
            var cooldownEnd = lastFail.AddMinutes(rules.FailureCooldownMinutes);
            if (cooldownEnd > nowUtc)
            {
                var minutesLeft = (int)Math.Ceiling((cooldownEnd - nowUtc).TotalMinutes);
                return Reject($"{consecutiveFails} ardışık başarısızlık — {minutesLeft} dk cooldown'dasın.", minutesLeft);
            }
        }

        // 3) Per-minute rate limit
        var lastMinute = await _logRepo.CountSuccessAsync(nowUtc.AddMinutes(-1), cancellationToken);
        if (lastMinute >= rules.MaxPerMinute)
        {
            return Reject($"Dakika limiti aşıldı ({lastMinute}/{rules.MaxPerMinute}).", suggestedWaitMinutes: 1);
        }

        // 4) Per-hour rate limit
        var lastHour = await _logRepo.CountSuccessAsync(nowUtc.AddHours(-1), cancellationToken);
        if (lastHour >= rules.MaxPerHour)
        {
            return Reject($"Saat limiti aşıldı ({lastHour}/{rules.MaxPerHour}). 1 saat sonra tekrar dene.", suggestedWaitMinutes: 60);
        }

        // 5) Per-day rate limit (warm-up dönemi varsa düşük limit uygula)
        var todayStartUtc = DateTime.UtcNow.Date;
        var todayCount = await _logRepo.CountSuccessAsync(todayStartUtc, cancellationToken);
        var effectiveDailyLimit = IsInWarmupPeriod(rules) ? rules.WarmupMaxPerDay : rules.MaxPerDay;
        if (todayCount >= effectiveDailyLimit)
        {
            var phase = IsInWarmupPeriod(rules) ? " (warm-up)" : "";
            return Reject($"Günlük limit aşıldı{phase} ({todayCount}/{effectiveDailyLimit}). Yarın tekrar dene.",
                          suggestedWaitMinutes: (int)(todayStartUtc.AddDays(1) - nowUtc).TotalMinutes);
        }

        // 6) Per-recipient daily limit (aynı numaraya günde max N)
        var perRecipient = await _logRepo.CountSuccessForRecipientTodayAsync(NormalizePhone(toPhone), cancellationToken);
        if (perRecipient >= rules.MaxPerRecipientPerDay)
        {
            return Reject($"Bu alıcıya günlük limit aşıldı ({perRecipient}/{rules.MaxPerRecipientPerDay}).", suggestedWaitMinutes: 60 * 12);
        }

        // 7) Identik mesaj sayacı (spam koruma)
        var identicCount = await _logRepo.CountSuccessByHashTodayAsync(msgHash, cancellationToken);
        if (identicCount >= rules.MaxIdenticalMessagesPerDay)
        {
            return Reject($"Aynı içerikli mesaj bugün {identicCount} kere gönderildi (limit {rules.MaxIdenticalMessagesPerDay}). Mesaj içeriğini farklılaştır.", 0);
        }

        return Allow(rules, msgHash);
    }

    /// <summary>
    /// İnsan-benzeri gecikme — random 3-15 sn. Caller bunu await Task.Delay() ile uygular.
    /// </summary>
    public TimeSpan ComputeHumanDelay(WhatsAppSafetyRules rules)
    {
        var min = Math.Max(0, rules.MinDelaySeconds);
        var max = Math.Max(min + 1, rules.MaxDelaySeconds);
        return TimeSpan.FromSeconds(Random.Shared.Next(min, max));
    }

    private static SafetyCheckResult Reject(string reason, int suggestedWaitMinutes = 0)
        => new(false, reason, suggestedWaitMinutes, MessageHash: null, Rules: null);

    private static SafetyCheckResult Allow(WhatsAppSafetyRules rules, string messageHash)
        => new(true, null, 0, messageHash, rules);

    private static bool IsInWarmupPeriod(WhatsAppSafetyRules rules)
    {
        if (rules.WarmupDays <= 0) return false;
        var since = rules.CreatedAt == default ? DateTime.UtcNow : rules.CreatedAt;
        return (DateTime.UtcNow - since).TotalDays < rules.WarmupDays;
    }

    private static bool IsQuietHour(int currentHour, int startHour, int endHour)
    {
        // start=20, end=8 → 20-23 ve 0-7 sessiz
        if (startHour > endHour)
            return currentHour >= startHour || currentHour < endHour;
        // start=8, end=20 → 8-19 aktif demek (yani sessiz saat 20-7)
        return currentHour >= startHour && currentHour < endHour;
    }

    private static int MinutesUntilHour(DateTime now, int targetHour)
    {
        var target = new DateTime(now.Year, now.Month, now.Day, targetHour, 0, 0, DateTimeKind.Local);
        if (target <= now) target = target.AddDays(1);
        return (int)(target - now).TotalMinutes;
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string NormalizePhone(string input)
    {
        var sb = new StringBuilder();
        foreach (var c in (input ?? string.Empty).Trim())
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}

public sealed record SafetyCheckResult(
    bool Allowed,
    string? RejectReason,
    int SuggestedWaitMinutes,
    string? MessageHash,
    WhatsAppSafetyRules? Rules);
