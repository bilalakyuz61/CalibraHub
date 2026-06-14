using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IBpmFormRepository
{
    // ── Definition ────────────────────────────────────────────────────────────
    Task<IReadOnlyList<BpmFormDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct = default);
    Task<BpmFormDefinitionDetailDto?>         GetDefinitionDetailAsync(int id, CancellationToken ct = default);
    Task<int>  SaveDefinitionAsync(BpmFormDefinition def, int? actor, CancellationToken ct = default);
    Task       DeleteDefinitionAsync(int id, CancellationToken ct = default);
    Task<int>  SaveFieldAsync(BpmFormField field, int? actor, CancellationToken ct = default);
    Task       DeleteFieldAsync(int fieldId, CancellationToken ct = default);

    // ── Submission ────────────────────────────────────────────────────────────
    Task<IReadOnlyList<BpmFormSubmissionDto>> GetSubmissionsByFormAsync(int formDefinitionId, CancellationToken ct = default);
    Task<IReadOnlyList<BpmFormSubmissionDto>> GetMySubmissionsAsync(string userId, CancellationToken ct = default);
    Task<BpmFormSubmissionDetailDto?>         GetSubmissionDetailAsync(int submissionId, CancellationToken ct = default);
    Task<int>  CreateSubmissionAsync(BpmFormSubmission submission, CancellationToken ct = default);
    Task       UpdateSubmissionStatusAsync(BpmFormSubmission submission, CancellationToken ct = default);
}
