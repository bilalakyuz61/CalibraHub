using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Sirket tanimlari. Her sirket icin ayri connection string (per-company DB mimarisi). Login sirasinda sirket secimi bu tablodan yapilir. IsEDocumentApprovalEnabled = e-fatura / e-irsaliye onay akislarinin aktif olup olmadigini belirler.")]
public sealed class Company
{
    public int Id { get; set; }
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Address { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? PostalCode { get; init; }
    public required string TaxOffice { get; init; }
    public required string TaxNumber { get; init; }
    public bool IsEDocumentApprovalEnabled { get; init; }
    public string? DatabaseConnectionString { get; init; }
    /// <summary>
    /// FAZ 1 (2026-07-19): Sunucu/kullanici/parola tasimayan, yalnizca veritabani adi.
    /// Doluysa calisma zamani baglantisi master connection string + bu ad ile kurulur
    /// (bkz. Program.cs ResolveCompanyConnectionString); bossa DatabaseConnectionString'e
    /// (tam dize, guvenlik agi) dusulur. DatabaseConnectionString kolonu bilinçli olarak
    /// silinmedi — bkz. CLAUDE.md.
    /// </summary>
    public string? DatabaseName { get; init; }
    public string? PublicBaseUrl { get; init; }
    public bool IsActive { get; private set; } = true;

    public void Deactivate() => IsActive = false;
}
