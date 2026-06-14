using System.Text;
using CalibraHub.Application.Abstractions.Services;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace CalibraHub.Infrastructure.DocumentExtraction;

/// <summary>
/// 2026-05-24 — Calibo dokuman extractor. Excel/PDF/DOCX binary'lerinden text uretir.
///
/// Limit: ilk ~50.000 karakter (cok buyuk dokuman'lar context limiti astirir).
/// </summary>
public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    private const int MaxOutputChars = 50_000;

    public bool Supports(string mimeType, string fileName)
    {
        var mt = (mimeType ?? "").ToLowerInvariant();
        var fn = (fileName ?? "").ToLowerInvariant();
        return mt == "application/pdf" || fn.EndsWith(".pdf")
            || mt == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" || fn.EndsWith(".xlsx")
            || mt == "application/vnd.ms-excel" || fn.EndsWith(".xls")
            || mt == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" || fn.EndsWith(".docx");
    }

    public string? ExtractText(byte[] data, string mimeType, string fileName)
    {
        if (data == null || data.Length == 0) return null;
        var mt = (mimeType ?? "").ToLowerInvariant();
        var fn = (fileName ?? "").ToLowerInvariant();

        try
        {
            // PDF
            if (mt == "application/pdf" || fn.EndsWith(".pdf"))
                return ExtractPdf(data);

            // Excel (xlsx)
            if (mt == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" || fn.EndsWith(".xlsx")
                || mt == "application/vnd.ms-excel" || fn.EndsWith(".xls"))
                return ExtractExcel(data);

            // Word (docx)
            if (mt == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" || fn.EndsWith(".docx"))
                return ExtractDocx(data);
        }
        catch (Exception ex)
        {
            return $"[Dokuman okunamadi: {ex.Message}]";
        }
        return null;
    }

    // ── PDF (PdfPig) ─────────────────────────────────────────────

    private static string ExtractPdf(byte[] data)
    {
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(data);
        var pageCount = doc.NumberOfPages;
        for (int i = 1; i <= pageCount; i++)
        {
            var page = doc.GetPage(i);
            sb.AppendLine($"───── Sayfa {i}/{pageCount} ─────");
            sb.AppendLine(page.Text);
            sb.AppendLine();
            if (sb.Length > MaxOutputChars) { sb.AppendLine("[...sonraki sayfalar kısaltıldı...]"); break; }
        }
        return Truncate(sb.ToString());
    }

    // ── Excel (ClosedXML) ────────────────────────────────────────

    private static string ExtractExcel(byte[] data)
    {
        var sb = new StringBuilder();
        using var ms = new MemoryStream(data);
        using var wb = new XLWorkbook(ms);
        foreach (var ws in wb.Worksheets)
        {
            sb.AppendLine($"───── Sayfa: {ws.Name} ─────");
            var used = ws.RangeUsed();
            if (used == null)
            {
                sb.AppendLine("(boş)");
                continue;
            }
            int firstRow = used.FirstRow().RowNumber();
            int lastRow = used.LastRow().RowNumber();
            int firstCol = used.FirstColumn().ColumnNumber();
            int lastCol = used.LastColumn().ColumnNumber();

            // Tab ile ayrılmış (TSV) format — model okumakta daha rahat
            for (int r = firstRow; r <= lastRow; r++)
            {
                var row = new StringBuilder();
                for (int c = firstCol; c <= lastCol; c++)
                {
                    if (c > firstCol) row.Append('\t');
                    var cell = ws.Cell(r, c);
                    var val = cell.IsEmpty() ? "" : cell.GetFormattedString();
                    row.Append(val);
                }
                sb.AppendLine(row.ToString());
                if (sb.Length > MaxOutputChars) { sb.AppendLine("[...kalan satırlar kısaltıldı...]"); goto end; }
            }
            sb.AppendLine();
        }
        end:
        return Truncate(sb.ToString());
    }

    // ── DOCX (OpenXml) ───────────────────────────────────────────

    private static string ExtractDocx(byte[] data)
    {
        var sb = new StringBuilder();
        using var ms = new MemoryStream(data);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "(boş döküman)";

        foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            var text = para.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine(text);
            if (sb.Length > MaxOutputChars) { sb.AppendLine("[...kalan kısım kısaltıldı...]"); break; }
        }
        return Truncate(sb.ToString());
    }

    private static string Truncate(string s)
        => s.Length <= MaxOutputChars ? s : s.Substring(0, MaxOutputChars) + "\n[...]";
}
