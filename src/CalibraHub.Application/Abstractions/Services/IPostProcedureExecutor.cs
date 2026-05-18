namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Entegrasyon basariyla calistiktan sonra opsiyonel SQL stored procedure'u
/// calistiran servis. Engine'in HTTP cagrisi sonrasi tetikler.
///
/// Parametre kaynak tipleri (paramsJson icindeki sourceType):
///   FormField — header/lines verisinden alan degeri (sourceValue = field code)
///   Constant  — sabit literal (sourceValue = literal degeri)
///   RunMeta   — runtime metadata (RunId / IntegrationId / StartedAt)
///   Response  — HTTP response payload'undan deger (basit JSON path)
/// </summary>
public interface IPostProcedureExecutor
{
    /// <summary>
    /// Stored procedure'u parametrelerle calistir. Parametre cozumlemesinde hata
    /// olusursa exception throw eder. Caller try/catch ile sarsin (run log'a yazilir).
    /// </summary>
    Task<PostProcedureExecutionResult> ExecuteAsync(
        string procedureName,
        string? paramsJson,
        IReadOnlyDictionary<string, object?> headerData,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? linesData,
        PostProcedureRunMeta runMeta,
        string? httpResponseBody,
        int? httpStatusCode,
        CancellationToken ct);
}

public sealed record PostProcedureRunMeta(
    long RunId,
    int IntegrationId,
    DateTime StartedAt,
    string? SourceRecordId,
    string? TriggeredBy);

public sealed record PostProcedureExecutionResult(
    bool Success,
    int RowsAffected,
    string? ErrorMessage);
