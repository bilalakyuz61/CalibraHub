using CalibraHub.Application.Abstractions.Persistence;

namespace CalibraHub.Application.Workflow;

/// <summary>
/// BpmFormSubmission değerlerini WorkflowEngine NCalc context'ine çevirir.
/// Form alanlarının Key'leri doğrudan NCalc değişken adı olur.
/// ör. key="tahminiTutar", value="15000" → context["tahminiTutar"] = 15000.0
/// </summary>
public sealed class BpmFormContextBuilder(IBpmFormRepository formRepo) : IDocumentContextBuilder
{
    public async Task<Dictionary<string, object?>> BuildContextAsync(int sourceId, CancellationToken ct = default)
    {
        var detail = await formRepo.GetSubmissionDetailAsync(sourceId, ct);
        if (detail is null) return [];

        var ctx = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var val in detail.Values)
        {
            var raw = val.Value;
            if (raw is null) { ctx[val.FieldKey] = null; continue; }

            // Sayıya çevrilebiliyorsa double, değilse string
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var d))
                ctx[val.FieldKey] = d;
            else if (bool.TryParse(raw, out var b))
                ctx[val.FieldKey] = b;
            else
                ctx[val.FieldKey] = raw;
        }

        // Standart meta değişkenler
        ctx["FormName"]       = detail.Form.Definition.Name;
        ctx["SubmittedBy"]    = detail.Submission.SubmittedBy;
        ctx["FormDefinitionId"] = (double)detail.Submission.FormDefinitionId;

        return ctx;
    }
}
