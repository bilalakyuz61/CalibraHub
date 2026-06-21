using System.Text;
using CalibraHub.Application.Abstractions.Services;
using ClosedXML.Excel;

namespace CalibraHub.Infrastructure.Import;

/// <summary>
/// 2026-06-20 — Şablon-tabanlı içe aktarım için Excel (.xlsx) + CSV okuyucu.
/// ClosedXML (xlsx) ve elle CSV ayrıştırma. Salt-okunur; AI içermez.
/// Güvenlik: ilk 20.000 veri satırı (çok büyük dosya context/bellek koruması).
/// </summary>
public sealed class ExcelReader : IExcelReader
{
    private const int MaxRows = 20_000;

    public IReadOnlyList<ExcelSheet> ListSheets(byte[] data, string fileName)
    {
        if (IsCsv(fileName))
        {
            var rows = ParseCsv(data);
            return new[] { new ExcelSheet("Sheet1", rows.Count) };
        }

        using var ms = new MemoryStream(data);
        using var wb = new XLWorkbook(ms);
        return wb.Worksheets
            .Select(ws => new ExcelSheet(ws.Name, ws.RangeUsed()?.RowCount() ?? 0))
            .ToList();
    }

    public ExcelTable Read(byte[] data, string fileName, string? sheetName, int headerRowIndex)
    {
        if (headerRowIndex < 1) headerRowIndex = 1;

        return IsCsv(fileName)
            ? ReadCsv(data, headerRowIndex)
            : ReadXlsx(data, sheetName, headerRowIndex);
    }

    // ── Boş şablon üretimi (.xlsx) ────────────────────────────────────────
    public byte[] WriteTemplate(string sheetName, IReadOnlyList<ExcelTemplateColumn> columns)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(string.IsNullOrWhiteSpace(sheetName) ? "Veri" : sheetName);

        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var cell = ws.Cell(1, i + 1);
            cell.Value = col.Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(col.Required ? "#E0E7FF" : "#F1F5F9");
            cell.Style.Font.FontColor = XLColor.FromHtml("#1E293B");
            ws.Column(i + 1).Width = Math.Max(16, col.Header.Length + 4);
        }
        if (columns.Count > 0)
        {
            ws.SheetView.FreezeRows(1);
            ws.Range(1, 1, 1, columns.Count).SetAutoFilter();
        }

        // İkinci sayfa: alan açıklamaları (zorunlu mu + ipucu)
        var help = wb.Worksheets.Add("Aciklama");
        help.Cell(1, 1).Value = "Kolon";
        help.Cell(1, 2).Value = "Zorunlu";
        help.Cell(1, 3).Value = "Aciklama";
        help.Row(1).Style.Font.Bold = true;
        for (int i = 0; i < columns.Count; i++)
        {
            help.Cell(i + 2, 1).Value = columns[i].Header;
            help.Cell(i + 2, 2).Value = columns[i].Required ? "Evet" : "Hayir";
            help.Cell(i + 2, 3).Value = columns[i].Hint ?? "";
        }
        help.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── XLSX (ClosedXML) ─────────────────────────────────────────────────
    private static ExcelTable ReadXlsx(byte[] data, string? sheetName, int headerRowIndex)
    {
        using var ms = new MemoryStream(data);
        using var wb = new XLWorkbook(ms);

        var ws = !string.IsNullOrWhiteSpace(sheetName)
            ? wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheet(1)
            : wb.Worksheet(1);

        var used = ws.RangeUsed();
        if (used is null)
            return new ExcelTable(ws.Name, Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());

        int firstCol = used.FirstColumn().ColumnNumber();
        int lastCol  = used.LastColumn().ColumnNumber();
        int lastRow  = used.LastRow().RowNumber();

        // Başlıklar
        var headers = new List<string>(lastCol - firstCol + 1);
        for (int c = firstCol; c <= lastCol; c++)
        {
            var h = ws.Cell(headerRowIndex, c).GetFormattedString().Trim();
            headers.Add(string.IsNullOrEmpty(h) ? $"Kolon{c}" : h);
        }

        // Veri satırları
        var rows = new List<IReadOnlyList<string>>();
        for (int r = headerRowIndex + 1; r <= lastRow; r++)
        {
            var cells = new List<string>(headers.Count);
            bool anyValue = false;
            for (int c = firstCol; c <= lastCol; c++)
            {
                var v = ws.Cell(r, c).GetFormattedString();
                if (!string.IsNullOrWhiteSpace(v)) anyValue = true;
                cells.Add(v ?? string.Empty);
            }
            if (!anyValue) continue;     // tamamen boş satır atla
            rows.Add(cells);
            if (rows.Count >= MaxRows) break;
        }

        return new ExcelTable(ws.Name, headers, rows);
    }

    // ── CSV (elle, ; veya , otomatik) ────────────────────────────────────
    private static ExcelTable ReadCsv(byte[] data, int headerRowIndex)
    {
        var lines = ParseCsv(data);
        if (lines.Count == 0 || headerRowIndex > lines.Count)
            return new ExcelTable("Sheet1", Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());

        var headers = lines[headerRowIndex - 1]
            .Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Kolon{i + 1}" : h.Trim())
            .ToList();

        var rows = new List<IReadOnlyList<string>>();
        for (int i = headerRowIndex; i < lines.Count; i++)
        {
            var cells = lines[i];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            // Başlık genişliğine hizala
            var norm = new List<string>(headers.Count);
            for (int c = 0; c < headers.Count; c++)
                norm.Add(c < cells.Count ? cells[c] : string.Empty);
            rows.Add(norm);
            if (rows.Count >= MaxRows) break;
        }

        return new ExcelTable("Sheet1", headers, rows);
    }

    /// <summary>CSV'yi satır → hücre listesine ayrıştır. İlk satıra göre ; veya , seçilir.</summary>
    private static List<List<string>> ParseCsv(byte[] data)
    {
        var text = DecodeText(data);
        var result = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return result;

        // Ayraç tespiti: ilk dolu satırda ; sayısı , sayısından fazlaysa ;
        var firstLine = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
        char delim = firstLine.Count(ch => ch == ';') > firstLine.Count(ch => ch == ',') ? ';' : ',';

        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == delim) { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\r') { /* skip */ }
            else if (ch == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                result.Add(row); row = new List<string>();
            }
            else field.Append(ch);
        }
        // son alan/satır
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); result.Add(row); }
        return result;
    }

    /// <summary>UTF-8 BOM varsa ona göre, yoksa UTF-8 decode.</summary>
    private static string DecodeText(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        return Encoding.UTF8.GetString(data);
    }

    private static bool IsCsv(string fileName) =>
        (fileName ?? "").EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
}
