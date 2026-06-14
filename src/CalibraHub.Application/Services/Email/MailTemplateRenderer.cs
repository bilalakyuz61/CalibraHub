using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Email;

/// <summary>
/// Mail sablonu HTML render — tablo-tabanli, inline CSS.
/// Mail istemcileri (Outlook, Gmail, vb.) <head><style> blogunu cogunlukla atar;
/// guvenli sablon icin tum stil ozellikleri inline yazilir.
/// </summary>
public sealed class MailTemplateRenderer : IMailTemplateRenderer
{
    // {token} veya {{token}} pattern — Compose ekrani Mustache stili ({{name}}) sunuyor
    // ama eski layout'lar tek-brace ({name}) kullanmis olabilir; ikisini birden destekle.
    // Optional ?'ler ile {{name}}, {name}, {{name} ve {name}} kombinasyonlari yakalanir.
    private static readonly Regex TokenRegex = new(@"\{\{?([A-Za-z][A-Za-z0-9_]*)\}\}?", RegexOptions.Compiled);

    public string RenderHtml(DocLayoutDetailDto layout, IDictionary<string, string> tokenValues, string? mailBodyContent)
    {
        var bands = ParseBands(layout.LayoutJson);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"UTF-8\"></head>")
          .Append("<body style=\"margin:0;padding:0;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#222;background:#f5f5f5;\">")
          // Outer wrapper — 640px center align (yaygin standart)
          .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"background:#f5f5f5;padding:16px 0;\">")
          .Append("<tr><td align=\"center\">")
          .Append("<table role=\"presentation\" width=\"640\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"background:#ffffff;border:1px solid #e0e0e0;border-collapse:collapse;\">");

        foreach (var band in bands)
            sb.Append(RenderBand(band, tokenValues, mailBodyContent));

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    // ── Band rendering ─────────────────────────────────────────────────────────
    private static string RenderBand(BandDto band, IDictionary<string, string> tokens, string? mailBody)
    {
        var cell = new StringBuilder();
        cell.Append("<tr><td style=\"padding:12px 20px;\">");

        if (string.Equals(band.Type, "mail_body", StringComparison.OrdinalIgnoreCase))
        {
            // Placeholder band — kullanici gondrim aninda doldurur.
            // Plain text → token substitute + HTML escape + satir sonu <br/>.
            cell.Append(RenderMailBody(mailBody, tokens));
        }
        else
        {
            // Standart bandlar — element listesi inline akar (PDF mutlak konum yerine
            // mail sablonu icin akis dizilim mantikli; mail istemcileri absolute position
            // kullanmaz). Elementleri Y sirasiyla (sonra X) dizip <div> ile renderler.
            var sorted = band.Elements
                .OrderBy(e => e.Y)
                .ThenBy(e => e.X)
                .ToList();

            foreach (var el in sorted)
                cell.Append(RenderElement(el, tokens));
        }

        cell.Append("</td></tr>");
        return cell.ToString();
    }

    private static string RenderMailBody(string? text, IDictionary<string, string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "<div style=\"color:#999;font-style:italic;\">(Mail govdesi bos)</div>";
        // Sira: 1) Token substitute (raw degerler) → 2) HTML escape → 3) newline → <br/>.
        // Substitute once yapilir ki kullanicinin {{personName}} placeholder'i gercek isimle dolsun.
        // Escape sonradan yapilir ki kullanici body'sindeki "<" gibi karakterler guvenli kalsin.
        var substituted = SubstituteTokens(text, tokens);
        var escaped = WebUtility.HtmlEncode(substituted).Replace("\r\n", "\n").Replace("\n", "<br/>");
        return $"<div style=\"font-size:13px;line-height:1.5;color:#222;white-space:normal;\">{escaped}</div>";
    }

    private static string RenderElement(ElementDto el, IDictionary<string, string> tokens)
    {
        var style = BuildInlineStyle(el);

        return el.Kind switch
        {
            "Label" or "BoundField" or "DateTimeNow" or "PageNumber" or "AmountInWords"
                => $"<div style=\"{style}\">{WebUtility.HtmlEncode(SubstituteTokens(el.Text ?? string.Empty, tokens))}</div>",

            "Image" when !string.IsNullOrWhiteSpace(el.ImageSrc)
                // Tasarimcidaki genislik/yukseklik (mm) korunur — eskisi sabit max-width:100% idi,
                // resmi tum kart genisligine sisirip dizilim'i bozuyordu. Mail client kucuk
                // ekran limitleri icin max-width:100% guvence olarak kalir.
                => $"<div style=\"{style}\"><img src=\"{WebUtility.HtmlEncode(el.ImageSrc!)}\" alt=\"\" "
                   + $"style=\"width:{el.W.ToString(CultureInfo.InvariantCulture)}mm;"
                   + (el.H > 0 ? $"height:{el.H.ToString(CultureInfo.InvariantCulture)}mm;" : "height:auto;")
                   + "max-width:100%;display:block;\"/></div>",

            "Shape"
                => $"<div style=\"{style}border-top:1px solid #cccccc;height:1px;\"></div>",

            _ => string.Empty,
        };
    }

    private static string BuildInlineStyle(ElementDto el)
    {
        var s = new StringBuilder();
        var st = el.Style;
        if (st != null)
        {
            if (st.FontSize.HasValue) s.Append($"font-size:{st.FontSize.Value.ToString(CultureInfo.InvariantCulture)}px;");
            if (st.Bold == true)      s.Append("font-weight:bold;");
            if (st.Italic == true)    s.Append("font-style:italic;");
            if (st.Underline == true) s.Append("text-decoration:underline;");
            if (!string.IsNullOrWhiteSpace(st.Align))   s.Append($"text-align:{st.Align};");
            if (!string.IsNullOrWhiteSpace(st.Color))   s.Append($"color:{st.Color};");
            if (!string.IsNullOrWhiteSpace(st.BgColor) && st.BgColor != "transparent")
                s.Append($"background:{st.BgColor};");
        }
        s.Append("padding:2px 0;");
        return s.ToString();
    }

    private static string SubstituteTokens(string text, IDictionary<string, string> tokens)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return TokenRegex.Replace(text, m =>
        {
            var key = m.Groups[1].Value;
            return tokens.TryGetValue(key, out var v) ? v : m.Value;
        });
    }

    // ── LayoutJson → DTO ───────────────────────────────────────────────────────
    private static List<BandDto> ParseBands(string layoutJson)
    {
        if (string.IsNullOrWhiteSpace(layoutJson)) return new();
        try
        {
            using var doc = JsonDocument.Parse(layoutJson);
            if (!doc.RootElement.TryGetProperty("bands", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return new();

            var result = new List<BandDto>();
            foreach (var b in arr.EnumerateArray())
            {
                var band = new BandDto
                {
                    Type     = b.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : string.Empty,
                    Height   = b.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetDouble() : 20,
                    Elements = new(),
                };
                if (b.TryGetProperty("elements", out var els) && els.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in els.EnumerateArray())
                        band.Elements.Add(ParseElement(e));
                }
                result.Add(band);
            }
            return result;
        }
        catch
        {
            return new();
        }
    }

    private static ElementDto ParseElement(JsonElement e)
    {
        var el = new ElementDto
        {
            Kind     = e.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : string.Empty,
            Text     = e.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String ? tx.GetString() : null,
            X        = e.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 0,
            Y        = e.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetDouble() : 0,
            W        = e.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : 0,
            H        = e.TryGetProperty("h", out var hh) && hh.ValueKind == JsonValueKind.Number ? hh.GetDouble() : 0,
            ImageSrc = e.TryGetProperty("imageSrc", out var im) && im.ValueKind == JsonValueKind.String ? im.GetString() : null,
        };
        if (e.TryGetProperty("style", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            el.Style = new StyleDto
            {
                FontSize  = st.TryGetProperty("fontSize", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetDouble() : null,
                Bold      = st.TryGetProperty("bold", out var b) && b.ValueKind == JsonValueKind.True,
                Italic    = st.TryGetProperty("italic", out var i) && i.ValueKind == JsonValueKind.True,
                Underline = st.TryGetProperty("underline", out var u) && u.ValueKind == JsonValueKind.True,
                Align     = st.TryGetProperty("align", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null,
                Color     = st.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
                BgColor   = st.TryGetProperty("bgColor", out var bg) && bg.ValueKind == JsonValueKind.String ? bg.GetString() : null,
            };
        }
        return el;
    }

    private sealed class BandDto
    {
        public string Type { get; set; } = string.Empty;
        public double Height { get; set; }
        public List<ElementDto> Elements { get; set; } = new();
    }

    private sealed class ElementDto
    {
        public string Kind { get; set; } = string.Empty;
        public string? Text { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public string? ImageSrc { get; set; }
        public StyleDto? Style { get; set; }
    }

    private sealed class StyleDto
    {
        public double? FontSize { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public bool? Underline { get; set; }
        public string? Align { get; set; }
        public string? Color { get; set; }
        public string? BgColor { get; set; }
    }
}
