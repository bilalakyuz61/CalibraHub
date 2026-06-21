using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Mail / WhatsApp tek tıkla onay tokeni — gönderilen bağlantıdaki GUID token, onay adımına ve onaylayıcıya bağlanır; süre dolumunda veya kullanıldıktan sonra geçersiz olur.")]
public sealed class ApprovalActionToken
{
    public int Id { get; init; }
    public required string Token { get; init; }
    public int InstanceId { get; init; }
    public int? StepRecordId { get; init; }
    public required string ApproverId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? UsedAt { get; init; }
    /// <summary>approve | reject</summary>
    public string? UsedAction { get; init; }
    public DateTime Created { get; init; }
}
