using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Tek seferlik migration: eski `material_card_field_*` + `dynamic_field_values`
/// tablolarindan yeni `WidgetMas` + `WidgetTra` tablolarina veri tasir.
///
/// Idempotent: mevcut WidgetMas satirlarini overwrite etmez (FormId+WidgetCode
/// unique constraint'ine gore skip eder). WidgetTra'da da ayni RecordId icin
/// hic deger yoksa insert eder, aksi halde o kayit icin hicbir deger yazmaz
/// (kullanicinin manuel degisikliklerini korur).
/// </summary>
public interface ILegacyMigrationService
{
    Task<LegacyMigrationReport> MigrateAsync(CancellationToken ct);
}
