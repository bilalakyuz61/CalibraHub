namespace CalibraHub.Domain.Entities;

public sealed class ReportTemplate
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int DocumentTypeId { get; init; }

    /// <summary>
    /// Eski veri uyumu icin tutuluyor (legacy: wwwroot/Document/Templates/...).
    /// Yeni sablonlar FrxContent ile DB'ye kaydedilir.
    /// </summary>
    public string? FrxFilePath { get; init; }

    /// <summary>
    /// .frx icerigi binary olarak DB'de tutulur. Yeni akisin ana depolama alani.
    /// </summary>
    public byte[]? FrxContent { get; set; }

    public string? Description { get; init; }

    /// <summary>
    /// Opsiyonel per-template SQL view adi override'i.
    /// Bos ise DocumentType.SqlViewName kullanilir; dolu ise bu deger
    /// ReportDataRepository'nin veri cekeceği view adidir.
    /// Kullanicinin "Yeni Sablon" dialog'unda doldurdugu alan buraya yazilir.
    /// </summary>
    public string? SqlViewName { get; set; }

    /// <summary>
    /// Opsiyonel key kolon override'i. recordId ile eslesecek view kolonu.
    /// Bos ise DetectFilterColumnAsync heuristigi kullanilir (BelgeId > id > Id > ID).
    /// Kullanici "Yeni Sablon" dialog'unda view sectikten sonra bu alani secer.
    /// Ornek: vw_ReportDocument -> BelgeId;  vw_ProductBarcode -> id;  vw_CustomerBalance -> CariId
    /// </summary>
    public string? KeyColumn { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Per-template cikti secenekleri (JSON). Kullanici "Cikti Ayarlari" modalinda
    /// belirler; "Yazdir" akisi bu ayarlari okuyup direkt uygular (modalsiz).
    /// Format: { "preview": bool, "pdf": bool, "mail": bool,
    ///           "mailTo": string?, "mailSubject": string?, "mailBody": string? }
    /// NULL olabilir → kayitli ayar yoksa eski modal akisi calisir.
    /// </summary>
    public string? OutputOptionsJson { get; set; }

    /// <summary>
    /// Generation sirasinda view sorgusuna eklenecek default siralama kolonu.
    /// View'in INFORMATION_SCHEMA'sindan kullanici secer.
    /// NULL ise siralama uygulanmaz (view'in dogal sirasi).
    /// </summary>
    public string? OrderColumn { get; set; }

    /// <summary>"ASC" veya "DESC". OrderColumn dolu degilse anlamsiz.</summary>
    public string? OrderDirection { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
