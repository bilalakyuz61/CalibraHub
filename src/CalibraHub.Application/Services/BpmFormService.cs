using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Workflow;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class BpmFormService(
    IBpmFormRepository     formRepo,
    IWorkflowDefinitionRepository defRepo,
    IDocumentContextBuilder contextBuilder,
    WorkflowEngine         engine)
{
    // ── Definition CRUD ──────────────────────────────────────────────────────

    public Task<IReadOnlyList<BpmFormDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct) =>
        formRepo.GetAllDefinitionsAsync(ct);

    public Task<BpmFormDefinitionDetailDto?> GetDefinitionDetailAsync(int id, CancellationToken ct) =>
        formRepo.GetDefinitionDetailAsync(id, ct);

    public async Task<int> SaveDefinitionAsync(SaveBpmFormDefinitionRequest req, int? actor, CancellationToken ct)
    {
        var def = new BpmFormDefinition
        {
            Id                   = req.Id ?? 0,
            Name                 = req.Name.Trim(),
            Code                 = ToCode(req.Name),
            Description          = req.Description,
            WorkflowDefinitionId = req.WorkflowDefinitionId,
            IsActive             = req.IsActive,
        };
        return await formRepo.SaveDefinitionAsync(def, actor, ct);
    }

    public Task DeleteDefinitionAsync(int id, CancellationToken ct) =>
        formRepo.DeleteDefinitionAsync(id, ct);

    public Task<int> SaveFieldAsync(SaveBpmFormFieldRequest req, int? actor, CancellationToken ct)
    {
        var field = new BpmFormField
        {
            Id               = req.Id ?? 0,
            FormDefinitionId = req.FormDefinitionId,
            Key              = req.Key.Trim(),
            Label            = req.Label.Trim(),
            FieldType        = req.FieldType,
            IsRequired       = req.IsRequired,
            SortOrder        = req.SortOrder,
            OptionsJson      = req.OptionsJson,
            Placeholder      = req.Placeholder,
            DefaultValue     = req.DefaultValue,
            LayoutRow        = req.LayoutRow,
            LayoutCol        = req.LayoutCol,
            LayoutColSpan    = req.LayoutColSpan,
        };
        return formRepo.SaveFieldAsync(field, actor, ct);
    }

    public Task DeleteFieldAsync(int fieldId, CancellationToken ct) =>
        formRepo.DeleteFieldAsync(fieldId, ct);

    // ── Submission ───────────────────────────────────────────────────────────

    public Task<IReadOnlyList<BpmFormSubmissionDto>> GetMySubmissionsAsync(string userId, CancellationToken ct) =>
        formRepo.GetMySubmissionsAsync(userId, ct);

    public Task<BpmFormSubmissionDetailDto?> GetSubmissionDetailAsync(int id, CancellationToken ct) =>
        formRepo.GetSubmissionDetailAsync(id, ct);

    /// <summary>
    /// Formu kaydet + workflow başlat (varsa).
    /// </summary>
    public async Task<int> SubmitFormAsync(SubmitBpmFormRequest req, int? actorId, CancellationToken ct)
    {
        var defDetail = await formRepo.GetDefinitionDetailAsync(req.FormDefinitionId, ct)
            ?? throw new InvalidOperationException($"Form tanımı bulunamadı: {req.FormDefinitionId}");

        // Zorunlu alan kontrolü
        var required = defDetail.Fields.Where(f => f.IsRequired).Select(f => f.Key).ToHashSet();
        var provided = req.Values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => v.FieldKey).ToHashSet();
        var missing  = required.Except(provided).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Zorunlu alanlar eksik: {string.Join(", ", missing)}");

        // Submission oluştur
        var submission = new BpmFormSubmission
        {
            FormDefinitionId = req.FormDefinitionId,
            CreatedById      = actorId,
        };
        submission.Submit(actorId?.ToString());
        foreach (var v in req.Values)
            submission.AddValue(new BpmFormSubmissionValue { FieldKey = v.FieldKey, Value = v.Value });

        var submissionId = await formRepo.CreateSubmissionAsync(submission, ct);
        submission.Id = submissionId;

        // Workflow başlat
        if (defDetail.Definition.WorkflowDefinitionId.HasValue)
        {
            var ctx     = await contextBuilder.BuildContextAsync(submissionId, ct);
            var ctxJson = System.Text.Json.JsonSerializer.Serialize(ctx);
            var instance = await engine.StartAsync(
                sourceId:       submissionId,
                definitionId:   defDetail.Definition.WorkflowDefinitionId.Value,
                contextJson:    ctxJson,
                startedBy:      actorId,
                ct:             ct,
                sourceType:     "Form");
            submission.LinkWorkflow(instance.Id);
            await formRepo.UpdateSubmissionStatusAsync(submission, ct);
        }

        return submissionId;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ToCode(string name)
    {
        var slug = Regex.Replace(name.Trim().ToUpperInvariant(), @"[^A-Z0-9]+", "_");
        return slug.Trim('_')[..Math.Min(slug.Length, 50)];
    }
}
