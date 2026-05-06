using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CalibraHub.Infrastructure.Reporting;

/// <summary>
/// LayoutJson → QuestPDF (PDF) veya basit HTML (önizleme) dönüştürücü.
/// Renderer salt veri alır — SQL çalıştırmaz; orchestration DocDesignerService'dedir.
/// </summary>
public sealed class DocLayoutRenderer : IDocLayoutRenderer
{
    public byte[] RenderPdf(string layoutJson, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var doc = ParseLayout(layoutJson);
        var bands = doc.Bands;

        var pageHeader   = bands.FirstOrDefault(b => b.Type == "PageHeader");
        var docHeader    = bands.FirstOrDefault(b => b.Type == "DocumentHeader");
        var tableHeader  = bands.FirstOrDefault(b => b.Type == "TableHeader");
        var detailBand   = bands.FirstOrDefault(b => b.Type == "Detail");
        var totalsBand   = bands.FirstOrDefault(b => b.Type == "TotalsBlock");
        var sigBand      = bands.FirstOrDefault(b => b.Type == "SignatureBlock");
        var pageFooter   = bands.FirstOrDefault(b => b.Type == "PageFooter");

        var contentWidth = doc.PageWidth - doc.Margins.Left - doc.Margins.Right;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size((float)doc.PageWidth, (float)doc.PageHeight, Unit.Millimetre);
                page.MarginTop((float)doc.Margins.Top, Unit.Millimetre);
                page.MarginBottom((float)doc.Margins.Bottom, Unit.Millimetre);
                page.MarginLeft((float)doc.Margins.Left, Unit.Millimetre);
                page.MarginRight((float)doc.Margins.Right, Unit.Millimetre);

                if (pageHeader != null)
                    page.Header().MinHeight((float)pageHeader.Height, Unit.Millimetre)
                        .Element(c => RenderBandContent(c, pageHeader, data, contentWidth));

                page.Content().Column(col =>
                {
                    col.Spacing(0);

                    if (docHeader != null)
                        col.Item().MinHeight((float)docHeader.Height, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, docHeader, data, contentWidth));

                    // Detail bölümü — TableHeader her sayfada tekrar eder
                    if (detailBand != null)
                    {
                        var alias = detailBand.DataAlias ?? "Detail";
                        data.TryGetValue(alias, out var detailData);
                        var colDefs = BuildColumnDefs(detailBand, contentWidth);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                foreach (var cd in colDefs)
                                    cols.RelativeColumn((float)cd.RelWidth);
                            });

                            if (tableHeader != null)
                            {
                                table.Header(header =>
                                {
                                    foreach (var cd in colDefs)
                                    {
                                        var el = tableHeader.Elements
                                            .OrderBy(e => e.X)
                                            .ElementAtOrDefault(colDefs.IndexOf(cd));

                                        header.Cell()
                                            .Background(el?.Style?.BgColor ?? "#EEEEEE")
                                            .Border(0.3f)
                                            .Padding(1, Unit.Millimetre)
                                            .Text(el?.Text ?? cd.ColName)
                                                .FontSize(el?.Style?.FontSize ?? 9)
                                                .Bold();
                                    }
                                });
                            }

                            if (detailData != null)
                            {
                                var colIndex = BuildColIndex(detailData);
                                foreach (var row in detailData.Rows)
                                {
                                    foreach (var cd in colDefs)
                                    {
                                        var cellVal = cd.ColName != null && colIndex.TryGetValue(cd.ColName, out var ci)
                                            ? FormatValue(row.Count > ci ? row[ci] : null, cd.Format)
                                            : "";

                                        table.Cell()
                                            .Border(0.2f)
                                            .Padding(1, Unit.Millimetre)
                                            .Text(cellVal)
                                                .FontSize(cd.FontSize)
                                                .FontColor(cd.Color ?? Colors.Black);
                                    }
                                }
                            }
                        });
                    }

                    if (totalsBand != null)
                        col.Item().MinHeight((float)totalsBand.Height, Unit.Millimetre)
                            .PaddingTop(2, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, totalsBand, data, contentWidth));

                    if (sigBand != null)
                        col.Item().MinHeight((float)sigBand.Height, Unit.Millimetre)
                            .PaddingTop(4, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, sigBand, data, contentWidth));
                });

                if (pageFooter != null)
                    page.Footer().MinHeight((float)pageFooter.Height, Unit.Millimetre)
                        .Element(c => RenderBandContent(c, pageFooter, data, contentWidth));
            });
        }).GeneratePdf();
    }

    public string RenderHtml(string layoutJson, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var doc = ParseLayout(layoutJson);
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<style>*{box-sizing:border-box;margin:0;padding:0;font-family:Arial,sans-serif;font-size:10pt;}");
        sb.AppendLine(".page{width:794px;min-height:1123px;background:#fff;margin:20px auto;padding:38px 57px 38px 57px;box-shadow:0 2px 8px rgba(0,0,0,.15);}");
        sb.AppendLine(".band{position:relative;width:100%;margin-bottom:2px;}");
        sb.AppendLine(".band-label{font-size:8px;color:#999;background:#f5f5f5;padding:1px 4px;border-left:3px solid #ccc;margin-bottom:1px;}");
        sb.AppendLine(".el{position:absolute;overflow:hidden;font-size:10pt;}");
        sb.AppendLine("table.detail{width:100%;border-collapse:collapse;}");
        sb.AppendLine("table.detail th,table.detail td{border:1px solid #ddd;padding:2px 4px;font-size:9pt;}");
        sb.AppendLine("table.detail th{background:#eee;font-weight:bold;}");
        sb.AppendLine("</style></head><body><div class='page'>");

        foreach (var band in doc.Bands)
        {
            var heightPx = (double)band.Height * 3.78;
            sb.AppendLine($"<div class='band' style='height:{heightPx:F0}px;'>");
            sb.AppendLine($"<div class='band-label'>{band.Type}</div>");

            if (band.Type == "Detail")
            {
                var alias = band.DataAlias ?? "Detail";
                data.TryGetValue(alias, out var detailData);
                var elements = band.Elements.OrderBy(e => e.X).ToList();

                sb.AppendLine("<table class='detail'><thead><tr>");
                foreach (var el in elements)
                    sb.AppendLine($"<th>{(el.Text ?? el.Binding?.Col ?? "")}</th>");
                sb.AppendLine("</tr></thead><tbody>");

                if (detailData != null)
                {
                    var colIndex = BuildColIndex(detailData);
                    foreach (var row in detailData.Rows)
                    {
                        sb.AppendLine("<tr>");
                        foreach (var el in elements)
                        {
                            var val = "";
                            if (el.Binding?.Col != null && colIndex.TryGetValue(el.Binding.Col, out var ci))
                                val = FormatValue(row.Count > ci ? row[ci] : null, el.Format);
                            sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(val)}</td>");
                        }
                        sb.AppendLine("</tr>");
                    }
                }

                sb.AppendLine("</tbody></table>");
            }
            else
            {
                // Elements sıralanır: JSON'daki dizi sırası = z-index (son = üstte)
                foreach (var el in band.Elements)
                {
                    var xPx = el.X * 3.78;
                    var yPx = el.Y * 3.78;
                    var wPx = el.W * 3.78;
                    var hPx = el.H * 3.78;

                    var style = new StringBuilder($"left:{xPx:F0}px;top:{yPx:F0}px;width:{wPx:F0}px;height:{hPx:F0}px;");

                    var s = el.Style;
                    if (s != null)
                    {
                        if (s.FontSize > 0) style.Append($"font-size:{s.FontSize}pt;");
                        if (s.Bold) style.Append("font-weight:bold;");
                        if (s.Italic) style.Append("font-style:italic;");
                        if (!string.IsNullOrEmpty(s.Color)) style.Append($"color:{s.Color};");
                        if (!string.IsNullOrEmpty(s.BgColor) && s.BgColor != "transparent")
                            style.Append($"background:{s.BgColor};");
                        if (s.Border) style.Append("border:1px solid #999;");
                        var align = s.Align switch { "center" => "center", "right" => "right", "justify" => "justify", _ => "left" };
                        style.Append($"text-align:{align};");
                    }

                    var content = el.Kind switch
                    {
                        "Label"        => System.Net.WebUtility.HtmlEncode(el.Text ?? ""),
                        "BoundField"   => ResolveField(el, data),
                        "PageNumber"   => "<span style='color:#888'>[Sayfa No]</span>",
                        "DateTimeNow"  => DateTime.Now.ToString("dd.MM.yyyy"),
                        "Image"        => "<span style='color:#888;font-size:8pt'>[Görsel]</span>",
                        "AmountInWords"=> ResolveAmountInWords(el, data),
                        _              => System.Net.WebUtility.HtmlEncode(el.Text ?? "")
                    };

                    sb.AppendLine($"<div class='el' style='{style}'>{content}</div>");
                }
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RenderBandContent(IContainer container,
        LayoutBand band, IReadOnlyDictionary<string, ReportRawResult> data, decimal contentWidthMm)
    {
        // Elements JSON array sırası z-index'i belirler (last = top).
        // Statik bantlar için Row() ile x pozisyonuna göre sıralı render.
        var sorted = band.Elements.OrderBy(e => e.X).ToList();
        if (sorted.Count == 0) return;

        container.Row(row =>
        {
            foreach (var el in sorted)
            {
                var relW = contentWidthMm > 0 ? (float)(el.W / (double)contentWidthMm) : 0.1f;
                var cell = row.RelativeItem(Math.Max(relW, 0.05f));

                var text = el.Kind switch
                {
                    "Label"       => el.Text ?? "",
                    "BoundField"  => ResolveFieldRaw(el, data),
                    "PageNumber"  => null,  // handled below
                    "DateTimeNow" => DateTime.Now.ToString("dd.MM.yyyy"),
                    "AmountInWords" => ResolveAmountInWordsRaw(el, data),
                    _             => el.Text ?? ""
                };

                var s = el.Style;
                if (el.Kind == "PageNumber")
                {
                    cell.Text(t =>
                    {
                        t.Span("Sayfa "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                        if (s?.FontSize > 0) t.DefaultTextStyle(x => x.FontSize(s.FontSize));
                    });
                }
                else if (!string.IsNullOrEmpty(s?.BgColor) && s.BgColor != "transparent")
                {
                    cell.Background(s.BgColor).Padding(1, Unit.Millimetre).Text(t =>
                    {
                        BuildTextStyle(t.Span(text ?? ""), s);
                    });
                }
                else
                {
                    cell.Padding(1, Unit.Millimetre).Text(t =>
                    {
                        BuildTextStyle(t.Span(text ?? ""), s);
                    });
                }
            }
        });
    }

    private static void BuildTextStyle(TextSpanDescriptor span, ElementStyle? s)
    {
        if (s == null) return;
        if (s.FontSize > 0) span.FontSize(s.FontSize);
        if (s.Bold) span.Bold();
        if (s.Italic) span.Italic();
        if (!string.IsNullOrEmpty(s.Color)) span.FontColor(s.Color);
    }

    private sealed record ColDef(string? ColName, string? Format, double RelWidth, float FontSize, string? Color)
    {
        public int Index { get; set; }
    }

    private static List<ColDef> BuildColumnDefs(LayoutBand detailBand, decimal contentWidthMm)
    {
        var elements = detailBand.Elements.OrderBy(e => e.X).ToList();
        if (elements.Count == 0) return [];
        var totalW = elements.Sum(e => e.W);
        if (totalW <= 0) totalW = (double)contentWidthMm;
        var defs = elements.Select((el, i) => new ColDef(
            ColName: el.Binding?.Col,
            Format: el.Format,
            RelWidth: el.W / totalW,
            FontSize: el.Style?.FontSize ?? 9f,
            Color: el.Style?.Color
        ) { Index = i }).ToList();
        return defs;
    }

    private static Dictionary<string, int> BuildColIndex(ReportRawResult result)
    {
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < result.ColumnNames.Count; i++)
            idx[result.ColumnNames[i]] = i;
        return idx;
    }

    private static string ResolveField(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        if (el.Binding == null) return "";
        if (!data.TryGetValue(el.Binding.Alias, out var result)) return "";
        if (result.Rows.Count == 0) return "";
        var colIndex = BuildColIndex(result);
        if (!colIndex.TryGetValue(el.Binding.Col, out var ci)) return "";
        return System.Net.WebUtility.HtmlEncode(FormatValue(result.Rows[0].Count > ci ? result.Rows[0][ci] : null, el.Format));
    }

    private static string ResolveFieldRaw(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        if (el.Binding == null) return "";
        if (!data.TryGetValue(el.Binding.Alias, out var result)) return "";
        if (result.Rows.Count == 0) return "";
        var colIndex = BuildColIndex(result);
        if (!colIndex.TryGetValue(el.Binding.Col, out var ci)) return "";
        return FormatValue(result.Rows[0].Count > ci ? result.Rows[0][ci] : null, el.Format);
    }

    private static string ResolveAmountInWords(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var raw = ResolveField(el, data);
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt)) return raw;
        return NumberToWordsTr(amt);
    }

    private static string ResolveAmountInWordsRaw(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var raw = ResolveFieldRaw(el, data);
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt)) return raw;
        return NumberToWordsTr(amt);
    }

    private static string FormatValue(object? val, string? format)
    {
        if (val == null || val is DBNull) return "";
        if (!string.IsNullOrEmpty(format))
        {
            if (val is decimal d) return d.ToString(format, new CultureInfo("tr-TR"));
            if (val is double dbl) return dbl.ToString(format, new CultureInfo("tr-TR"));
            if (val is DateTime dt && format.Contains('d', StringComparison.OrdinalIgnoreCase))
                return dt.ToString(format, new CultureInfo("tr-TR"));
        }
        return val.ToString() ?? "";
    }

    // ── Turkish amount-in-words ───────────────────────────────────────────────

    private static string NumberToWordsTr(decimal amount)
    {
        var whole = (long)Math.Floor(amount);
        var cents = (int)((amount - whole) * 100);
        var result = WholeToWordsTr(whole) + " Türk Lirası";
        if (cents > 0)
            result += " " + WholeToWordsTr(cents) + " Kuruş";
        return result;
    }

    private static readonly string[] Units = ["", "Bir", "İki", "Üç", "Dört", "Beş", "Altı", "Yedi", "Sekiz", "Dokuz"];
    private static readonly string[] Tens = ["", "On", "Yirmi", "Otuz", "Kırk", "Elli", "Altmış", "Yetmiş", "Seksen", "Doksan"];

    private static string WholeToWordsTr(long n)
    {
        if (n == 0) return "Sıfır";
        if (n < 0) return "Eksi " + WholeToWordsTr(-n);
        var sb = new StringBuilder();
        if (n >= 1_000_000_000) { sb.Append(WholeToWordsTr(n / 1_000_000_000) + " Milyar "); n %= 1_000_000_000; }
        if (n >= 1_000_000)     { sb.Append(WholeToWordsTr(n / 1_000_000) + " Milyon "); n %= 1_000_000; }
        if (n >= 1_000)
        {
            var thousands = n / 1_000;
            sb.Append(thousands == 1 ? "Bin " : WholeToWordsTr(thousands) + " Bin ");
            n %= 1_000;
        }
        if (n >= 100) { sb.Append(n / 100 == 1 ? "Yüz " : Units[n / 100] + "yüz "); n %= 100; }
        if (n >= 10)  { sb.Append(Tens[n / 10] + " "); n %= 10; }
        if (n > 0)    sb.Append(Units[n] + " ");
        return sb.ToString().Trim();
    }

    // ── JSON deserialization ──────────────────────────────────────────────────

    private static LayoutDoc ParseLayout(string json) =>
        JsonSerializer.Deserialize<LayoutDoc>(json, JsonOpts) ?? throw new InvalidOperationException("Geçersiz LayoutJson.");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record LayoutDoc(
        [property: System.Text.Json.Serialization.JsonPropertyName("pageWidth")]  decimal PageWidth,
        [property: System.Text.Json.Serialization.JsonPropertyName("pageHeight")] decimal PageHeight,
        [property: System.Text.Json.Serialization.JsonPropertyName("margins")]    LayoutMargins Margins,
        [property: System.Text.Json.Serialization.JsonPropertyName("bands")]      IReadOnlyList<LayoutBand> Bands);

    private sealed record LayoutMargins(decimal Top, decimal Bottom, decimal Left, decimal Right);

    private sealed record LayoutBand(
        string Id, string Type, decimal Height,
        bool RepeatOnEveryPage,
        string? DataAlias,
        bool CanGrow,
        IReadOnlyList<LayoutElement> Elements);

    private sealed record LayoutElement(
        string Id, string Kind,
        double X, double Y, double W, double H,
        string? Text,
        ElementStyle? Style,
        BindingDef? Binding,
        string? Format,
        string? Expression,
        string? ShapeKind,
        string? ImageSource);

    private sealed record ElementStyle(
        float FontSize, bool Bold, bool Italic, bool Underline,
        string Align, string? Color, string? BgColor, bool Border);

    private sealed record BindingDef(string Alias, string Col);
}
