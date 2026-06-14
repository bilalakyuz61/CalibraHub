using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Xml;
using System.Xml.Linq;

namespace CalibraHub.Web.Helpers;

/// <summary>
/// Evernote .enex dışa aktarma dosyasını ayrıştırır ve CalibraHub Not formatına dönüştürür.
/// </summary>
public sealed record EnexNote(
    string Title,
    string HtmlContent,
    DateTime? Created,
    DateTime? Updated,
    List<string> Tags,
    List<EnexResource> Attachments,
    int SkippedAttachmentCount);   // 20 MB sınırını aşan ve atlanan ek sayısı

public sealed record EnexResource(
    string FileName,
    string Mime,
    byte[] Data);

public static class EnexImporter
{
    private const long MaxAttachmentBytes = 20L * 1024 * 1024; // 20 MB

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver   = null
    };

    public static List<EnexNote> Parse(Stream enexStream)
    {
        XDocument doc;
        using (var reader = XmlReader.Create(enexStream, XmlSettings))
            doc = XDocument.Load(reader);

        var notes = new List<EnexNote>();
        foreach (var noteEl in doc.Root?.Elements("note") ?? [])
        {
            var title   = noteEl.Element("title")?.Value?.Trim() ?? "Adsız Not";
            var created = ParseEnexDate(noteEl.Element("created")?.Value);
            var updated = ParseEnexDate(noteEl.Element("updated")?.Value);

            // Etiketleri oku
            var tags = noteEl.Elements("tag")
                .Select(t => t.Value.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            var contentRaw = noteEl.Element("content")?.Value ?? string.Empty;

            // Kaynakları ayrıştır — hash → kaynak sözlüğü inşa et
            var byHash              = new Dictionary<string, ResourceEntry>(StringComparer.OrdinalIgnoreCase);
            var attachments         = new List<EnexResource>();
            int skippedAttachments  = 0;

            foreach (var resEl in noteEl.Elements("resource"))
            {
                var dataRaw  = resEl.Element("data")?.Value ?? string.Empty;
                var mime     = resEl.Element("mime")?.Value ?? "application/octet-stream";
                var fileName = resEl.Element("resource-attributes")?.Element("file-name")?.Value
                               ?? DefaultFileName(mime);

                byte[] bytes;
                try
                {
                    var clean = dataRaw.Replace("\n", "").Replace("\r", "").Replace(" ", "");
                    bytes = Convert.FromBase64String(clean);
                }
                catch { continue; }

                var hash = ComputeMd5Hex(bytes);
                byHash[hash] = new ResourceEntry(fileName, mime, bytes);

                // Resim olmayanlar dosya eki listesine alınır; resimler HTML içinde inline gösterilir
                if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    if (bytes.Length > MaxAttachmentBytes)
                        skippedAttachments++;
                    else
                        attachments.Add(new EnexResource(fileName, mime, bytes));
                }
            }

            var html = EnmlToHtml(contentRaw, byHash);
            notes.Add(new EnexNote(title, html, created, updated, tags, attachments, skippedAttachments));
        }

        return notes;
    }

    // -------------------------------------------------------------------------

    private static string EnmlToHtml(string enml, Dictionary<string, ResourceEntry> byHash)
    {
        if (string.IsNullOrWhiteSpace(enml)) return string.Empty;

        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(enml), XmlSettings);
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return ExtractEnNoteContent(enml);
        }

        var enNote = doc.Root;
        if (enNote is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var node in enNote.Nodes())
            RenderNode(sb, node, byHash);
        return sb.ToString();
    }

    private static void RenderNode(StringBuilder sb, XNode node, Dictionary<string, ResourceEntry> byHash)
    {
        if (node is XText text)
        {
            sb.Append(HtmlEncoder.Default.Encode(text.Value));
            return;
        }

        if (node is not XElement el) return;

        var name = el.Name.LocalName.ToLowerInvariant();

        switch (name)
        {
            // ── Evernote özel gizli metadata divileri — içerik değil, atla ──────────
            case "div" when IsHiddenEnMetadata(el):
                return;

            // ── Evernote kod bloğu (--en-codeblock:true) → <pre><code> ─────────────
            case "div" when IsCodeBlock(el):
            {
                sb.Append("<pre><code>");
                var codeSb = new StringBuilder();
                ExtractPlainTextLines(el, codeSb);
                // XDocument zaten &lt; → < şeklinde unescaped; HtmlEncoder geri encode eder → <code> için doğru
                sb.Append(HtmlEncoder.Default.Encode(codeSb.ToString().Trim('\n')));
                sb.Append("</code></pre>");
                return;
            }

            // ── ul: task list kontrolü ───────────────────────────────────────────────
            case "ul":
            {
                var isTaskList = el.Descendants().Any(d =>
                    d.Name.LocalName.Equals("en-todo", StringComparison.OrdinalIgnoreCase));
                sb.Append(isTaskList ? "<ul data-type=\"taskList\">" : "<ul>");
                foreach (var child in el.Nodes())
                    RenderNode(sb, child, byHash);
                sb.Append("</ul>");
                return;
            }

            // ── li: task item dönüşümü ───────────────────────────────────────────────
            case "li":
            {
                // en-todo doğrudan veya iç div'lerde olabilir
                var todoEl = el.Descendants().FirstOrDefault(d =>
                    d.Name.LocalName.Equals("en-todo", StringComparison.OrdinalIgnoreCase));
                if (todoEl != null)
                {
                    var isChecked = string.Equals(
                        todoEl.Attribute("checked")?.Value, "true", StringComparison.OrdinalIgnoreCase);
                    sb.Append($"<li data-type=\"taskItem\" data-checked=\"{(isChecked ? "true" : "false")}\">");
                    sb.Append("<p>");
                    foreach (var child in el.Nodes())
                    {
                        // en-todo node'unu atla — data-checked ile temsil edildi
                        if (child is XElement ce &&
                            ce.Name.LocalName.Equals("en-todo", StringComparison.OrdinalIgnoreCase))
                            continue;
                        RenderNode(sb, child, byHash);
                    }
                    sb.Append("</p></li>");
                }
                else
                {
                    sb.Append("<li>");
                    foreach (var child in el.Nodes())
                        RenderNode(sb, child, byHash);
                    sb.Append("</li>");
                }
                return;
            }

            // ── Standalone en-todo (ul/li dışında — örn. <div><en-todo/>metin</div>) ─
            case "en-todo":
            {
                var isChecked = string.Equals(
                    el.Attribute("checked")?.Value, "true", StringComparison.OrdinalIgnoreCase);
                // TipTap, <li> içindeki checkbox'ı task item olarak tanır
                sb.Append(isChecked
                    ? "<input type=\"checkbox\" checked>"
                    : "<input type=\"checkbox\">");
                return;
            }

            // ── Resim / dosya referansı ──────────────────────────────────────────────
            case "en-media":
            {
                var hash = el.Attribute("hash")?.Value ?? string.Empty;
                if (byHash.TryGetValue(hash, out var res))
                {
                    if (res.Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        var b64 = Convert.ToBase64String(res.Data);
                        // MIME değeri ENEX'den geliyor — ASCII, güvenli
                        sb.Append($"<img src=\"data:{res.Mime};base64,{b64}\" style=\"max-width:100%\" alt=\"{HtmlEncoder.Default.Encode(res.FileName)}\">");
                    }
                    else
                    {
                        sb.Append($"<span>[Ek: {HtmlEncoder.Default.Encode(res.FileName)}]</span>");
                    }
                }
                else
                {
                    sb.Append("<span>[Ek]</span>");
                }
                return;
            }

            case "en-crypt":
                sb.Append("<span><em>[Şifreli içerik — Evernote'da açınız]</em></span>");
                return;

            default:
                sb.Append($"<{name}");
                foreach (var attr in el.Attributes())
                    sb.Append($" {attr.Name.LocalName}=\"{HtmlEncoder.Default.Encode(attr.Value)}\"");

                sb.Append('>');
                foreach (var child in el.Nodes())
                    RenderNode(sb, child, byHash);
                if (!el.IsEmpty)
                    sb.Append($"</{name}>");
                return;
        }
    }

    // ── Yardımcı metodlar ───────────────────────────────────────────────────────

    /// <summary>Evernote'un gizli stil metadata div'i mi? (display:none + --en-chs içerir)</summary>
    private static bool IsHiddenEnMetadata(XElement el)
    {
        var style = el.Attribute("style")?.Value ?? string.Empty;
        return style.Contains("display:none", StringComparison.OrdinalIgnoreCase)
            && style.Contains("--en-chs:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Evernote kod bloğu mu? (--en-codeblock:true CSS değişkeni içerir)</summary>
    private static bool IsCodeBlock(XElement el)
    {
        var style = el.Attribute("style")?.Value ?? string.Empty;
        return style.Contains("--en-codeblock:true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bir elementin düz metin içeriğini satır bazında çıkartır.
    /// Her iç &lt;div&gt; bir satır, &lt;br&gt; satır sonu olarak işlenir.
    /// HTML entity'leri XDocument tarafından zaten çözümlenmiş olduğundan
    /// ham (unescaped) metin döner — çağıran HtmlEncoder uygular.
    /// </summary>
    private static void ExtractPlainTextLines(XElement el, StringBuilder sb)
    {
        bool firstLine = true;
        foreach (var node in el.Nodes())
        {
            if (node is XText t)
            {
                sb.Append(t.Value);
            }
            else if (node is XElement child)
            {
                var cName = child.Name.LocalName.ToLowerInvariant();
                if (cName == "br")
                {
                    sb.Append('\n');
                }
                else if (cName == "div")
                {
                    // Her iç div yeni bir satır
                    if (!firstLine) sb.Append('\n');
                    firstLine = false;
                    ExtractPlainTextLines(child, sb);
                }
                else
                {
                    // Span, b, i gibi inline elemanlar — metin içeriğini al
                    ExtractPlainTextLines(child, sb);
                }
            }
        }
    }

    // -------------------------------------------------------------------------

    private static string ComputeMd5Hex(byte[] data)
    {
        var hash = MD5.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DateTime? ParseEnexDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        // Evernote format: "20230101T120000Z"
        return DateTime.TryParseExact(
            value, "yyyyMMdd'T'HHmmss'Z'",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt)
            ? dt.ToLocalTime()
            : null;
    }

    private static string DefaultFileName(string mime)
    {
        if (!mime.Contains('/')) return "attachment";
        var ext = mime.Split('/')[1].Split('+')[0];   // "image/svg+xml" → "svg"
        return $"attachment.{ext}";
    }

    /// <summary>XML parse edilemezse &lt;en-note&gt; içeriğini string olarak çıkartır.</summary>
    private static string ExtractEnNoteContent(string enml)
    {
        var openStart = enml.IndexOf("<en-note", StringComparison.OrdinalIgnoreCase);
        if (openStart < 0) return enml;
        var openEnd = enml.IndexOf('>', openStart);
        if (openEnd < 0) return enml;
        var closeTag = enml.LastIndexOf("</en-note>", StringComparison.OrdinalIgnoreCase);
        return closeTag > openEnd ? enml[(openEnd + 1)..closeTag] : enml[(openEnd + 1)..];
    }

    private sealed record ResourceEntry(string FileName, string Mime, byte[] Data);
}
