using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// GenericExportController — CalibraSmartBoard'lardan gelen liste verisini
/// gercek .xlsx Excel dosyasi olarak server-side generate eder. ClosedXML
/// kullanir; client formal POST ile payload yollar, browser dogrudan dosyayi
/// indirir (Content-Disposition: attachment) — iframe blob URL kisitlamalarindan
/// etkilenmez ("Tasinmis, duzenlenmis veya silinmis olabilir" hatasinin sebebi).
///
/// Endpoint:
///   POST /api/export/smartboard-excel  (form-encoded: payload=<json>)
///
/// Payload JSON sekli:
///   {
///     "fileName":  "logistics-material-cards_20260506_142530.xlsx" (optional),
///     "sheetName": "Liste" (optional, max 31 char),
///     "headers":   [ { "id": "code", "label": "Kod" }, ... ],
///     "rows":      [ { "code": "1500.284", "name": "...", ... }, ... ]
///   }
///
/// Header kayit duzeni: koyu zemin, beyaz yazi, bold, autofilter, freeze-pane.
/// Hucre tipleri otomatik: sayi → numeric, ISO tarih (yyyy-MM-dd) → date,
/// bool → "Evet"/"Hayir", obje → display/label/JSON fallback.
/// </summary>
[ApiController]
[Authorize]
[Route("api/export")]
[IgnoreAntiforgeryToken]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.Dashboards)]
public sealed class GenericExportController : ControllerBase
{
    private readonly ILogger<GenericExportController> _logger;

    public GenericExportController(ILogger<GenericExportController> logger)
    {
        _logger = logger;
    }

    [HttpPost("smartboard-excel")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB sinir — buyuk listeler icin
    [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024, ValueLengthLimit = 50 * 1024 * 1024)]
    public IActionResult SmartBoardExcel([FromForm] string? payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return BadRequest(new { success = false, message = "payload bos olamaz." });
        }

        ExportPayload? data;
        try
        {
            data = JsonSerializer.Deserialize<ExportPayload>(payload, _jsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Export] Payload JSON parse hatasi");
            return BadRequest(new { success = false, message = "Gecersiz JSON: " + ex.Message });
        }

        if (data == null || data.Headers == null || data.Rows == null)
        {
            return BadRequest(new { success = false, message = "Headers veya rows eksik." });
        }

        try
        {
            var bytes = BuildExcel(data);
            var fileName = SanitizeFileName(data.FileName ?? ("liste_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx"));
            if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                fileName += ".xlsx";

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Export] Excel olusturma hatasi");
            return StatusCode(500, new { success = false, message = "Excel olusturulamadi: " + "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Rapor (dashboard) cok-sayfa Excel export. Her panel ayri sheet olur.
    /// POST /api/export/report-excel  (form-encoded: payload=<json>)
    /// Payload: { fileName?, sheets: [ { sheetName, headers:[{id,label}], rows:[{...}] } ] }
    /// </summary>
    [HttpPost("report-excel")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024, ValueLengthLimit = 50 * 1024 * 1024)]
    public IActionResult ReportExcel([FromForm] string? payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return BadRequest(new { success = false, message = "payload bos olamaz." });

        ReportExportPayload? data;
        try { data = JsonSerializer.Deserialize<ReportExportPayload>(payload, _jsonOpts); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Export] Report payload parse hatasi");
            return BadRequest(new { success = false, message = "Gecersiz JSON: " + ex.Message });
        }

        if (data?.Sheets == null || data.Sheets.Count == 0)
            return BadRequest(new { success = false, message = "Sheets eksik." });

        try
        {
            using var wb = new XLWorkbook();
            foreach (var s in data.Sheets)
            {
                if (s?.Headers == null || s.Rows == null) continue;
                AppendSheet(wb, s.SheetName, s.Headers, s.Rows);
            }
            if (!wb.Worksheets.Any()) wb.AddWorksheet("Bos");

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var fileName = SanitizeFileName(data.FileName ?? ("rapor_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx"));
            if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) fileName += ".xlsx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Export] Report Excel olusturma hatasi");
            return StatusCode(500, new { success = false, message = "Excel olusturulamadi: " + "İşlem sırasında bir hata oluştu." });
        }
    }

    // Cok-sayfa export icin tek worksheet ekler (benzersiz ad garantili).
    private static void AppendSheet(XLWorkbook wb, string? sheetNameRaw, List<ExportHeader> headers, List<Dictionary<string, JsonElement>> rows)
    {
        var name = (sheetNameRaw ?? "Sayfa").Trim();
        name = new string(name.Where(c => c is not ('\\' or '/' or '*' or '[' or ']' or ':' or '?')).ToArray());
        if (string.IsNullOrWhiteSpace(name)) name = "Sayfa";
        if (name.Length > 31) name = name[..31];
        var baseName = name; var k = 2;
        while (wb.Worksheets.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            var suffix  = " (" + k++ + ")";
            var maxBase = 31 - suffix.Length;
            name = (baseName.Length > maxBase ? baseName[..maxBase] : baseName) + suffix;
        }
        var ws = wb.AddWorksheet(name);

        for (var i = 0; i < headers.Count; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i].Label ?? headers[i].Id ?? string.Empty;
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var rowIdx = 2;
        foreach (var row in rows)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var key = headers[i].Id ?? string.Empty;
                if (string.IsNullOrEmpty(key)) continue;
                if (!row.TryGetValue(key, out var rawValue)) continue;
                WriteCell(ws.Cell(rowIdx, i + 1), rawValue);
            }
            rowIdx++;
        }

        if (rowIdx > 2)
        {
            ws.RangeUsed()?.SetAutoFilter();
            ws.SheetView.FreezeRows(1);
        }
        ws.Columns().AdjustToContents();
        foreach (var col in ws.Columns())
            if (col.Width > 60) col.Width = 60;
    }

    // ── Excel uretimi ────────────────────────────────────────────────────
    private static byte[] BuildExcel(ExportPayload data)
    {
        using var wb = new XLWorkbook();
        var sheetName = (data.SheetName ?? "Liste").Trim();
        if (string.IsNullOrWhiteSpace(sheetName)) sheetName = "Liste";
        // Excel sheet adi 31 karakter max ve bazi karakterler yasak (\, /, *, [, ], :, ?)
        sheetName = new string(sheetName.Where(c => c is not ('\\' or '/' or '*' or '[' or ']' or ':' or '?')).ToArray());
        if (sheetName.Length > 31) sheetName = sheetName[..31];
        var ws = wb.AddWorksheet(sheetName);

        var headers = data.Headers!;
        var rows    = data.Rows!;

        // ── Header satiri ─────────────────────────────────────
        for (var i = 0; i < headers.Count; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i].Label ?? headers[i].Id ?? string.Empty;
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
            c.Style.Font.FontColor       = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            c.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        }

        // ── Veri satirlari ────────────────────────────────────
        var rowIdx = 2;
        foreach (var row in rows)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var key = headers[i].Id ?? string.Empty;
                if (string.IsNullOrEmpty(key)) continue;
                if (!row.TryGetValue(key, out var rawValue)) continue;
                WriteCell(ws.Cell(rowIdx, i + 1), rawValue);
            }
            rowIdx++;
        }

        // ── Sayfa bicimleme ───────────────────────────────────
        if (rowIdx > 2)
        {
            ws.RangeUsed()?.SetAutoFilter();
            ws.SheetView.FreezeRows(1);
        }
        ws.Columns().AdjustToContents();
        // Maksimum kolon genisligi — cok uzun aciklama hucreleri Excel'i bozar
        foreach (var col in ws.Columns())
        {
            if (col.Width > 60) col.Width = 60;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // JsonElement → ClosedXML Cell.Value tip guvenli yazici.
    // Sayilari, tarihleri, bool'lari otomatik dogru tipe cevirir.
    private static void WriteCell(IXLCell cell, JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                cell.Value = string.Empty;
                return;

            case JsonValueKind.True:
                cell.Value = "Evet";
                return;

            case JsonValueKind.False:
                cell.Value = "Hayir";
                return;

            case JsonValueKind.Number:
                if (v.TryGetInt64(out var l))
                {
                    cell.Value = l;
                }
                else if (v.TryGetDouble(out var d))
                {
                    cell.Value = d;
                    cell.Style.NumberFormat.Format = "#,##0.00######";
                }
                return;

            case JsonValueKind.String:
                var s = v.GetString() ?? string.Empty;
                // ISO tarih formatini tespit et: yyyy-MM-dd veya yyyy-MM-ddTHH:mm:ss
                if (TryParseIsoDate(s, out var dt))
                {
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = dt.TimeOfDay == TimeSpan.Zero ? "dd.MM.yyyy" : "dd.MM.yyyy HH:mm";
                }
                else
                {
                    cell.Value = s;
                }
                return;

            case JsonValueKind.Object:
                // Lookup-style: { display, label, value, code, name } — onceligi sirayla
                if (TryGetStringProp(v, out var disp, "display", "label", "name", "code", "value"))
                {
                    cell.Value = disp;
                }
                else
                {
                    cell.Value = v.GetRawText();
                }
                return;

            case JsonValueKind.Array:
                // Multi-select / array degerler — virgul ile birlestir
                var parts = new List<string>();
                foreach (var item in v.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) parts.Add(item.GetString() ?? "");
                    else parts.Add(item.GetRawText());
                }
                cell.Value = string.Join(", ", parts);
                return;
        }
    }

    private static bool TryParseIsoDate(string s, out DateTime dt)
    {
        // Performans: kisa string'lerin tarih olamayacagini erken ele
        if (s.Length < 10 || s.Length > 30) { dt = default; return false; }
        // ISO 8601 formatlarini dene
        return DateTime.TryParseExact(s, new[]
        {
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:ss.fffffffZ",
        }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out dt);
    }

    private static bool TryGetStringProp(JsonElement obj, out string value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            {
                value = p.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(value)) return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static string SanitizeFileName(string name)
    {
        // Path traversal ve gecersiz dosya adi karakterlerini at
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        // / ve \ Windows path separator olabilir, ekstra savunma
        name = name.Replace('/', '_').Replace('\\', '_').Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "liste.xlsx";
        return name;
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── DTO ──────────────────────────────────────────────────────────────
    public sealed class ExportPayload
    {
        public string? FileName { get; set; }
        public string? SheetName { get; set; }
        public List<ExportHeader>? Headers { get; set; }
        public List<Dictionary<string, JsonElement>>? Rows { get; set; }
    }

    public sealed class ReportExportPayload
    {
        public string? FileName { get; set; }
        public List<ExportPayload>? Sheets { get; set; }
    }

    public sealed class ExportHeader
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
    }
}
