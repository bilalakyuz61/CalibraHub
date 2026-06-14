namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-05-24 — Calibo'nun dokuman okuma yetenegi.
/// Excel/PDF/DOCX gibi ikili dosyalardan metin cikartir, model'e text olarak iletir.
///
/// Implementasyon Infrastructure katmaninda (ClosedXML, PdfPig, OpenXml).
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>
    /// Verilen binary icerikten metin cikarir. Format MIME tipinden anlasilir.
    /// Desteklenmiyorsa null doner.
    /// </summary>
    /// <param name="data">Dosya binary icerigi</param>
    /// <param name="mimeType">application/pdf, application/vnd.openxmlformats-officedocument.* vb.</param>
    /// <param name="fileName">Display amaciyla — ornek log'a yazilabilir</param>
    /// <returns>Metin (NULL desteklenmeyen format)</returns>
    string? ExtractText(byte[] data, string mimeType, string fileName);

    /// <summary>Bu MIME tipini bu extractor destekliyor mu?</summary>
    bool Supports(string mimeType, string fileName);
}
