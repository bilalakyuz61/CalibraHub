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
    List<EnexResource> Attachments);   // Resim olmayan kaynaklar dosya eki olarak kaydedilir

public sealed record EnexResource(
    string FileName,
    string Mime,
    byte[] Data);

public static class EnexImporter
{
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
            var title      = noteEl.Element("title")?.Value?.Trim() ?? "Adsız Not";
            var created    = ParseEnexDate(noteEl.Element("created")?.Value);
            var contentRaw = noteEl.Element("content")?.Value ?? string.Empty;

            // Kaynakları ayrıştır — hash → kaynak sözlüğü inşa et
            var byHash      = new Dictionary<string, ResourceEntry>(StringComparer.OrdinalIgnoreCase);
            var attachments = new List<EnexResource>();

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
                    attachments.Add(new EnexResource(fileName, mime, bytes));
            }

            var html = EnmlToHtml(contentRaw, byHash);
            notes.Add(new EnexNote(title, html, created, attachments));
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
            case "en-todo":
                var isChecked = string.Equals(
                    el.Attribute("checked")?.Value, "true", StringComparison.OrdinalIgnoreCase);
                sb.Append(isChecked
                    ? "<input type=\"checkbox\" checked disabled>"
                    : "<input type=\"checkbox\" disabled>");
                return;

            case "en-media":
                var hash = el.Attribute("hash")?.Value ?? string.Empty;
                if (byHash.TryGetValue(hash, out var res))
                {
                    if (res.Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        var b64 = Convert.ToBase64String(res.Data);
                        // MIME type used as-is in data URI — HtmlEncoder would encode '/' → '&#x2F;'
                        // which breaks the data URI. MIME values from ENEX are well-formed ASCII.
                        sb.Append($"<img src=\"data:{res.Mime};base64,{b64}\" style=\"max-width:100%\">");
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

    /// <summary>XML parse edilemezse <en-note> içeriğini string olarak çıkartır.</summary>
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
