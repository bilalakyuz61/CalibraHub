using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Bir REST endpoint icin "Body Schema'yi nereden ogrenirim?" sorusunun otomatik cevabi.
///
/// 2-katmanli fallback (V1):
///   1. Describe — POST {baseUrl}/api/v2/{Resource}/Describe (Netsis NetOpenX standardi)
///   2. POST-probe — bos body gonder, 400 hata cevabini parse et (zorunlu alanlari topla)
///
/// V2: Swagger/OpenAPI parser + GET sample sterilizer + AI-powered.
/// </summary>
public interface IBodySchemaResolver
{
    Task<BodySchemaResolveResult> ResolveAsync(
        IntegrationEndpoint endpoint,
        IntegrationApiProfile profile,
        CancellationToken ct);
}

/// <summary>
/// Body schema resolution sonucu.
/// Source: hangi katmandan geldi (Services / Definitions / Describe / SampleGet / Probe / Failed).
/// Record cunku resolver akisinda `with` expression ile DurationMs ekleniyor.
/// </summary>
public sealed record BodySchemaResolveResult
{
    public bool Success { get; init; }
    public string Source { get; init; } = "Failed";
    public string? BodyJson { get; init; }          // Eger Success ise sterilize edilmis JSON
    public string? ErrorMessage { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? RawResponse { get; init; }       // Debug icin
    public int DurationMs { get; init; }

    /// <summary>
    /// Swagger schema'sindan parse edilmis alan metadatasi:
    ///   • Path: "FatUst.CariKod" / "Kalems[].StokKodu"
    ///   • Type: "string" | "integer" | "boolean" | "array" | "object"
    ///   • Required: zorunlu mu (parent'in required[] dizisinde mi)
    ///   • MaxLength: string max length (varsa)
    ///   • Enum: enum degerleri JSON-encoded (varsa)
    /// Sadece Services / Definitions katmaninda doldurulur.
    /// UI'da Body Schema editor altinda "Zorunlu alanlar" panelinde gosterilir +
    /// Step 3 mapping ekraninda red asterisk icin kullanilir.
    /// </summary>
    public IReadOnlyList<BodySchemaFieldMeta>? Fields { get; init; }
}

/// <summary>Tek bir field'in metadata'si — UI'da ipucu, mapping ekraninda zorunluluk.</summary>
public sealed record BodySchemaFieldMeta(
    string Path,
    string Type,
    bool Required,
    int? MaxLength,
    string? Enum);
