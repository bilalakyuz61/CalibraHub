using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace CalibraHub.Application.Mapping;

/// <summary>
/// Mapster konfigurasyonu (rapor §2.4 cozumu).
///
/// Su an Web/Application'da 987 manuel anonim type projection (entity → DTO/JSON)
/// var. Bu konfig her entity icin TEK noktada mapping kuralini tanimlar. Yeni kod
/// `entity.Adapt&lt;DocumentDto&gt;()` cagirip otomatik DTO uretir.
///
/// Mevcut DTO'lar (DocumentDto, MachineDto, vb. Application.Contracts altinda) zaten
/// var; bu config sadece entity → DTO yonunde otomatik mapping kurar.
///
/// Bootstrap (Web/Program.cs ve Worker/Program.cs):
///   builder.Services.AddSingleton(MapsterConfig.BuildTypeAdapterConfig());
///   builder.Services.AddScoped&lt;IMapper, ServiceMapper&gt;();
///
/// Kullanim (controller / service):
///   var dto = document.Adapt&lt;DocumentDto&gt;();          // tek
///   var dtos = documents.Adapt&lt;List&lt;DocumentDto&gt;&gt;();    // koleksiyon
///   var dto = _mapper.Map&lt;DocumentDto&gt;(document);     // IMapper injection
///
/// Yeni entity eklenince: bu dosyada Configure metodu icine bir satir mapping eklenir.
/// </summary>
public static class MapsterConfig
{
    /// <summary>Mapster global config — singleton olarak DI'ya kaydedilir.</summary>
    public static TypeAdapterConfig BuildTypeAdapterConfig()
    {
        var config = new TypeAdapterConfig();
        Configure(config);
        return config;
    }

    /// <summary>
    /// Tum entity → DTO mapping'lerini buraya kaydet. Mapster convention tabanli
    /// (same-name property auto-map), sadece ozel donusumler manuel tanimlanir.
    /// </summary>
    public static void Configure(TypeAdapterConfig config)
    {
        // ── Document ─────────────────────────────────────────────────────
        // Convention: Document.Id, DocumentNumber, ContactId, ... aynı isimle DocumentDto'ya gider.
        // Transient/computed alanlar (ContactCode) entity tarafinda dolar, DTO'da ayni isim.
        // Domain'de DateTime, DTO'da DateTime — kultur neutral.
        config.NewConfig<Document, DocumentDto>();

        // ── DocumentLine ─────────────────────────────────────────────────
        // Convention: LineNo, ItemId, Quantity, UnitPrice, ... → ayni isim.
        // MaterialCode/Name vb. transient — entity tarafinda JOIN ile dolar.
        config.NewConfig<DocumentLine, DocumentLineDto>();

        // ── Contact ──────────────────────────────────────────────────────
        // Convention: Id, AccountCode, AccountTitle, TaxNumber, ... auto-map.
        // AccountType: ContactType enum (byte-backed) → byte DTO. Cast otomatik.
        // CreatedAt eslesir; transient olmayan diger alanlar (Mobile, Website, WaPhone vb.) DTO'da nullable optional.
        config.NewConfig<Contact, ContactDto>()
              .Map(dest => dest.AccountType, src => (byte)src.AccountType);

        // ── Item ─────────────────────────────────────────────────────────
        // Convention: Id, Code, Name, TypeId, UnitId, Combinations, TaxRate auto-map.
        config.NewConfig<Item, ItemDto>();

        // ── Company ──────────────────────────────────────────────────────
        // Auto-map: tum alanlar ayni isim, ayni tip.
        config.NewConfig<Company, CompanyDto>();

        // ── Department ───────────────────────────────────────────────────
        // CompanyName transient (DTO'da var, entity'de yok). Auto-map default deger birakir,
        // servis tarafinda doldurulur (snapshot JOIN ile).
        config.NewConfig<Department, DepartmentDto>()
              .Map(dest => dest.CompanyName, src => string.Empty)
              .IgnoreNullValues(true);

        // İleride yeni entity eklerken: tek satir NewConfig + ozel donusumler buraya gelir.
    }
}
