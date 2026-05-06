using System.Data;
using System.Text;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CalibraHub.Infrastructure.Scheduling;

/// <summary>
/// Bir DB view'ini sorgulayip sonucu rapor dosyasi (CSV/XLSX/PDF) halinde belirtilen
/// alicilarin mail adresine gonderen executor.
/// ParametersJson format:
///   {
///     "viewName":   "vw_DailySales",
///     "recipients": ["a@b.com", "c@d.com"],
///     "format":     "csv" | "xlsx" | "pdf",
///     "subject":    "...",  // optional, {date}/{time}/{taskName} placeholders
///     "bodyText":   "...",  // optional
///     "maxRows":    10000   // optional, default 10000
///   }
/// </summary>
public sealed class ViewReportTaskExecutor : IScheduledTaskExecutor
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IEmailSender _emailSender;

    public ViewReportTaskExecutor(SqlServerConnectionFactory connectionFactory, IEmailSender emailSender)
    {
        _connectionFactory = connectionFactory;
        _emailSender = emailSender;

        // QuestPDF Community license — saying "I'm eligible" explicitly (small company, <$1M rev)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ScheduledTaskType SupportedType => ScheduledTaskType.ViewReport;

    public async Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        ViewReportConfig? cfg;
        try { cfg = ParseConfig(task.ParametersJson); }
        catch (Exception ex) { return TaskExecutionResult.Error("Parametre parse hatasi: " + ex.Message); }

        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ViewName))
            return TaskExecutionResult.Error("viewName parametresi gerekli.");
        if (cfg.Recipients is null || cfg.Recipients.Count == 0)
            return TaskExecutionResult.Error("En az bir alici mail adresi gerekli.");
        if (!IsSafeViewName(cfg.ViewName))
            return TaskExecutionResult.Error("Gecersiz view adi (sadece harf/rakam/alt cizgi/nokta).");

        var maxRows = cfg.MaxRows > 0 && cfg.MaxRows <= 1_000_000 ? cfg.MaxRows : 10_000;

        // 1) View verisini cek
        var table = new DataTable();
        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT TOP ({maxRows}) * FROM {QuoteIdentifier(cfg.ViewName)};";
            cmd.CommandTimeout = 120;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            table.Load(reader);
        }
        catch (SqlException ex)
        {
            return TaskExecutionResult.Error($"View sorgusu basarisiz ({cfg.ViewName}): {ex.Message}");
        }

        // 2) Format'a gore byte[] rapor uret
        var format = (cfg.Format ?? "csv").Trim().ToLowerInvariant();
        byte[] bytes; string fileName; string contentType;
        try
        {
            var baseName = Sanitize(cfg.ViewName);
            switch (format)
            {
                case "xlsx":
                    bytes = BuildXlsx(table, baseName);
                    fileName = baseName + ".xlsx";
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    break;
                case "pdf":
                    bytes = BuildPdf(table, task.Name ?? baseName);
                    fileName = baseName + ".pdf";
                    contentType = "application/pdf";
                    break;
                case "csv":
                default:
                    bytes = BuildCsv(table);
                    fileName = baseName + ".csv";
                    contentType = "text/csv";
                    break;
            }
        }
        catch (Exception ex)
        {
            return TaskExecutionResult.Error($"Rapor dosyasi olusturulamadi: {ex.Message}");
        }

        // 3) Mail subject/body placeholder genislet
        var subject = ApplyPlaceholders(cfg.Subject ?? $"{task.Name} — Rapor", task.Name);
        var body    = ApplyPlaceholders(cfg.BodyText ?? $"Ekte {cfg.ViewName} view'inden üretilmiş rapor yer almaktadır.", task.Name);

        // 4) Mail gonder
        // companyId: ScheduledTask entity'sinde company_id yok — sirket SMTP'si "ilk aktif" olandan alinir
        var result = await _emailSender.SendAsync(
            0,
            cfg.Recipients,
            subject,
            body,
            new[] { new EmailAttachment(fileName, bytes, contentType) },
            cancellationToken);

        return result.Status switch
        {
            EmailStatus.Sent    => TaskExecutionResult.Success(
                $"{table.Rows.Count} satir / {FormatBytes(bytes.Length)} — {cfg.Recipients.Count} aliciya gonderildi."),
            EmailStatus.Skipped => TaskExecutionResult.Error("Mail atlandi: " + (result.Message ?? "(bos)")),
            EmailStatus.Failed  => TaskExecutionResult.Error("Mail hatasi: " + (result.Message ?? "(bos)")),
            _                   => TaskExecutionResult.Error("Bilinmeyen mail durumu."),
        };
    }

    private static ViewReportConfig? ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<ViewReportConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
    }

    private static bool IsSafeViewName(string name)
    {
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '[' || c == ']')) return false;
        }
        return name.Length > 0 && name.Length < 200;
    }

    private static string QuoteIdentifier(string name)
    {
        // "schema.view" veya "view" — tiap parcayi koseli parantez ile sar
        var parts = name.Split('.', 2);
        if (parts.Length == 1) return "[" + parts[0].Trim('[', ']') + "]";
        return "[" + parts[0].Trim('[', ']') + "].[" + parts[1].Trim('[', ']') + "]";
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        }
        return sb.ToString();
    }

    private static string ApplyPlaceholders(string template, string? taskName)
    {
        var now = DateTime.Now;
        return template
            .Replace("{date}", now.ToString("dd.MM.yyyy"))
            .Replace("{time}", now.ToString("HH:mm"))
            .Replace("{datetime}", now.ToString("dd.MM.yyyy HH:mm"))
            .Replace("{taskName}", taskName ?? "");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("F2") + " MB";
    }

    // ── CSV (UTF-8 with BOM — Excel'de turkce karakterler dogru gorunur) ──────
    private static byte[] BuildCsv(DataTable t)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < t.Columns.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(EscapeCsv(t.Columns[i].ColumnName));
        }
        sb.AppendLine();
        foreach (DataRow row in t.Rows)
        {
            for (var i = 0; i < t.Columns.Count; i++)
            {
                if (i > 0) sb.Append(';');
                var v = row[i];
                sb.Append(EscapeCsv(v is DBNull ? "" : Convert.ToString(v) ?? ""));
            }
            sb.AppendLine();
        }
        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        var combined = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, combined, 0, preamble.Length);
        Buffer.BlockCopy(content,  0, combined, preamble.Length, content.Length);
        return combined;
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    // ── XLSX ──────────────────────────────────────────────────────────────────
    private static byte[] BuildXlsx(DataTable t, string sheetName)
    {
        using var wb = new XLWorkbook();
        var safeName = sheetName.Length > 30 ? sheetName.Substring(0, 30) : sheetName;
        var ws = wb.AddWorksheet(string.IsNullOrWhiteSpace(safeName) ? "Rapor" : safeName);
        ws.Cell(1, 1).InsertTable(t.AsEnumerable().Any() ? t : new DataTable { TableName = "Rapor" });
        // InsertTable ile bos datatable crash edebilir — manual fallback
        if (!t.AsEnumerable().Any())
        {
            for (var c = 0; c < t.Columns.Count; c++)
                ws.Cell(1, c + 1).Value = t.Columns[c].ColumnName;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── PDF (QuestPDF) ────────────────────────────────────────────────────────
    private static byte[] BuildPdf(DataTable t, string title)
    {
        var doc = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(title).FontSize(14).SemiBold();
                    col.Item().Text($"Olusturuldu: {DateTime.Now:dd.MM.yyyy HH:mm}  ·  Toplam: {t.Rows.Count} satir")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(8).Table(tbl =>
                {
                    tbl.ColumnsDefinition(cols =>
                    {
                        for (var i = 0; i < t.Columns.Count; i++) cols.RelativeColumn();
                    });
                    tbl.Header(h =>
                    {
                        foreach (System.Data.DataColumn c in t.Columns)
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                .Text(c.ColumnName).SemiBold().FontSize(9);
                        }
                    });
                    foreach (DataRow row in t.Rows)
                    {
                        for (var i = 0; i < t.Columns.Count; i++)
                        {
                            var v = row[i];
                            tbl.Cell().BorderBottom(.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .Text(v is DBNull ? "" : Convert.ToString(v) ?? "").FontSize(8);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(t => {
                    t.Span("Sayfa ").FontSize(7);
                    t.CurrentPageNumber().FontSize(7);
                    t.Span(" / ").FontSize(7);
                    t.TotalPages().FontSize(7);
                });
            });
        });
        return doc.GeneratePdf();
    }

    private sealed class ViewReportConfig
    {
        public string? ViewName { get; set; }
        public List<string>? Recipients { get; set; }
        public string? Format { get; set; }
        public string? Subject { get; set; }
        public string? BodyText { get; set; }
        public int MaxRows { get; set; }
    }
}
