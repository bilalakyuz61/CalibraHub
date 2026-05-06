namespace CalibraHub.Domain.Entities;

/// <summary>
/// Her WhatsApp gönderim girişimini kaydeden audit log.
/// Rate-limit ve spam-koruma mantığının baktığı tablo bu.
/// </summary>
public sealed class WhatsAppSendLog
{
    public long      Id            { get; set; }
    public DateTime  SentAt        { get; set; }
    public string?   ToPhone       { get; set; }
    public string?   MessageHash   { get; set; }   // SHA1 hash — identik mesaj tespiti
    public string?   MessageId     { get; set; }   // Meta cevabı
    public bool      Success       { get; set; }
    public string?   ErrorMessage  { get; set; }
    public string?   BlockReason   { get; set; }   // safety checker rejection reason (boş ise gönderildi)
}

/// <summary>
/// WhatsApp güvenlik kuralları (rate limit, sessiz saat, vb).
/// Tek-row config tablosu. UI'dan değiştirilebilir; default'lar agresif/güvenli tarafta.
/// </summary>
public sealed class WhatsAppSafetyRules
{
    public int      Id                       { get; set; } = 1;

    // Rate limits
    public int      MaxPerMinute             { get; set; } = 5;
    public int      MaxPerHour               { get; set; } = 60;
    public int      MaxPerDay                { get; set; } = 300;
    public int      MaxPerRecipientPerDay    { get; set; } = 3;

    // İnsan-benzeri gecikme
    public int      MinDelaySeconds          { get; set; } = 3;
    public int      MaxDelaySeconds          { get; set; } = 15;

    // Sessiz saatler (gece bu aralıkta gönderme)
    public bool     RespectQuietHours        { get; set; } = true;
    public int      QuietHoursStartHour      { get; set; } = 20;  // 20:00
    public int      QuietHoursEndHour        { get; set; } = 8;   // 08:00

    // Burst koruma — N ardışık başarısızlıkta cooldown
    public int      MaxConsecutiveFailures   { get; set; } = 5;
    public int      FailureCooldownMinutes   { get; set; } = 30;

    // Warm-up (ilk açılıştan itibaren N gün düşük tempo)
    public int      WarmupDays               { get; set; } = 7;
    public int      WarmupMaxPerDay          { get; set; } = 50;

    // Identik mesaj koruma
    public int      MaxIdenticalMessagesPerDay { get; set; } = 10;

    public DateTime CreatedAt                { get; set; }
    public DateTime UpdatedAt                { get; set; }
}
