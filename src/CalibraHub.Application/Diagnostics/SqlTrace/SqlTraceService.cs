using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Diagnostics.SqlTrace;

/// <summary>
/// Canlı SQL izleme — "sql profiler" özelliğini repository'lere DOKUNMADAN, merkezî bir
/// DiagnosticSource aboneliğiyle sağlar. Microsoft.Data.SqlClient, her komut/bağlantı olayını
/// "SqlClientDiagnosticListener" adlı bir <see cref="DiagnosticListener"/> üzerinden yayınlar
/// (bkz. Microsoft.Data.SqlClient.SqlClientDiagnosticListenerExtensions sabitleri); SqlClient
/// bu yayını yalnızca en az bir abone VARSA ve o abonenin isEnabled filtresi true dönüyorsa
/// yapar — abone yoksa komut metni/parametreleri hiç okunmaz, hiç string oluşturulmaz.
///
/// KAPALIYKEN SIFIR MALİYET: Bu servis DiagnosticListener.AllListeners'a HER ZAMAN (uygulama
/// açılışından itibaren) abonedir — ama bu yalnızca "SqlClientDiagnosticListener" kaynağının
/// VARLIĞINI keşfetmek içindir (sistemde bir kez, neredeyse hiç tetiklenmeyen bir olay).
/// Gerçek SQL olay aboneliği (Start) ve onun GERÇEK sökülmesi (Stop / süre dolumu) ayrı bir
/// IDisposable ile yönetilir — kapalıyken bu ikinci abonelik YOK, dolayısıyla SqlClient
/// tarafında IsEnabled() sorgusu bile hiç yapılmaz.
///
/// KAYITLAR DİSKE YAZILMAZ: yalnızca <see cref="SqlTraceBuffer"/> (bellek, sabit boyutlu halka
/// tampon) tutulur — kullanıcı kararı (parametre değerleri gerçek veri taşıyor, canlı izleme
/// isteniyor ama kalıcı birikim istenmiyor).
/// </summary>
public sealed class SqlTraceService : ISqlTraceService, IDisposable
{
    private const int BufferCapacity = 2000;
    private const int MaxCommandTextLength = 8000;
    private const int MaxParamValueLength = 2000;
    private const int MinDurationMinutes = 1;
    private const int MaxDurationMinutes = 60;
    private const int DefaultDurationMinutes = 10;

    private readonly SqlTraceBuffer _buffer = new(BufferCapacity);
    private readonly ILogger<SqlTraceService> _logger;
    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<Guid, (DateTime StartedUtc, long StartTicks)> _pendingCommands = new();
    private readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> _propCache = new();

    // SqlClient, "SqlClientDiagnosticListener" adiyla BIRDEN FAZLA DiagnosticListener
    // ornegi yayinlayabiliyor (uygulamada 3 ornek gozlendi). Yalniz sonuncusuna abone
    // olmak, sorgularin digerlerinden akmasi halinde HIC olay yakalamamak demekti —
    // izleme "calisiyor" gorunup bos kaliyordu. Bu yuzden hepsi tutulur ve hepsine
    // abone olunur.
    private readonly List<DiagnosticListener> _sqlListeners = new();
    private readonly List<IDisposable> _sqlSubscriptions = new();
    private CancellationTokenSource? _sessionCts;
    private volatile bool _running;
    private DateTime? _expiresAtUtc;

    public SqlTraceService(ILogger<SqlTraceService> logger)
    {
        _logger = logger;
        // Kesif aboneligi - kapaliyken de acik durur ama ucuzdur (yalnizca yeni
        // DiagnosticListener kaynagi olusunca OnNext tetiklenir, bu uygulama omrunde bir kez olur).
        DiagnosticListener.AllListeners.Subscribe(new AllListenersObserver(this));
    }

    public bool IsRunning => _running;
    public DateTime? ExpiresAtUtc => _expiresAtUtc;

    public DateTime Start(int durationMinutes)
    {
        var minutes = Math.Clamp(
            durationMinutes <= 0 ? DefaultDurationMinutes : durationMinutes,
            MinDurationMinutes, MaxDurationMinutes);

        lock (_stateLock)
        {
            // Onceki oturumdan kalan varsa gercekten sok, yeniden basla.
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            DisposeSubscriptionsNoLock();
            _pendingCommands.Clear();
            _buffer.Reset();

            _running = true;
            _expiresAtUtc = DateTime.UtcNow.AddMinutes(minutes);

            foreach (var listener in _sqlListeners)
                _sqlSubscriptions.Add(listener.Subscribe(new SqlEventObserver(this), IsEnabledForCommandEvents));

            var cts = new CancellationTokenSource();
            _sessionCts = cts;
            _ = ExpireAfterAsync(minutes, cts.Token);

            return _expiresAtUtc.Value;
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = null;
            StopInternalNoLock();
        }
    }

    private async Task ExpireAfterAsync(int minutes, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(minutes), ct);
        }
        catch (TaskCanceledException)
        {
            return; // Stop() tarafindan iptal edildi, tekrar StopInternal cagirmaya gerek yok.
        }

        lock (_stateLock)
        {
            StopInternalNoLock();
        }
    }

    /// <summary>Dinleyiciyi GERCEKTEN soker (yalnizca bayrak degil) — cagiran _stateLock icinde olmali.</summary>
    private void StopInternalNoLock()
    {
        _running = false;
        _expiresAtUtc = null;
        DisposeSubscriptionsNoLock();
        _pendingCommands.Clear();
    }

    /// <summary>Tum dinleyici aboneliklerini soker — cagiran _stateLock icinde olmali.</summary>
    private void DisposeSubscriptionsNoLock()
    {
        foreach (var sub in _sqlSubscriptions)
        {
            try { sub.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "SQL izleme aboneligi sokulemedi."); }
        }
        _sqlSubscriptions.Clear();
    }

    public SqlTraceEventsResult GetEvents(long afterSeq)
    {
        var (events, dropped) = _buffer.Snapshot(afterSeq);
        return new SqlTraceEventsResult(_running, _expiresAtUtc, events, dropped);
    }

    public void RecordRequest(string requestId, string path, string method, int statusCode, double durationMs)
    {
        if (!_running) return;
        _buffer.Add(new SqlTraceEvent(
            Seq: 0, TsUtc: DateTime.UtcNow, Kind: SqlTraceEventKind.Request, RequestId: requestId,
            DurationMs: durationMs, Text: null, Parameters: null,
            Path: path, Method: method, StatusCode: statusCode,
            Error: null, Database: null, Truncated: false));
    }

    // ── DiagnosticSource entegrasyonu ──────────────────────────────────────────

    private void OnSqlListenerDiscovered(DiagnosticListener listener)
    {
        if (listener.Name != "SqlClientDiagnosticListener") return;
        lock (_stateLock)
        {
            if (_sqlListeners.Contains(listener)) return;
            _sqlListeners.Add(listener);
            // Nadir sira farki: Start() bu kesiften ONCE cagrilmis olabilir — o durumda
            // yeni gelen dinleyiciye de hemen abone ol, yoksa o kaynaktan akan sorgular kacar.
            if (_running)
                _sqlSubscriptions.Add(listener.Subscribe(new SqlEventObserver(this), IsEnabledForCommandEvents));
        }
    }

    /// <summary>
    /// Yalnizca komut olaylarina abone ol — baglanti ac/kapat ve transaction olaylari icin
    /// SqlClient tarafinda anonim payload nesnesi hic olusturulmaz (IsEnabled false doner).
    /// </summary>
    private static bool IsEnabledForCommandEvents(string eventName) =>
        eventName is "Microsoft.Data.SqlClient.WriteCommandBefore"
                  or "Microsoft.Data.SqlClient.WriteCommandAfter"
                  or "Microsoft.Data.SqlClient.WriteCommandError";

    private void OnSqlEvent(string eventName, object? payload)
    {
        try
        {
            switch (eventName)
            {
                case "Microsoft.Data.SqlClient.WriteCommandBefore":
                    HandleCommandBefore(payload);
                    break;
                case "Microsoft.Data.SqlClient.WriteCommandAfter":
                    HandleCommandAfter(payload);
                    break;
                case "Microsoft.Data.SqlClient.WriteCommandError":
                    HandleCommandError(payload);
                    break;
                // Baglanti-ac/kapat ve transaction olaylari bilincli olarak yakalanmiyor —
                // istenen kapsam "SQL komutlari" (metin+parametre+sure), gurultu azaltmak icin.
            }
        }
        catch (Exception ex)
        {
            // Izleme altyapisi is akisini ASLA bozamaz; hata sadece loglanir (sessiz catch degil).
            _logger.LogWarning(ex, "[SqlTrace] SQL olayi yakalanirken hata olustu, kayit atlandi.");
        }
    }

    private void HandleCommandBefore(object? payload)
    {
        if (payload == null) return;
        var operationId = GetProp<Guid>(payload, "OperationId");
        if (operationId == Guid.Empty) return;
        _pendingCommands[operationId] = (DateTime.UtcNow, Stopwatch.GetTimestamp());
    }

    private void HandleCommandAfter(object? payload) => HandleCommandCompletion(payload, SqlTraceEventKind.Sql, error: null);

    private void HandleCommandError(object? payload)
    {
        if (payload == null) return;
        var ex = GetPropObj(payload, "Exception") as Exception;
        // CLAUDE.md: mutasyon uc noktalarinda ex.Message istemciye sizdirilmaz — ANCAK izleme
        // kayitlari bu kuralin acikca belirtilen istisnasidir (teshis amacli, yonetici-only).
        HandleCommandCompletion(payload, SqlTraceEventKind.Error, ex?.Message);
    }

    private void HandleCommandCompletion(object? payload, string kind, string? error)
    {
        if (payload == null) return;
        var command = GetPropObj(payload, "Command") as SqlCommand;
        if (command == null) return;

        var requestId = SqlTraceContext.Current;
        if (requestId == SqlTraceContext.Excluded) return; // kendini-izleme dongu koruması

        double? durationMs = null;
        var tsUtc = DateTime.UtcNow;
        var operationId = GetProp<Guid>(payload, "OperationId");
        if (operationId != Guid.Empty && _pendingCommands.TryRemove(operationId, out var started))
        {
            durationMs = (Stopwatch.GetTimestamp() - started.StartTicks) * 1000.0 / Stopwatch.Frequency;
            tsUtc = started.StartedUtc;
        }

        var rawText = command.CommandText ?? string.Empty;
        var maskedText = SqlTraceMasking.MaskInlineLiterals(rawText);
        var truncated = maskedText.Length > MaxCommandTextLength;
        var text = truncated ? maskedText[..MaxCommandTextLength] : maskedText;

        List<SqlTraceParam>? parameters = null;
        if (command.Parameters is { Count: > 0 })
        {
            parameters = new List<SqlTraceParam>(command.Parameters.Count);
            foreach (SqlParameter p in command.Parameters)
            {
                string? valueStr;
                try
                {
                    valueStr = p.Value is null || p.Value == DBNull.Value
                        ? "NULL"
                        : Convert.ToString(p.Value, CultureInfo.InvariantCulture);
                }
                catch (Exception convEx)
                {
                    _logger.LogWarning(convEx, "[SqlTrace] Parametre degeri okunamadi: {Param}", p.ParameterName);
                    valueStr = "<okunamadi>";
                }
                if (valueStr != null && valueStr.Length > MaxParamValueLength)
                    valueStr = valueStr[..MaxParamValueLength] + "…(kırpıldı)";

                parameters.Add(new SqlTraceParam(p.ParameterName, SqlTraceMasking.MaskParamValue(p.ParameterName, valueStr)));
            }
        }

        string? database = null;
        try { database = command.Connection?.Database; }
        catch (Exception dbEx) { _logger.LogWarning(dbEx, "[SqlTrace] Veritabani adi okunamadi."); }

        _buffer.Add(new SqlTraceEvent(
            Seq: 0, TsUtc: tsUtc, Kind: kind, RequestId: requestId,
            DurationMs: durationMs, Text: text, Parameters: parameters,
            Path: null, Method: null, StatusCode: null,
            Error: error, Database: database, Truncated: truncated));
    }

    private T GetProp<T>(object payload, string name) where T : struct
        => GetPropObj(payload, name) is T v ? v : default;

    private object? GetPropObj(object payload, string name)
    {
        var key = (payload.GetType(), name);
        var prop = _propCache.GetOrAdd(key, k => k.Type.GetProperty(k.Name));
        return prop?.GetValue(payload);
    }

    public void Dispose()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        lock (_stateLock) { DisposeSubscriptionsNoLock(); }
    }

    // ── Kucuk yardimci observer'lar ─────────────────────────────────────────

    private sealed class AllListenersObserver : IObserver<DiagnosticListener>
    {
        private readonly SqlTraceService _owner;
        public AllListenersObserver(SqlTraceService owner) => _owner = owner;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(DiagnosticListener value) => _owner.OnSqlListenerDiscovered(value);
    }

    private sealed class SqlEventObserver : IObserver<KeyValuePair<string, object?>>
    {
        private readonly SqlTraceService _owner;
        public SqlEventObserver(SqlTraceService owner) => _owner = owner;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(KeyValuePair<string, object?> value) => _owner.OnSqlEvent(value.Key, value.Value);
    }
}
