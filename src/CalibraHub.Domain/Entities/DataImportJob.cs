using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Veritabanı üzerinden içe aktarım işi: harici bir SQL kaynağındaki tablo/view'ın
/// kolonlarını bir hedef entity'nin alanlarına eşleyen, tekrar çalıştırılabilir tanım.
/// Yazma <c>IImportTargetHandler</c> ailesine devredilir (Excel içe aktarımıyla ortak).
///
/// <see cref="MatchKeyField"/> ZORUNLUDUR — bu iş elle veya cron ile tekrar tekrar
/// çalışır; anahtarsız bir aktarım her turda veriyi çoğaltır.
/// </summary>
[Description("Veritabanı üzerinden içe aktarım işi: harici SQL tablo/view kolonlarının hedef entity alanlarına eşlenmesi. Anahtar alan zorunludur (upsert).")]
public sealed class DataImportJob
{
    public int Id { get; set; }

    /// <summary>İş adı — benzersiz (kullanıcı kod girmez kuralı: ad üzerinden uniqueness).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Kaynak bağlantı — <see cref="ExternalDbConnection"/> FK.</summary>
    public int ConnectionId { get; set; }

    /// <summary>Hedef entity kodu — <c>IImportTargetHandler.Entity</c> ("CONTACT", "ITEM", "MACHINE"...).</summary>
    public string TargetEntity { get; set; } = string.Empty;

    /// <summary>Kaynak nesnenin şeması ("dbo").</summary>
    public string SourceSchema { get; set; } = "dbo";

    /// <summary>Kaynak tablo/view adı.</summary>
    public string SourceObject { get; set; } = string.Empty;

    /// <summary>
    /// Upsert anahtarı — mevcut kaydı bulmak için kullanılan HEDEF alan anahtar(lar)ı.
    /// BİRDEN FAZLA alan verilebilir (bileşik anahtar, örn. Cari Kodu + Ad Soyad);
    /// eşleşme için hepsinin tutması gerekir. En az bir tane ZORUNLUDUR (bkz. sınıf özeti).
    /// DB'de virgülle ayrılmış tek kolonda saklanır.
    /// </summary>
    public List<string> MatchKeyFields { get; set; } = new();

    /// <summary>Hata mesajları/gösterim için ilk anahtar.</summary>
    public string? MatchKeyField => MatchKeyFields.Count > 0 ? MatchKeyFields[0] : null;

    /// <summary>Tek çalıştırmada okunacak azami satır (emniyet tavanı).</summary>
    public int MaxRows { get; set; } = 50_000;

    /// <summary>
    /// Kaynak kısıt kuralları — dışa aktarımdaki "Kısıt Kuralları" ile AYNI JSON şekli:
    /// <c>[{ "field": "Durum", "op": "eq", "value": "A" }, ...]</c> (AND zinciri).
    /// Operatörler: eq, neq, gt, gte, lt, lte, isnull, notnull, contains, startsWith, in, between.
    /// Kolon adları kaynağın <c>sys.columns</c>'ına doğrulanır, değerler SqlParameter ile bağlanır.
    /// Boş/null = kısıt yok (tüm satırlar).
    /// </summary>
    public string? SourceFilterJson { get; set; }

    /// <summary>Prosedür hatalarının aktarımı nasıl etkileyeceği.</summary>
    public DataImportErrorBehavior ErrorBehavior { get; set; } = DataImportErrorBehavior.Lenient;

    // ── Aktarım öncesi prosedür ──────────────────────────────────────────
    public string? PreProcedureName { get; set; }
    public DataImportProcedureTarget PreProcedureTarget { get; set; } = DataImportProcedureTarget.Calibra;
    public string? PreProcedureParamsJson { get; set; }

    // ── Aktarım sonrası prosedür ─────────────────────────────────────────
    public string? PostProcedureName { get; set; }
    public DataImportProcedureTarget PostProcedureTarget { get; set; } = DataImportProcedureTarget.Calibra;
    public string? PostProcedureParamsJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public int? CreatedById { get; set; }
    public int? UpdatedById { get; set; }

    /// <summary>Kolon eşlemeleri — transient; repository ayrı yükler/yazar.</summary>
    public List<DataImportJobColumn> Columns { get; set; } = new();

    /// <summary>"dbo.Cari" biçiminde tam kaynak nesne adı.</summary>
    public string SourceFullName => $"{SourceSchema}.{SourceObject}";

    /// <summary>
    /// Kaydetme/çalıştırma öncesi zorunlu doğrulama. Hata mesajı döner, geçerliyse null.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "İş adı zorunludur.";
        if (ConnectionId <= 0) return "Kaynak bağlantı seçilmelidir.";
        if (string.IsNullOrWhiteSpace(TargetEntity)) return "Hedef entity seçilmelidir.";
        if (string.IsNullOrWhiteSpace(SourceObject)) return "Kaynak tablo/view seçilmelidir.";
        if (MatchKeyFields.Count == 0) return "Anahtar alan zorunludur — anahtarsız aktarım her çalıştırmada kayıtları çoğaltır.";
        if (Columns.Count == 0) return "En az bir kolon eşlemesi tanımlanmalıdır.";

        // Her anahtar alan eşlenmiş olmalı — aksi halde o anahtar satırlarda hiç bulunmaz
        // ve bileşik eşleşme sessizce hep başarısız olur (her satır insert'e döner).
        foreach (var key in MatchKeyFields)
        {
            if (!Columns.Any(c => string.Equals(c.TargetKey, key, StringComparison.OrdinalIgnoreCase)))
                return $"Anahtar alan '{key}' kolon eşlemelerinde yer almalıdır.";
        }

        return null;
    }
}
