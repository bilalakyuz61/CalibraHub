namespace CalibraHub.Web.Models.Diagnostics;

/// <summary>
/// Bir tablo için INSERT testinin tanımı.
/// SqlValue: SQL literal'i (örn. <c>N'HCTEST'</c>, <c>1</c>, <c>SYSUTCDATETIME()</c>).
/// Sadece NOT NULL kolonları ve gerekli FK kolonlarını içerir; diğerleri DEFAULT veya NULL alır.
/// ScreenPaths: bu probe'un hangi menü URL'lerine bağlı olduğu (string eşleşme).
/// </summary>
public sealed record SchemaProbeDefinition(
    string Table,
    IReadOnlyList<(string Column, string SqlValue)> Columns,
    string[] ScreenPaths);

/// <summary>
/// 2026-05-26 — Schema probe registry (Faz 1).
///
/// Eklenecek entity için:
///   1) CREATE TABLE bloğunu CalibraDatabaseInitializer'da bul, NOT NULL kolonları topla
///   2) Default'u olmayan NOT NULL kolonlar için SqlValue gir (Code, Name, FK ID, ...)
///   3) ScreenPaths array'ine ekranın liste/edit URL'lerini koy
///
/// FK'li kolonlarda SABİT ID YAZMA — <see cref="FirstCompanyId"/> / <see cref="FirstUserId"/>
/// gibi "var olan ilk kaydı seçen" alt sorguları kullan. Sabit 0 yazılmıştı ve Company/Users
/// FK'leri eklenince probe'ların kendisi FK ihlali vermeye başladı (2026-08-28).
/// </summary>
public static class SchemaProbeRegistry
{
    /// <summary>
    /// FK'li kolonlar icin VAR OLAN bir kaydi secen alt sorgular (2026-08-28).
    /// Sabit <c>0</c> yaziliyordu; Company/Users tablolarina yabanci anahtar eklendiginde
    /// probe'un KENDISI FK ihlali verdi ve saglik kontrolu ekrani saglikli oldugu halde
    /// "sema hatasi" gosterdi. <c>{schema}</c> yer tutucusunu SchemaProbeService cozer.
    /// </summary>
    private const string FirstCompanyId = "(SELECT TOP 1 [Id] FROM [{schema}].[Company] ORDER BY [Id])";
    private const string FirstUserId    = "(SELECT TOP 1 [Id] FROM [{schema}].[Users] ORDER BY [Id])";

    public static readonly IReadOnlyList<SchemaProbeDefinition> Definitions = new[]
    {
        // ── Items (Malzeme Kartı) ────────────────────────────────────────────
        new SchemaProbeDefinition(
            Table: "Items",
            Columns: new[]
            {
                ("Code",      "N'HCTEST'"),
                ("Name",      "N'HCTEST'"),
                ("CompanyId", FirstCompanyId),
                // TypeId/UnitId NULL kabul, TaxRate/Combinations/IsActive/Created/Updated DEFAULT'lu
            },
            ScreenPaths: new[]
            {
                "/Logistics/MaterialCards",
            }),

        // ── Contact (Cari) ──────────────────────────────────────────────────
        new SchemaProbeDefinition(
            Table: "Contact",
            Columns: new[]
            {
                ("CompanyId",    FirstCompanyId),
                ("AccountCode",  "N'HCTEST'"),
                ("AccountTitle", "N'HCTEST'"),
                // 2026-08-28: kolon adi "CreatedAt" degil "Created" (snake→Pascal gecisinde
                // audit dortlusu adlandirmasina uyduruldu). Probe eski adi kullandigi icin
                // Cari ekrani icin "Invalid column name 'CreatedAt'" uretiyordu.
                ("Created",      "SYSUTCDATETIME()"),
            },
            ScreenPaths: new[]
            {
                "/Contacts",
                "/Contact",
            }),

        // ── Department ──────────────────────────────────────────────────────
        new SchemaProbeDefinition(
            Table: "Department",
            Columns: new[]
            {
                ("CompanyId", FirstCompanyId),
                ("Name",      "N'HCTEST'"),
            },
            ScreenPaths: new[]
            {
                "/Admin/Departments",
            }),

        // ── Machine ─────────────────────────────────────────────────────────
        new SchemaProbeDefinition(
            Table: "Machine",
            Columns: new[]
            {
                ("CompanyId",   FirstCompanyId),
                ("LocationId",  "1"),     // FK: Location.Id — 1 yoksa bu test FK violation atar (beklenen)
                ("Code", "N'HCTEST'"),
            },
            ScreenPaths: new[]
            {
                "/Logistics/Machines",
            }),

        // ── Personnel ───────────────────────────────────────────────────────
        new SchemaProbeDefinition(
            Table: "Personnel",
            Columns: new[]
            {
                ("CompanyId", FirstCompanyId),
                ("Code",      "N'HCTEST'"),
                ("FullName",  "N'HCTEST'"),
            },
            ScreenPaths: new[]
            {
                "/Production/Definitions",
            }),

        // ── Note (Notlar) ───────────────────────────────────────────────────
        // 2026-08-28: tablo adi "notes" olarak kalmisti. Tablo, snake_case →
        // PascalCase gecisinde (MigrateTableRenamesAsync) "Note" olarak yeniden
        // adlandirildigi icin saglik kontrolu her calistiginda
        // "Invalid object name 'dbo.notes'" veriyordu — yani probe'un kendisi
        // bozuktu, Notlar ekraninda bir sorun yoktu (ekran zaten 200 donuyordu).
        new SchemaProbeDefinition(
            Table: "Note",
            Columns: new[]
            {
                ("Id",        "NEWID()"),
                ("CompanyId", FirstCompanyId),
                ("UserId",    FirstUserId),
                ("Title",     "N'HCTEST'"),
                ("Created",   "SYSUTCDATETIME()"),
                ("Updated",   "SYSUTCDATETIME()"),
                ("IsPinned",  "0"),
            },
            ScreenPaths: new[]
            {
                "/Notes",
            }),
    };

    /// <summary>
    /// Belirtilen ekran URL'ine eşleşen probe definition'unu döndürür (case-insensitive).
    /// Tam eşleşme yoksa null.
    /// </summary>
    public static SchemaProbeDefinition? Resolve(string screenPath)
    {
        if (string.IsNullOrEmpty(screenPath)) return null;
        foreach (var def in Definitions)
        {
            foreach (var p in def.ScreenPaths)
            {
                if (string.Equals(p, screenPath, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
        }
        return null;
    }
}
