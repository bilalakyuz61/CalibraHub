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
            .Where(ws => !IsHelpSheet(ws))   // şablonun "Aciklama" yardım sayfası içe aktarıma sunulmaz
            .Select(ws => new ExcelSheet(ws.Name, ws.RangeUsed()?.RowCount() ?? 0))
            .ToList();
    }

    public ExcelTable Read(byte[] data, string fileName, string? sheetName, int headerRowIndex)
    {
        if (headerRowIndex < 0) headerRowIndex = 0;   // 0 = başlıksız (sentetik "Kolon{n}" adları)

        return IsCsv(fileName)
            ? ReadCsv(data, headerRowIndex)
            : ReadXlsx(data, sheetName, headerRowIndex);
    }

    // ── Boş şablon üretimi (.xlsx) ────────────────────────────────────────
    public byte[] WriteTemplate(string sheetName, IReadOnlyList<ExcelTemplateColumn> columns)
    {
        const int DvLastRow = MaxRows + 1;   // doğrulama kapsamı: başlık + MaxRows veri satırı

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(string.IsNullOrWhiteSpace(sheetName) ? "Veri" : sheetName);

        // 1) Veri sayfası başlıkları
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

        // 2) TEK "Aciklama" sayfası — TRANSPOZE: her alan bir SÜTUN; A sütunu satır etiketleri
        //    (Kolon / Açıklama / Tip / Zorunlu / Anahtar / Değerler). Sınırlı-değerli alanların
        //    değerleri "Değerler" satırından itibaren AŞAĞI doğru, her biri ayrı hücre.
        var help = wb.Worksheets.Add("Aciklama");
        const int valuesStartRow = 6;
        help.Cell(1, 1).Value = "Kolon";
        help.Cell(2, 1).Value = "Açıklama";
        help.Cell(3, 1).Value = "Tip";
        help.Cell(4, 1).Value = "Zorunlu";
        help.Cell(5, 1).Value = "Anahtar";
        help.Cell(valuesStartRow, 1).Value = "Değerler";
        help.Range(1, 1, valuesStartRow, 1).Style.Font.Bold = true;
        help.Column(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");

        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            int c = i + 2;   // A sütunu etiketler; alan sütunları B'den başlar

            var head = help.Cell(1, c);
            head.Value = col.Header;
            head.Style.Font.Bold = true;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml(col.Required ? "#E0E7FF" : "#F1F5F9");
            help.Cell(2, c).Value = col.Hint ?? "";
            help.Cell(3, c).Value = TypeLabel(col);
            help.Cell(4, c).Value = col.Required ? "Evet" : "Hayır";
            help.Cell(5, c).Value = col.CanBeMatchKey ? "Evet" : "Hayır";

            // 3) Hücre veri doğrulaması — enum→liste, sayısal→sayı, tarih→tarih, metin→uzunluk.
            //    Not: Excel doğrulaması YAZARKEN devreye girer (yapıştırılan hücreleri Excel
            //    denetlemez); sunucu tarafı doğrulama her durumda ayrıca çalışır.
            if (col.AllowedValues is { Count: > 0 } av)
            {
                for (int r = 0; r < av.Count; r++)
                    help.Cell(valuesStartRow + r, c).Value = av[r];

                // Veri sayfasındaki kolona açılır liste — kısa → satır içi, uzun → bu sayfadaki değer aralığı
                var dv = ws.Range(2, i + 1, DvLastRow, i + 1).CreateDataValidation();
                var inline = "\"" + string.Join(",", av) + "\"";
                if (inline.Length <= 250)
                    dv.List(inline, true);
                else
                    dv.List(help.Range(valuesStartRow, c, valuesStartRow + av.Count - 1, c), true);
                dv.IgnoreBlanks = true;
                dv.ErrorStyle = XLErrorStyle.Stop;      // yalnızca listedeki değerler (enum kilidi)
                dv.ShowErrorMessage = true;
                dv.ErrorTitle = "Geçersiz Değer";
                dv.ErrorMessage = "Bu alana yalnızca listedeki değerlerden biri girilebilir: "
                                  + string.Join(", ", av.Take(12)) + (av.Count > 12 ? "…" : "");
            }
            else
            {
                var type = (col.DataType ?? "string").ToLowerInvariant();
                bool needsDv = type is "decimal" or "number" or "int" or "date" || col.MaxLength is > 0;
                if (!needsDv) continue;

                var dv = ws.Range(2, i + 1, DvLastRow, i + 1).CreateDataValidation();
                dv.IgnoreBlanks = true;
                dv.ErrorStyle = XLErrorStyle.Stop;
                dv.ShowErrorMessage = true;
                switch (type)
                {
                    case "decimal":
                    case "number":
                        dv.Decimal.Between(-999_999_999_999d, 999_999_999_999d);
                        dv.ErrorTitle = "Sayısal Değer";
                        dv.ErrorMessage = "Bu alana yalnızca sayısal değer girilebilir (örn. 12,50).";
                        break;
                    case "int":
                        dv.WholeNumber.Between(-2_000_000_000, 2_000_000_000);
                        dv.ErrorTitle = "Tam Sayı";
                        dv.ErrorMessage = "Bu alana yalnızca tam sayı girilebilir.";
                        break;
                    case "date":
                        dv.Date.Between(new DateTime(1900, 1, 1), new DateTime(2100, 12, 31));
                        dv.ErrorTitle = "Tarih";
                        dv.ErrorMessage = "Bu alana yalnızca tarih girilebilir (gg.aa.yyyy).";
                        dv.ShowInputMessage = true;
                        dv.InputTitle = "Tarih";
                        dv.InputMessage = "gg.aa.yyyy biçiminde girin.";
                        break;
                    default:   // string + MaxLength
                        dv.TextLength.EqualOrLessThan(col.MaxLength!.Value);
                        dv.ErrorTitle = "Uzunluk Sınırı";
                        dv.ErrorMessage = $"Bu alana en fazla {col.MaxLength.Value} karakter girilebilir.";
                        break;
                }
            }
        }
        help.SheetView.FreezeColumns(1);   // etiket sütunu sabit kalsın
        help.Columns().AdjustToContents();

        // 4) Aciklama sayfası salt-okunur — özet/yardım bilgileri yanlışlıkla silinip bozulmasın.
        //    (Parola gizlilik değil, kazara düzenleme koruması içindir; içe aktarım bu sayfayı
        //    zaten okumaz — bkz. IsHelpSheet.)
        help.Protect("CalibraHub");

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Aciklama sayfası "Tip" satırı etiketi.</summary>
    private static string TypeLabel(ExcelTemplateColumn col)
    {
        var t = (col.DataType ?? "string").ToLowerInvariant();
        if (t == "bool") return "Evet/Hayır";
        if (col.AllowedValues is { Count: > 0 }) return "Liste";
        return t switch
        {
            "decimal" or "number" => "Sayı",
            "int" => "Tam Sayı",
            "date" => "Tarih",
            _ => col.MaxLength is > 0 ? $"Metin (en çok {col.MaxLength})" : "Metin",
        };
    }

    // ── XLSX (ClosedXML) ─────────────────────────────────────────────────
    private static ExcelTable ReadXlsx(byte[] data, string? sheetName, int headerRowIndex)
    {
        using var ms = new MemoryStream(data);
        using var wb = new XLWorkbook(ms);

        var ws = !string.IsNullOrWhiteSpace(sheetName)
            ? wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase)) ?? DefaultDataSheet(wb)
            : DefaultDataSheet(wb);

        var used = ws.RangeUsed();
        if (used is null)
            return new ExcelTable(ws.Name, Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());

        int firstCol = used.FirstColumn().ColumnNumber();
        int lastCol  = used.LastColumn().ColumnNumber();
        int firstRow = used.FirstRow().RowNumber();
        int lastRow  = used.LastRow().RowNumber();
        bool hasHeader = headerRowIndex >= 1;

        // Başlıklar — başlıksız modda (headerRowIndex=0) sentetik "Kolon{n}"
        var headers = new List<string>(lastCol - firstCol + 1);
        for (int c = firstCol; c <= lastCol; c++)
        {
            var h = hasHeader ? ws.Cell(headerRowIndex, c).GetFormattedString().Trim() : "";
            headers.Add(string.IsNullOrEmpty(h) ? $"Kolon{c}" : h);
        }

        // Veri satırları — başlıklıysa header+1'den, başlıksızsa ilk dolu satırdan
        var rows = new List<IReadOnlyList<string>>();
        for (int r = hasHeader ? headerRowIndex + 1 : firstRow; r <= lastRow; r++)
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

        bool hasHeader = headerRowIndex >= 1;
        var headers = hasHeader
            ? lines[headerRowIndex - 1].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Kolon{i + 1}" : h.Trim()).ToList()
            : Enumerable.Range(1, lines.Max(l => l.Count)).Select(i => $"Kolon{i}").ToList();

        var rows = new List<IReadOnlyList<string>>();
        for (int i = hasHeader ? headerRowIndex : 0; i < lines.Count; i++)
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

    /// <summary>Varsayılan veri sayfası: şablonun "Aciklama" yardım sayfası atlanır —
    /// yardım sayfaları içe aktarımı etkilemez.</summary>
    private static IXLWorksheet DefaultDataSheet(XLWorkbook wb)
        => wb.Worksheets.FirstOrDefault(w => !IsHelpSheet(w)) ?? wb.Worksheet(1);

    /// <summary>Bizim ürettiğimiz yardım sayfası mı? (adı "Aciklama" + A1 hücresinde "Kolon" imzası —
    /// kullanıcının kendi dosyasındaki aynı adlı GERÇEK veri sayfaları imza tutmayacağı için etkilenmez.)</summary>
    private static bool IsHelpSheet(IXLWorksheet ws)
        => string.Equals(ws.Name, "Aciklama", StringComparison.OrdinalIgnoreCase)
           && string.Equals(ws.Cell(1, 1).GetString().Trim(), "Kolon", StringComparison.OrdinalIgnoreCase);
}
