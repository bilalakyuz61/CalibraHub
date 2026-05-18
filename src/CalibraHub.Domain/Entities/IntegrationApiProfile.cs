using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class IntegrationApiProfile : Entity
{
    public int CompanyId { get; init; }
    public required string Name { get; set; }
    public string AuthType { get; set; } = "None";
    public required string BaseUrl { get; set; }
    public string? AuthConfigJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Hangi entegrasyon provider'ina bagli (Netsis / Logo / SAP / CustomREST).
    /// Wizard field-doc API'sinde dogru katalogtan veri cekmek icin kullanilir.
    /// Default null = "Netsis" (geriye uyum).
    /// </summary>
    public string? ProviderCode { get; set; }
}
