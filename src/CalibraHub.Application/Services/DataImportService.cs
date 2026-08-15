using System.Diagnostics;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

/// <summary>
/// 2026-08-15 — Veritabanı üzerinden içe aktarım orkestrasyonu.
///
/// Akış (RunAsync):
///   1. Ön prosedür (seçilen DB'de) — hata aktarımı DURDURUR (veriyi o hazırlar).
///   2. Kaynaktan salt-okunur SELECT (IExternalDbReader).
///   3. Satırlar ImportRowSet'e çevrilir (kaynak kolon → hedef alan) ve ilgili
///      IImportTargetHandler.CommitAsync'e devredilir — Excel içe aktarımıyla AYNI
///      yazma katmanı, dolayısıyla aynı domain doğrulaması ve upsert davranışı.
///   4. Son prosedür — hatası iş tanımındaki ErrorBehavior'a göre uyarı ya da başarısızlık.
///   5. Çalıştırma logu (DataImportRun) kapatılır.
///
/// Entity/alan kataloğu <see cref="IImportService"/>'ten okunur (tek kaynak); yazma
/// için handler'lar doğrudan çözülür.
/// </summary>
public sealed class DataImportService : IDataImportService
{
    private readonly IDataImportRepository _repo;
    private readonly IExternalDbConnectionRepository _connections;
    private readonly IExternalDbReader _reader;
    private readonly IDataImportProcedureExecutor _procedures;
    private readonly IImportService _importCatalog;
    private readonly IReadOnlyDictionary<string, IImportTargetHandler> _handlers;
    private readonly ILogger<DataImportService> _logger;

    public DataImportService(
        IDataImportRepository repo,
        IExternalDbConnectionRepository connections,
        IExternalDbReader reader,
        IDataImportProcedureExecutor procedures,
        IImportService importCatalog,
        IEnumerable<IImportTargetHandler> handlers,
        ILogger<DataImportService> logger)
    {
        _repo = repo;
        _connections = connections;
        _reader = reader;
        _procedures = procedures;
        _importCatalog = importCatalog;
        _handlers = handlers.ToDictionary(h => h.Entity, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    // ── Kaynak bağlantıları ──────────────────────────────────────────────

    public async Task<IReadOnlyList<ExternalDbConnectionDto>> ListConnectionsAsync(bool includeInactive, CancellationToken ct)
        => (await _connections.ListAsync(includeInactive, ct)).Select(ToDto).ToList();

    public async Task<(bool Ok, string? Error, int Id)> SaveConnectionAsync(
        SaveExternalDbConnectionRequest req, int? userId, CancellationToken ct)
    {
        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length == 0) return (false, "Bağlantı adı zorunludur.", 0);
        if (string.IsNullOrWhiteSpace(req.DatabaseName)) return (false, "Veritabanı adı zorunludur.", 0);

        // CalibraHub sunucusu devralınıyorsa sunucu/kimlik bilgisi hiç istenmez.
        var isIntegrated = string.Equals(req.AuthMode, "Integrated", StringComparison.OrdinalIgnoreCase);
        if (!req.UseHostServer)
        {
            if (string.IsNullOrWhiteSpace(req.ServerName)) return (false, "Sunucu adı zorunludur.", 0);

            if (!isIntegrated && string.IsNullOrWhiteSpace(req.Username))
                return (false, "SQL kimlik doğrulamada kullanıcı adı zorunludur.", 0);

            // Yeni kayıtta parola zorunlu; güncellemede boş = "değiştirme".
            if (!isIntegrated && req.Id <= 0 && string.IsNullOrWhiteSpace(req.Password))
                return (false, "SQL kimlik doğrulamada parola zorunludur.", 0);
        }

        if (await _connections.NameExistsAsync(name, req.Id > 0 ? req.Id : null, ct))
            return (false, $"Aynı isimde başka bir bağlantı zaten tanımlı: '{name}'", 0);

        var entity = new ExternalDbConnection
        {
            Id = req.Id,
            Name = name,
            UseHostServer = req.UseHostServer,
            ServerName = req.UseHostServer ? string.Empty : req.ServerName.Trim(),
            DatabaseName = req.DatabaseName.Trim(),
            AuthMode = isIntegrated ? "Integrated" : "Sql",
            Username = isIntegrated || req.UseHostServer ? null : req.Username?.Trim(),
            Encrypt = req.Encrypt,
            TrustServerCertificate = req.TrustServerCertificate,
            ConnectTimeoutSeconds = req.ConnectTimeoutSeconds <= 0 ? 15 : req.ConnectTimeoutSeconds,
            CommandTimeoutSeconds = req.CommandTimeoutSeconds <= 0 ? 120 : req.CommandTimeoutSeconds,
            IsActive = req.IsActive,
            CreatedById = userId is > 0 ? userId : null,
            UpdatedById = userId is > 0 ? userId : null,
        };

        var id = await _connections.SaveAsync(entity, isIntegrated || req.UseHostServer ? null : req.Password, ct);
        return (true, null, id);
    }

    public Task DeleteConnectionAsync(int id, CancellationToken ct) => _connections.DeleteAsync(id, ct);

    public Task<bool> ToggleConnectionActiveAsync(int id, CancellationToken ct) => _connections.ToggleActiveAsync(id, ct);

    public async Task<ExternalDbTestResultDto> TestConnectionAsync(int connectionId, CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId, ct);
        return conn is null
            ? new ExternalDbTestResultDto(false, "Bağlantı bulunamadı.", null, null)
            : await _reader.TestAsync(conn, ct);
    }

    public async Task<ExternalDbTestResultDto> TestConnectionDraftAsync(SaveExternalDbConnectionRequest req, CancellationToken ct)
    {
        var isIntegrated = string.Equals(req.AuthMode, "Integrated", StringComparison.OrdinalIgnoreCase);

        // Parola boş geldiyse (kullanıcı mevcut kaydı düzenliyor ve parolaya dokunmadı)
        // saklanan şifreli parolayı kullan — aksi halde test boş parolayla düşerdi.
        string? storedPassword = null;
        if (!isIntegrated && string.IsNullOrWhiteSpace(req.Password) && req.Id > 0)
            storedPassword = (await _connections.GetByIdAsync(req.Id, ct))?.PasswordEncrypted;

        var draft = new ExternalDbConnection
        {
            Name = req.Name ?? "(taslak)",
            UseHostServer = req.UseHostServer,
            ServerName = req.ServerName?.Trim() ?? string.Empty,
            DatabaseName = req.DatabaseName?.Trim() ?? string.Empty,
            AuthMode = isIntegrated ? "Integrated" : "Sql",
            Username = isIntegrated ? null : req.Username?.Trim(),
            PasswordEncrypted = storedPassword,
            Encrypt = req.Encrypt,
            TrustServerCertificate = req.TrustServerCertificate,
            ConnectTimeoutSeconds = req.ConnectTimeoutSeconds <= 0 ? 15 : req.ConnectTimeoutSeconds,
            CommandTimeoutSeconds = req.CommandTimeoutSeconds <= 0 ? 120 : req.CommandTimeoutSeconds,
        };

        // Yeni parola girildiyse ham haliyle taşınır; okuyucu "enc:" öneki olmayan
        // değeri olduğu gibi kullanır.
        if (!isIntegrated && !string.IsNullOrWhiteSpace(req.Password))
            draft.PasswordEncrypted = req.Password;

        return await _reader.TestAsync(draft, ct);
    }

    // ── Şema keşfi ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ExternalDbObjectDto>> ListSourceObjectsAsync(int connectionId, CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId, ct);
        if (conn is null) return Array.Empty<ExternalDbObjectDto>();
        return await _reader.ListObjectsAsync(conn, ct);
    }

    public async Task<IReadOnlyList<ExternalDbColumnDto>> ListSourceColumnsAsync(
        int connectionId, string schemaName, string objectName, CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId, ct);
        if (conn is null) return Array.Empty<ExternalDbColumnDto>();
        return await _reader.ListColumnsAsync(conn, schemaName, objectName, ct);
    }

    // ── Hedef entity kataloğu ────────────────────────────────────────────

    public Task<IReadOnlyList<ImportEntityDto>> ListTargetEntitiesAsync(CancellationToken ct)
        => Task.FromResult(_importCatalog.GetEntities());

    /// <summary>
    /// Statik + DİNAMİK izinli değerleri birleştirir. Dinamik olanlar (birim, rehber
    /// değerleri gibi DB'den gelenler) `GetFields()` içinde yoktur; eşleme ekranında
    /// gösterilmezse uyarlamacı kaynak view'ı hangi değerleri üreteceğini bilemez.
    /// </summary>
    public async Task<IReadOnlyList<ImportTargetFieldDto>> ListTargetFieldsAsync(string entity, CancellationToken ct)
    {
        var fields = await _importCatalog.GetTargetFieldsAsync(entity, ct);
        if (fields.Count == 0) return fields;

        if (!_handlers.TryGetValue((entity ?? string.Empty).Trim(), out var handler)) return fields;

        IReadOnlyDictionary<string, IReadOnlyList<string>> dynamic;
        try
        {
            dynamic = await handler.GetDynamicAllowedValuesAsync(ct);
        }
        catch (Exception ex)
        {
            // Dinamik değer listesi ekranı zenginleştirir; alınamazsa eşleme yine çalışmalı.
            _logger.LogError(ex, "Dinamik izinli değerler okunamadı. Entity={Entity}", entity);
            return fields;
        }
        if (dynamic.Count == 0) return fields;

        return fields.Select(f =>
            f.AllowedValues is null or { Count: 0 }
            && dynamic.TryGetValue(f.Key, out var vals) && vals.Count > 0
                ? f with { AllowedValues = vals }
                : f).ToList();
    }

    // ── İş tanımı ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DataImportJobDto>> ListJobsAsync(bool includeInactive, CancellationToken ct)
    {
        var jobs = await _repo.ListJobsAsync(includeInactive, ct);
        if (jobs.Count == 0) return Array.Empty<DataImportJobDto>();

        var connections = (await _connections.ListAsync(true, ct)).ToDictionary(c => c.Id);
        return jobs.Select(j => ToDto(j, connections.GetValueOrDefault(j.ConnectionId)?.Name)).ToList();
    }

    public async Task<DataImportJobDto?> GetJobAsync(int id, CancellationToken ct)
    {
        var job = await _repo.GetJobAsync(id, ct);
        if (job is null) return null;
        var conn = await _connections.GetByIdAsync(job.ConnectionId, ct);
        return ToDto(job, conn?.Name);
    }

    public async Task<(bool Ok, string? Error, int Id)> SaveJobAsync(
        SaveDataImportJobRequest req, int? userId, CancellationToken ct)
    {
        var name = (req.Name ?? string.Empty).Trim();

        var job = new DataImportJob
        {
            Id = req.Id,
            Name = name,
            ConnectionId = req.ConnectionId,
            TargetEntity = (req.TargetEntity ?? string.Empty).Trim().ToUpperInvariant(),
            SourceSchema = string.IsNullOrWhiteSpace(req.SourceSchema) ? "dbo" : req.SourceSchema.Trim(),
            SourceObject = (req.SourceObject ?? string.Empty).Trim(),
            MatchKeyField = (req.MatchKeyField ?? string.Empty).Trim(),
            MaxRows = req.MaxRows <= 0 ? 50_000 : req.MaxRows,
            SourceFilterJson = Blank(req.SourceFilterJson),
            ErrorBehavior = req.ErrorBehavior,
            PreProcedureName = Blank(req.PreProcedureName),
            PreProcedureTarget = req.PreProcedureTarget,
            PreProcedureParamsJson = Blank(req.PreProcedureParamsJson),
            PostProcedureName = Blank(req.PostProcedureName),
            PostProcedureTarget = req.PostProcedureTarget,
            PostProcedureParamsJson = Blank(req.PostProcedureParamsJson),
            IsActive = req.IsActive,
            CreatedById = userId is > 0 ? userId : null,
            UpdatedById = userId is > 0 ? userId : null,
            Columns = (req.Columns ?? Array.Empty<DataImportColumnDto>())
                .Where(c => !string.IsNullOrWhiteSpace(c.SourceColumn) && !string.IsNullOrWhiteSpace(c.TargetKey))
                .Select((c, i) => new DataImportJobColumn
                {
                    SourceColumn = c.SourceColumn.Trim(),
                    TargetKey = c.TargetKey.Trim(),
                    SortOrder = c.SortOrder > 0 ? c.SortOrder : i + 1,
                })
                .ToList(),
        };

        // Anahtar alan zorunluluğu dahil tüm yapısal doğrulama entity'de.
        var error = job.Validate();
        if (error is not null) return (false, error, 0);

        if (!_handlers.ContainsKey(job.TargetEntity))
            return (false, $"Bilinmeyen hedef entity: {job.TargetEntity}", 0);

        if (await _repo.JobNameExistsAsync(name, req.Id > 0 ? req.Id : null, ct))
            return (false, $"Aynı isimde başka bir aktarım işi zaten tanımlı: '{name}'", 0);

        var id = await _repo.SaveJobAsync(job, ct);
        return (true, null, id);
    }

    public Task DeleteJobAsync(int id, CancellationToken ct) => _repo.DeleteJobAsync(id, ct);

    public Task<bool> ToggleJobActiveAsync(int id, CancellationToken ct) => _repo.ToggleJobActiveAsync(id, ct);

    // ── Önizleme ─────────────────────────────────────────────────────────

    public async Task<DataImportPreviewResultDto> PreviewAsync(int jobId, int sampleRows, CancellationToken ct)
    {
        if (sampleRows <= 0) sampleRows = 50;

        var (job, conn, handler, error) = await LoadRunnableAsync(jobId, ct);
        if (error is not null) return new DataImportPreviewResultDto(false, error, 0, null);

        var sample = await ReadSourceAsync(job!, conn!, sampleRows, ct);
        if (!sample.Ok) return new DataImportPreviewResultDto(false, sample.Error, 0, null);

        await handler!.PreloadAsync(ct);
        var rowSet = BuildRowSet(job!, sample);
        var preview = await handler.PreviewAsync(rowSet, ct);
        return new DataImportPreviewResultDto(true, null, sample.Rows.Count, preview);
    }

    // ── Çalıştırma ───────────────────────────────────────────────────────

    public async Task<DataImportRunResultDto> RunAsync(
        int jobId, DataImportTriggerType trigger, string? triggeredBy, int? userId, CancellationToken ct)
    {
        var (job, conn, handler, loadError) = await LoadRunnableAsync(jobId, ct);
        if (loadError is not null) return new DataImportRunResultDto(false, loadError, null, null);

        var startedAt = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();
        var run = new DataImportRun
        {
            JobId = job!.Id,
            TriggerType = trigger,
            StartedAt = startedAt,
            Status = DataImportRunStatus.Failed,
            TriggeredBy = triggeredBy,
        };
        run.Id = await _repo.InsertRunAsync(run, ct);

        ImportCommitResultDto? commit = null;
        try
        {
            var context = new DataImportProcedureContext(
                run.Id, job.Id, job.Name, startedAt, triggeredBy, 0, 0, 0, 0);

            // 1) Ön prosedür — veriyi o hazırlar, hatası aktarımı durdurur.
            if (!string.IsNullOrWhiteSpace(job.PreProcedureName))
            {
                var pre = await _procedures.ExecuteAsync(
                    job.PreProcedureName!, job.PreProcedureTarget, conn, job.PreProcedureParamsJson, context, ct);
                run.PreProcedureResult = pre.Describe(job.PreProcedureName!, job.PreProcedureTarget);
                if (!pre.Ok)
                {
                    run.ErrorMessage = $"Ön prosedür başarısız, aktarım yapılmadı: {pre.Error}";
                    return await FinishAsync(run, sw, DataImportRunStatus.Failed, commit, ct);
                }
            }

            // 2) Kaynaktan oku (salt-okunur).
            var sample = await ReadSourceAsync(job, conn!, job.MaxRows, ct);
            if (!sample.Ok)
            {
                run.ErrorMessage = sample.Error;
                return await FinishAsync(run, sw, DataImportRunStatus.Failed, commit, ct);
            }
            run.RowsRead = sample.Rows.Count;

            // 3) Yaz — Excel içe aktarımıyla aynı handler.
            await handler!.PreloadAsync(ct);
            commit = await handler.CommitAsync(BuildRowSet(job, sample), userId, ct);
            run.RowsInserted = commit.Inserted;
            run.RowsUpdated = commit.Updated;
            run.RowsFailed = commit.Failed;
            if (!commit.Success)
            {
                run.ErrorMessage = commit.Error;
                return await FinishAsync(run, sw, DataImportRunStatus.Failed, commit, ct);
            }

            // 4) Son prosedür — satır sayıları artık dolu.
            var status = DataImportRunStatus.Success;
            if (!string.IsNullOrWhiteSpace(job.PostProcedureName))
            {
                var postContext = context with
                {
                    RowsRead = run.RowsRead,
                    RowsInserted = run.RowsInserted,
                    RowsUpdated = run.RowsUpdated,
                    RowsFailed = run.RowsFailed,
                };
                var post = await _procedures.ExecuteAsync(
                    job.PostProcedureName!, job.PostProcedureTarget, conn, job.PostProcedureParamsJson, postContext, ct);
                run.PostProcedureResult = post.Describe(job.PostProcedureName!, job.PostProcedureTarget);
                if (!post.Ok)
                {
                    // Kayıtlar YAZILDI — geri alınmaz. Katı modda çalıştırma başarısız
                    // sayılır, esnek modda uyarılı başarılı.
                    run.ErrorMessage = $"Son prosedür başarısız (kayıtlar yazıldı): {post.Error}";
                    status = job.ErrorBehavior == DataImportErrorBehavior.Strict
                        ? DataImportRunStatus.Failed
                        : DataImportRunStatus.SucceededWithWarnings;
                }
            }

            return await FinishAsync(run, sw, status, commit, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "İçe aktarım çalıştırması başarısız. İş={JobName} JobId={JobId} RunId={RunId}",
                job.Name, job.Id, run.Id);
            run.ErrorMessage = ex.Message;
            return await FinishAsync(run, sw, DataImportRunStatus.Failed, commit, ct);
        }
    }

    public async Task<IReadOnlyList<DataImportRunDto>> ListRunsAsync(int? jobId, int take, CancellationToken ct)
    {
        var runs = await _repo.ListRunsAsync(jobId, take, ct);
        if (runs.Count == 0) return Array.Empty<DataImportRunDto>();

        var jobs = (await _repo.ListJobsAsync(true, ct)).ToDictionary(j => j.Id);
        return runs.Select(r => ToDto(r, jobs.GetValueOrDefault(r.JobId)?.Name)).ToList();
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────

    private async Task<(DataImportJob? Job, ExternalDbConnection? Connection, IImportTargetHandler? Handler, string? Error)>
        LoadRunnableAsync(int jobId, CancellationToken ct)
    {
        var job = await _repo.GetJobAsync(jobId, ct);
        if (job is null) return (null, null, null, "Aktarım işi bulunamadı.");

        var error = job.Validate();
        if (error is not null) return (null, null, null, error);

        var conn = await _connections.GetByIdAsync(job.ConnectionId, ct);
        if (conn is null) return (null, null, null, "Kaynak bağlantı bulunamadı.");
        if (!conn.IsActive) return (null, null, null, $"Kaynak bağlantı pasif: '{conn.Name}'");

        if (!_handlers.TryGetValue(job.TargetEntity, out var handler))
            return (null, null, null, $"Bilinmeyen hedef entity: {job.TargetEntity}");

        return (job, conn, handler, null);
    }

    private Task<ExternalDbSampleDto> ReadSourceAsync(
        DataImportJob job, ExternalDbConnection conn, int maxRows, CancellationToken ct)
        => _reader.ReadRowsAsync(
            conn, job.SourceSchema, job.SourceObject,
            job.Columns.Select(c => c.SourceColumn).ToList(), maxRows, ct, job.SourceFilterJson);

    /// <summary>
    /// Kaynak satırlarını (kolon adı → değer) hedef alan sözlüğüne çevirir.
    /// Anahtar alan job.Validate() ile eşlemede garanti edilmiştir.
    /// </summary>
    private static ImportRowSet BuildRowSet(DataImportJob job, ExternalDbSampleDto sample)
    {
        var columns = job.Columns.OrderBy(c => c.SortOrder).ToList();
        var rows = new List<IReadOnlyDictionary<string, string?>>(sample.Rows.Count);

        foreach (var sourceRow in sample.Rows)
        {
            var target = new Dictionary<string, string?>(columns.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var col in columns)
            {
                var value = sourceRow.TryGetValue(col.SourceColumn, out var v) ? v : null;
                target[col.TargetKey] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            rows.Add(target);
        }

        var mappedKeys = columns.Select(c => c.TargetKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new ImportRowSet(rows, mappedKeys, job.MatchKeyField);
    }

    private async Task<DataImportRunResultDto> FinishAsync(
        DataImportRun run, Stopwatch sw, DataImportRunStatus status, ImportCommitResultDto? commit, CancellationToken ct)
    {
        sw.Stop();
        run.Status = status;
        run.FinishedAt = DateTime.UtcNow;
        run.DurationMs = (int)sw.ElapsedMilliseconds;
        await _repo.UpdateRunAsync(run, ct);

        var ok = status != DataImportRunStatus.Failed;
        return new DataImportRunResultDto(ok, ok ? null : run.ErrorMessage, ToDto(run, null), commit);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExternalDbConnectionDto ToDto(ExternalDbConnection c) => new(
        c.Id, c.Name, c.UseHostServer, c.ServerName, c.DatabaseName, c.AuthMode, c.Username,
        !string.IsNullOrWhiteSpace(c.PasswordEncrypted),
        c.Encrypt, c.TrustServerCertificate, c.ConnectTimeoutSeconds, c.CommandTimeoutSeconds,
        c.IsActive, c.Created, c.Updated);

    private static DataImportJobDto ToDto(DataImportJob j, string? connectionName) => new(
        j.Id, j.Name, j.ConnectionId, connectionName, j.TargetEntity, null,
        j.SourceSchema, j.SourceObject, j.MatchKeyField, j.MaxRows, j.SourceFilterJson, j.ErrorBehavior,
        j.PreProcedureName, j.PreProcedureTarget, j.PreProcedureParamsJson,
        j.PostProcedureName, j.PostProcedureTarget, j.PostProcedureParamsJson,
        j.Columns.OrderBy(c => c.SortOrder)
                 .Select(c => new DataImportColumnDto(c.SourceColumn, c.TargetKey, c.SortOrder))
                 .ToList(),
        j.IsActive, j.Created, j.Updated);

    private static DataImportRunDto ToDto(DataImportRun r, string? jobName) => new(
        r.Id, r.JobId, jobName, r.TriggerType, r.StartedAt, r.FinishedAt, r.DurationMs,
        r.Status, r.RowsRead, r.RowsInserted, r.RowsUpdated, r.RowsFailed,
        r.ErrorMessage, r.PreProcedureResult, r.PostProcedureResult, r.TriggeredBy);
}
