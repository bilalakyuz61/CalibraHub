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
/// FK gereken yerlerde 0 veya hardcoded ID kullan — ROLLBACK olduğu için sorun yok,
/// ama FK constraint check edilirken hata vermesin diye **var olan** bir ID ver
/// (örn. Department için CompanyId=1 koy; CompanyId=0 zaten DEFAULT'tan kabul edilir).
/// </summary>
public static class SchemaProbeRegistry
{
    public static readonly IReadOnlyList<SchemaProbeDefinition> Definitions = new[]
    {
        // ── Items (Malzeme Kartı) ────────────────────────────────────────────
        new SchemaProbeDefinition(
            Table: "Items",
            Columns: new[]
            {
                ("Code",      "N'HCTEST'"),
                ("Name",      "N'HCTEST'"),
                ("CompanyId", "0"),
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
                ("CompanyId",    "0"),
                ("AccountCode",  "N'HCTEST'"),
                ("AccountTitle", "N'HCTEST'"),
                ("CreatedAt",    "SYSUTCDATETIME()"),
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
                ("CompanyId", "0"),
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
                ("CompanyId",   "0"),
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
                ("CompanyId", "0"),
                ("Code",      "N'HCTEST'"),
                ("FullName",  "N'HCTEST'"),
            },
            ScreenPaths: new[]
            {
                "/Production/Definitions",
            }),

        // ── notes (legacy snake_case tablo, PascalCase kolonlu) ─────────────
        new SchemaProbeDefinition(
            Table: "notes",
            Columns: new[]
            {
                ("Id",        "NEWID()"),
                ("CompanyId", "0"),
                ("UserId",    "0"),
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
