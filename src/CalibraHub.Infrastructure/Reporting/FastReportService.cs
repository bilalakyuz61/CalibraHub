using System.Data;
using CalibraHub.Application.Abstractions.Services;
using FastReport;
using FastReport.Export.Html;
using FastReport.Export.PdfSimple;
using Microsoft.AspNetCore.Hosting;

namespace CalibraHub.Infrastructure.Reporting;

public sealed class FastReportService : IReportService
{
    private readonly string _webRootPath;

    public FastReportService(IWebHostEnvironment env)
    {
        _webRootPath = env.WebRootPath;
    }

    public async Task<byte[]> ExportPdfAsync(string frxFilePath, DataTable data, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(frxFilePath);

        using var report = new Report();
        report.Load(fullPath);
        report.RegisterData(data, "Data");

        report.Prepare();

        using var ms = new MemoryStream();
        var export = new PDFSimpleExport();
        report.Export(export, ms);
        return ms.ToArray();
    }

    public async Task<string> ExportHtmlAsync(string frxFilePath, DataTable data, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(frxFilePath);

        using var report = new Report();
        report.Load(fullPath);
        report.RegisterData(data, "Data");

        report.Prepare();

        using var ms = new MemoryStream();
        var export = new HTMLExport
        {
            SinglePage = true,
            Navigator = false,
            EmbedPictures = true,
        };
        report.Export(export, ms);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(ct);
    }

    public Task<byte[]> ExportPdfFromBytesAsync(byte[] frxContent, DataTable data, CancellationToken ct = default)
    {
        if (frxContent is null || frxContent.Length == 0)
            throw new InvalidOperationException("FRX icerigi bos.");

        using var report = new Report();
        using (var loadStream = new MemoryStream(frxContent))
        {
            report.Load(loadStream);
        }
        report.RegisterData(data, "Data");
        report.Prepare();

        using var ms = new MemoryStream();
        var export = new PDFSimpleExport();
        report.Export(export, ms);
        return Task.FromResult(ms.ToArray());
    }

    public async Task<string> ExportHtmlFromBytesAsync(byte[] frxContent, DataTable data, CancellationToken ct = default)
    {
        if (frxContent is null || frxContent.Length == 0)
            throw new InvalidOperationException("FRX icerigi bos.");

        using var report = new Report();
        using (var loadStream = new MemoryStream(frxContent))
        {
            report.Load(loadStream);
        }
        report.RegisterData(data, "Data");
        report.Prepare();

        using var ms = new MemoryStream();
        var export = new HTMLExport
        {
            SinglePage = true,
            Navigator = false,
            EmbedPictures = true,
        };
        report.Export(export, ms);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(ct);
    }

    private string ResolvePath(string frxFilePath)
    {
        var fullPath = Path.IsPathRooted(frxFilePath)
            ? frxFilePath
            : Path.Combine(_webRootPath, "Document", frxFilePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"FRX sablon dosyasi bulunamadi: {fullPath}");

        return fullPath;
    }
}
