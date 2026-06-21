namespace CalibraHub.Application.Abstractions.Persistence;

public sealed record ApprovalTokenRecord(
    int Id,
    string Token,
    int InstanceId,
    int? StepRecordId,
    string ApproverId,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    string? UsedAction);

public interface IApprovalTokenRepository
{
    /// <summary>Mevcut request'in şirket DB'sine token yazar; token string'ini döner.</summary>
    Task<string> CreateAsync(int instanceId, int? stepRecordId, string approverId, CancellationToken ct);

    /// <summary>companyId ile belirlenen şirket DB'sinden token'ı bulur (kimlik doğrulama gerekmez).</summary>
    Task<ApprovalTokenRecord?> FindAsync(int companyId, string token, CancellationToken ct);

    /// <summary>Token'ı kullanıldı olarak işaretler (action: "approve" | "reject").</summary>
    Task ConsumeAsync(int companyId, int tokenId, string action, CancellationToken ct);
}
