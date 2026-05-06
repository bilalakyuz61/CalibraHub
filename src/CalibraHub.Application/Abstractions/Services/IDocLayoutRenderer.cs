using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocLayoutRenderer
{
    byte[] RenderPdf(string layoutJson, IReadOnlyDictionary<string, ReportRawResult> data);
    string RenderHtml(string layoutJson, IReadOnlyDictionary<string, ReportRawResult> data);
}
