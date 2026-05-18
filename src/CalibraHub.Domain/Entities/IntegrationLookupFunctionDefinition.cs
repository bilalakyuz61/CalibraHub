namespace CalibraHub.Domain.Entities;

/// <summary>
/// Entegrasyon Wizard "Fonksiyon" source tipi icin admin tanimli fonksiyon kaydi.
/// Hard-coded yerine DB'den okunur — yeni fonksiyon eklemek admin paneli uzerinden.
/// </summary>
public sealed class IntegrationLookupFunctionDefinition
{
    public int Id { get; set; }

    /// <summary>Mapping kuralinin SourceValue alaninda saklanan stabil ID. Per-row unique (aktif).</summary>
    public required string Code { get; set; }

    /// <summary>UI'da gosterilen baslik.</summary>
    public required string Label { get; set; }

    /// <summary>Kisa aciklama (tooltip).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Veri cekilecek SQL view adi. Identifier dogrulamasi runtime'da yapilir.
    /// View+Key modunda zorunlu, SqlSnippet modunda bos olabilir.
    /// </summary>
    public string? ViewName { get; set; }

    /// <summary>
    /// Anahtar kolonu — `WHERE [KeyColumn] = @Key` ile satir bulunur.
    /// View+Key modunda zorunlu, SqlSnippet modunda bos olabilir.
    /// </summary>
    public string? KeyColumn { get; set; }

    /// <summary>
    /// [LEGACY] Serbest SQL snippet — yeni kayitlarda kullanilmaz, mevcut veri icin
    /// geriye uyum amaciyla okunur. Yeni tasarimda yerini <see cref="SqlFunctionName"/>
    /// (DB'de tanimli SQL function secimi) aldi.
    /// </summary>
    public string? SqlSnippet { get; set; }

    /// <summary>
    /// SQL Fonksiyonu modu — DB'de tanimli bir scalar function (sys.objects type='FN').
    /// Schema-qualified ad (orn. "dbo.fn_GetContactBalance"). Identifier dogrulamasi
    /// runtime'da yapilir (sadece harf/rakam/underscore/nokta).
    ///
    /// Function imzasi STANDART 3 parametre alir, scalar deger doner:
    ///   @P1 NVARCHAR(50)  = form code (orn. "SALES_ORDER_NEW") — mapping engine auto
    ///   @P2 NVARCHAR(100) = anahtar deger — mapping satirinin LookupSourceField'inden
    ///   @P3 NVARCHAR(500) = manuel parametre — mapping satirinin LookupParam alanindan
    ///
    /// Ornek DB function:
    ///   CREATE FUNCTION dbo.fn_GetContactBalance(
    ///       @formCode NVARCHAR(50),
    ///       @contactId NVARCHAR(100),
    ///       @currency NVARCHAR(500))
    ///   RETURNS NVARCHAR(500) AS BEGIN ... END
    ///
    /// Runtime cagri: SELECT [dbo].[fn_GetContactBalance](@P1, @P2, @P3)
    /// SqlFunctionName dolu ise View+Key/SqlSnippet alanlari yoksayilir.
    /// </summary>
    public string? SqlFunctionName { get; set; }

    /// <summary>Liste/dropdown siralamasi.</summary>
    public int SortOrder { get; set; }

    /// <summary>Soft delete — pasif fonksiyonlar mapping editorunde gorunmez.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Donulebilir kolon listesi (UI dropdown'i icin).</summary>
    public List<IntegrationLookupFunctionColumn> Columns { get; set; } = new();
}

public sealed class IntegrationLookupFunctionColumn
{
    public int Id { get; set; }
    public int FunctionId { get; set; }
    public required string Column { get; set; }
    public required string Label { get; set; }
    public int SortOrder { get; set; }
}
