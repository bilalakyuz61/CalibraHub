using System.ComponentModel;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir entegrasyon kurgusu — wizard 5 step sonucu uretilir. Hangi formdan (SourceFormCode)
/// hangi endpoint'e (TargetEndpointId) hangi mapping kurallariyla (Mappings) ve hangi
/// trigger'larla (Triggers) calistirilacagini tanimlar.
///
/// Tasarim dokumani: docs/integration-wizard-design.md
/// </summary>
[Description("Entegrasyon konfigurasyonu. Form -> Endpoint mapping kurallari + tetikleyicilerin agregatoru.")]
public sealed class Integration
{
    public int Id { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Kaynak form (dbo.Forms.FormCode ile eslesir, orn. "SALES_ORDERS").</summary>
    public required string SourceFormCode { get; set; }

    /// <summary>
    /// Hedef REST endpoint (opsiyonel). FK -> IntegrationEndpoint.Id.
    /// NULL ise "Sadece Prosedür" modu — HTTP cagrisi yapilmaz, mapping engine atlanir,
    /// yalnizca PreProcedureName / PostProcedureName calistirilir. Bu modda en az birinin
    /// dolu olmasi sart (validation servis katmaninda).
    /// </summary>
    public int? TargetEndpointId { get; set; }

    /// <summary>Hata aldiginda davranis: Skip / Retry / Manuel.</summary>
    public IntegrationErrorBehavior ErrorBehavior { get; set; } = IntegrationErrorBehavior.Skip;

    /// <summary>Retry davranisi secildiyse maks deneme sayisi.</summary>
    public int RetryCount { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Versiyon numarasi — IntegrationHistory ile birlikte rollback icin (V1.1).</summary>
    public int VersionNo { get; set; } = 1;

    /// <summary>
    /// Entegrasyon HTTP cagrisindan ONCE calistirilacak opsiyonel SQL stored procedure
    /// adi. NULL ise pre-action yok. Format: "dbo.LockDocument" veya "LockDocument".
    /// Engine: EXEC PreProc → (basarili ise) HTTP cagrisi → EXEC PostProc.
    /// PreProc HATA verirse entegrasyon IPTAL edilir; HTTP hic cagirilmaz, run log'a Failed yazilir.
    /// Kullanim ornekleri:
    ///   - Belgeyi 'Pending' status'a cek (concurrent lock)
    ///   - Pre-validation (yeterli stok? cari risk limit?)
    ///   - Audit kayit at (kim baslatti, ne zaman)
    ///   - Staging tablosuna snapshot al (rollback icin)
    /// </summary>
    public string? PreProcedureName { get; set; }

    /// <summary>
    /// Pre-procedure parametreleri JSON array — Post ile ayni format.
    /// SourceType: FormField | Constant | RunMeta. (Response/HttpStatus YOK — HTTP henuz yapilmadi)
    /// </summary>
    public string? PreProcedureParamsJson { get; set; }

    /// <summary>
    /// Entegrasyon basariyla calistiktan sonra calistirilacak SQL stored procedure adi
    /// (opsiyonel). NULL ise post-action yok. Format: "dbo.MarkAsExported" veya "MarkAsExported".
    /// Engine: HTTP basarili ise EXEC PostProcedureName @p1=v1, @p2=v2, ...
    /// </summary>
    public string? PostProcedureName { get; set; }

    /// <summary>
    /// Post-procedure parametreleri JSON array. Her parametre:
    ///   { "name":"@DocumentId", "sourceType":"FormField", "sourceValue":"Id" }
    ///   { "name":"@Status",     "sourceType":"Constant",  "sourceValue":"Exported" }
    ///   { "name":"@RunId",      "sourceType":"RunMeta",   "sourceValue":"RunId" }
    ///   { "name":"@HttpCode",   "sourceType":"Response",  "sourceValue":"StatusCode" }
    /// SourceType: FormField | Constant | RunMeta | Response
    /// </summary>
    public string? PostProcedureParamsJson { get; set; }

    /// <summary>
    /// 2026-05-22 Pre-flight Filter: kaynak kayıt entegrasyona uygun mu kuralları.
    /// JSON array: [{ field, op, value, logic }].
    /// Özel prefix: 'widget:fieldKey' = WidgetTra'dan değer çek.
    /// Operators: eq/neq/gt/gte/lt/lte/isnull/notnull/contains/startsWith/in/between.
    /// NULL veya boş array = filtre yok (tüm kayıtlar uygun).
    /// Tüm tetikleyici yollarda (Manual/Cron/OnSave/Queue/Sayaç) tek noktadan uygulanır.
    /// </summary>
    public string? SourceFilterJson { get; set; }

    /// <summary>
    /// 2026-05-22 Cascade: Bu entegrasyon başka entegrasyonlar tarafından cascade hedefi
    /// olarak seçilebilir mi? Default TRUE — her integration cascade'lenebilir.
    /// FALSE'a çevirilirse Wizard Step 2 "Bağımlılık" dropdown'ında gözükmez.
    /// Manuel/Cron/OnSave tetikleyiciler bu flag'ten ETKİLENMEZ — sadece cascade için.
    ///
    /// Kullanım: Aynı "Netsis Stok Ekle" integration'ı hem manuel butondan hem cron'dan
    /// hem de Sipariş cascade'inden çağrılabilir; tek tanım, çok yol. Kapatmak istersen
    /// (özel cascade-only versiyon yarattıysan vs.) bu flag'i FALSE yap.
    /// </summary>
    public bool AllowAsCascadeTarget { get; set; } = true;

    /// <summary>
    /// Kod bazlı cascade: bu integration cascade hedefi olarak bir KOD değeriyle çağrıldığında
    /// (SourceCodeColumn dolu + mapping'de CascadeByValue=true), bu kolon üzerinden entity bulunur.
    /// Örnek: "CariKod" → v_Flat_CONTACTS WHERE CariKod = @code → Id alınır.
    /// NULL = ID bazlı cascade (default davranış, geriye uyumlu).
    /// </summary>
    public string? SourceCodeColumn { get; set; }

    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }

    // ── Aggregate children (transient — repository tarafindan join ile doldurulur) ──

    public IReadOnlyList<IntegrationMapping> Mappings { get; set; } = Array.Empty<IntegrationMapping>();
    public IReadOnlyList<IntegrationTrigger> Triggers { get; set; } = Array.Empty<IntegrationTrigger>();

    /// <summary>Endpoint ve auth profile bilgileri (display amacli, ayri sorgu).</summary>
    public IntegrationEndpoint? Endpoint { get; set; }

    // ── Davranis (rapor §2.4 — Aktif/Pasif transition + validation) ──────────

    /// <summary>
    /// Konfigurasyonun tutarliligini kontrol eder. Save oncesi cagrilir.
    /// Hata: DomainException.
    /// </summary>
    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Name),
            "Entegrasyon adi zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(SourceFormCode),
            "Kaynak form (SourceFormCode) zorunludur.");

        // TargetEndpointId NULL ise "Sadece Procedure" modu — en az birinin dolu olmasi sart
        var hasEndpoint  = TargetEndpointId.HasValue && TargetEndpointId.Value > 0;
        var hasPreProc   = !string.IsNullOrWhiteSpace(PreProcedureName);
        var hasPostProc  = !string.IsNullOrWhiteSpace(PostProcedureName);
        DomainException.ThrowIf(!hasEndpoint && !hasPreProc && !hasPostProc,
            "Hedef endpoint veya en az bir SQL Procedure (Pre/Post) tanimlanmalidir.");

        // Retry davranisi secildiyse RetryCount pozitif olmali
        DomainException.ThrowIf(ErrorBehavior == IntegrationErrorBehavior.Retry && RetryCount <= 0,
            "Retry davranisi seciliyse RetryCount > 0 olmalidir.");
        DomainException.ThrowIf(RetryCount < 0,
            "RetryCount negatif olamaz.");
    }

    /// <summary>Entegrasyonu aktif et — yalniz IsValid ise.</summary>
    public void Activate()
    {
        EnsureValid();
        if (IsActive) return;
        IsActive = true;
        Updated = DateTime.UtcNow;
    }

    /// <summary>Entegrasyonu pasif et — yeni trigger calismaz, mevcut run'lar etkilenmez.</summary>
    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        Updated = DateTime.UtcNow;
    }

    /// <summary>Yeni versiyona gec — runtime'da degisiklik geldikten sonra cagrilir (IntegrationHistory ile birlikte).</summary>
    public void BumpVersion()
    {
        VersionNo += 1;
        Updated = DateTime.UtcNow;
    }

    /// <summary>"Sadece Prosedur" modu mu? (HTTP cagrisi yok, sadece Pre/Post SQL).</summary>
    public bool IsProcedureOnlyMode() => !TargetEndpointId.HasValue || TargetEndpointId.Value <= 0;
}
