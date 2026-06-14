using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Sistem-wide izin katalog kaydı (Form × Action). Form discovery sırasında startup'ta otomatik
/// seed edilir; admin elle de kayıt ekleyebilir.
///
/// Standart action'lar (her form için):
///   VIEW         — formu açabilme (menü görünürlüğü dahil)
///   CREATE       — yeni kayıt ekleme
///   EDIT_OWN     — kullanıcının kendi (CreatedById == userId) kayıtlarını düzenleme
///   EDIT_ALL     — tüm kayıtları düzenleme (üst kümeler EDIT_OWN'ı kapsar)
///   DELETE_OWN   — kendi kayıtlarını silme
///   DELETE_ALL   — tüm kayıtları silme
///
/// Form-içi özel butonlar:
///   BUTTON:&lt;KEY&gt; — örn. 'BUTTON:APPROVE', 'BUTTON:SEND'. Form designer'da her butona
///   atanan PermissionKey buraya seed edilir.
/// </summary>
[Description("İzin katalog kaydı (FormCode × ActionCode). 2026-06-06 — Yetkilendirme refactor F1.")]
public sealed class PermissionDef
{
    public static class StandardActions
    {
        public const string View       = "VIEW";
        public const string ViewOwn    = "VIEW_OWN";
        public const string Create     = "CREATE";
        public const string EditOwn    = "EDIT_OWN";
        public const string EditAll    = "EDIT_ALL";
        public const string DeleteOwn  = "DELETE_OWN";
        public const string DeleteAll  = "DELETE_ALL";

        public static readonly IReadOnlyList<string> All = new[]
        {
            ViewOwn, View, Create, EditOwn, EditAll, DeleteOwn, DeleteAll,
        };
    }

    public static class Categories
    {
        public const string Crud   = "CRUD";    // VIEW/CREATE/EDIT/DELETE
        public const string Action = "ACTION";  // Form içi özel butonlar (BUTTON:*)
        public const string Report = "REPORT";  // Rapor ekranları
        public const string Admin  = "ADMIN";   // Sistem yönetimi
    }

    public int Id { get; init; }

    /// <summary>Form kodu — Form metadata'sından discover edilir (örn. 'DOCUMENT_NEED', 'PERSONNEL_EDIT').</summary>
    public required string FormCode { get; set; }

    /// <summary>Action kodu — StandardActions sabitleri veya 'BUTTON:&lt;KEY&gt;' formatında özel.</summary>
    public required string ActionCode { get; set; }

    /// <summary>Kullanıcıya görünen ad ('İhtiyaç kaydı: Kendi kayıtlarını düzenle').</summary>
    public required string Label { get; set; }

    /// <summary>Kategori — UI'da grup başlığı için (CRUD/ACTION/REPORT/ADMIN).</summary>
    public string? Category { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime Created { get; init; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public int? CreatedById { get; set; }
    public int? UpdatedById { get; set; }

    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(FormCode),
            "FormCode zorunlu.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(ActionCode),
            "ActionCode zorunlu.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Label),
            "Label zorunlu.");
    }
}
