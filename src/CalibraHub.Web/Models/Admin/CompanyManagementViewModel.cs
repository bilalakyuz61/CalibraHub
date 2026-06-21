using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Shared;

namespace CalibraHub.Web.Models.Admin;

public sealed class CompanyManagementViewModel
{
    public required IReadOnlyCollection<CompanyDto> Companies { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public CompanyInput Input { get; init; } = new();
    public SmtpProfileInput SmtpInput { get; init; } = new();
    public SmtpProfileDto? CurrentSmtp { get; init; }
}

public sealed class CompanyInput
{
    public int? Id { get; set; }

    [MaxLength(120, ErrorMessage = "Sirket adi en fazla 120 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200, ErrorMessage = "Unvan en fazla 200 karakter olabilir.")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Adres en fazla 500 karakter olabilir.")]
    public string Address { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? District { get; set; }

    [MaxLength(10)]
    public string? PostalCode { get; set; }

    [MaxLength(100, ErrorMessage = "Vergi dairesi en fazla 100 karakter olabilir.")]
    public string TaxOffice { get; set; } = string.Empty;

    [MaxLength(20, ErrorMessage = "Vergi numarasi en fazla 20 karakter olabilir.")]
    public string TaxNumber { get; set; } = string.Empty;

    public bool IsEDocumentApprovalEnabled { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(500, ErrorMessage = "Veritabani baglantisi en fazla 500 karakter olabilir.")]
    public string? DatabaseConnectionString { get; set; }

    [MaxLength(300)]
    public string? PublicBaseUrl { get; set; }
}
