using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Admin;

public sealed class IntegratorSettingsManagementViewModel
{
    public required IReadOnlyCollection<IntegratorSettingsDto> Integrators { get; init; }
    public required IReadOnlyCollection<IntegratorImportLogEntryDto> IntegratorImportLogs { get; init; }
    public required IReadOnlyCollection<SmtpProfileDto> SmtpProfiles { get; init; }
    public required IReadOnlyCollection<ErpConnectionSettingsDto> ErpConnections { get; init; }
    public GridListStateViewModel IntegratorListState { get; init; } = new();
    public GridListStateViewModel SmtpListState { get; init; } = new();
    public GridListStateViewModel ErpListState { get; init; } = new();
    public required IReadOnlyCollection<SelectListItem> CompanyOptions { get; init; }
    public required IReadOnlyCollection<SelectListItem> ProviderOptions { get; init; }
    public IntegratorSettingsInput Input { get; init; } = new();
    public SmtpProfileInput SmtpInput { get; init; } = new();
    public ErpConnectionInput ErpInput { get; init; } = new();
    public string? ConnectionTestMessage { get; init; }
    public bool? IsConnectionTestSuccess { get; init; }
    public string? SmtpConnectionTestMessage { get; init; }
    public bool? IsSmtpConnectionTestSuccess { get; init; }
    public string? ErpConnectionTestMessage { get; init; }
    public bool? IsErpConnectionTestSuccess { get; init; }
}

public sealed class IntegratorSettingsInput
{
    public int? Id { get; set; }

    public int? CompanyId { get; set; }

    [Required(ErrorMessage = "Saglayici secimi zorunludur.")]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "Entegrator adi en fazla 100 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Base URL zorunludur.")]
    [MaxLength(300, ErrorMessage = "Base URL en fazla 300 karakter olabilir.")]
    public string BaseUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "VKN zorunludur.")]
    [MaxLength(20, ErrorMessage = "VKN en fazla 20 karakter olabilir.")]
    public string CompanyTaxNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kullanici adi zorunludur.")]
    [MaxLength(120, ErrorMessage = "Kullanici adi en fazla 120 karakter olabilir.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Sifre zorunludur.")]
    [MaxLength(200, ErrorMessage = "Sifre en fazla 200 karakter olabilir.")]
    public string Secret { get; set; } = string.Empty;

    [Range(10, 3600, ErrorMessage = "Polling suresi 10 ile 3600 saniye arasinda olmali.")]
    public int PollingIntervalSeconds { get; set; } = 120;

    [Range(1, 5000, ErrorMessage = "Max kayit pull 1 ile 5000 arasinda olmali.")]
    public int MaxRecordsPerPull { get; set; } = 200;

    [Range(1, 3650, ErrorMessage = "Log saklama suresi 1 ile 3650 gun arasinda olmali.")]
    public int LogRetentionDays { get; set; } = 30;

    public bool IncludeReceivedDocumentsInPull { get; set; }

    public bool MarkDownloadedDocumentsAsReceived { get; set; }

    public bool IncludeIssuedEInvoicesInPull { get; set; }

    public bool IncludeIssuedEArchivesInPull { get; set; }

    public bool IncludeIssuedEDispatchesInPull { get; set; }

    public bool IsActive { get; set; } = true;

    public bool ScheduleEnabled { get; set; }

    [MaxLength(100, ErrorMessage = "Uygulama adi en fazla 100 karakter olabilir.")]
    public string? AppStr { get; set; }

    [MaxLength(20, ErrorMessage = "Kaynak tipi en fazla 20 karakter olabilir.")]
    public string? Source { get; set; }

    [MaxLength(20, ErrorMessage = "Versiyon en fazla 20 karakter olabilir.")]
    public string? AppVersion { get; set; }

    [Range(5, 300, ErrorMessage = "Zaman asimi 5-300 saniye arasinda olmali.")]
    public int TimeoutSeconds { get; set; } = 30;

    [Range(1, 3650, ErrorMessage = "Geriye bakis suresi 1-3650 gun arasinda olmali.")]
    public int LookbackDays { get; set; } = 30;
}

public sealed class SmtpProfileInput
{
    public int? Id { get; set; }

    public int? CompanyId { get; set; }

    [MaxLength(120, ErrorMessage = "SMTP profil adi en fazla 120 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Gecerli bir e-posta giriniz.")]
    [MaxLength(160, ErrorMessage = "Gonderen e-posta en fazla 160 karakter olabilir.")]
    public string FromEmail { get; set; } = string.Empty;

    [MaxLength(120, ErrorMessage = "Gorunen ad en fazla 120 karakter olabilir.")]
    public string FromDisplayName { get; set; } = string.Empty;

    [MaxLength(200, ErrorMessage = "SMTP host en fazla 200 karakter olabilir.")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "SMTP port 1 ile 65535 arasinda olmali.")]
    public int Port { get; set; } = 587;

    [MaxLength(160, ErrorMessage = "SMTP kullanici adi en fazla 160 karakter olabilir.")]
    public string Username { get; set; } = string.Empty;

    [MaxLength(300, ErrorMessage = "SMTP sifresi en fazla 300 karakter olabilir.")]
    public string Password { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Gecerli bir deneme alici e-posta adresi giriniz.")]
    [MaxLength(160, ErrorMessage = "Deneme alici e-posta en fazla 160 karakter olabilir.")]
    public string TestRecipientEmail { get; set; } = string.Empty;

    /// <summary>Normal, AppPassword, OAuth2</summary>
    public string AuthMethod { get; set; } = "Normal";

    [MaxLength(300)]
    public string? OAuth2ClientId { get; set; }

    [MaxLength(300)]
    public string? OAuth2ClientSecret { get; set; }

    [MaxLength(500)]
    public string? OAuth2RefreshToken { get; set; }

    public bool UseSsl { get; set; } = true;

    public bool IsActive { get; set; } = true;
}

public sealed class ErpConnectionInput
{
    public Guid? Id { get; set; }
    
    [Required(ErrorMessage = "Sirket secimi zorunludur.")]
    public int? CompanyId { get; set; }
    public string Provider { get; set; } = "Netsis";

    [Required(ErrorMessage = "Sirket alani zorunludur.")]
    [MaxLength(50, ErrorMessage = "Sirket alani en fazla 50 karakter olabilir.")]
    public string Company { get; set; } = string.Empty;

    [Required(ErrorMessage = "Isletme alani zorunludur.")]
    [MaxLength(100, ErrorMessage = "Isletme alani en fazla 100 karakter olabilir.")]
    public string Business { get; set; } = string.Empty;

    [Required(ErrorMessage = "Sube alani zorunludur.")]
    [MaxLength(100, ErrorMessage = "Sube alani en fazla 100 karakter olabilir.")]
    public string Branch { get; set; } = string.Empty;

    [Required(ErrorMessage = "ERP kullanici adi zorunludur.")]
    [MaxLength(160, ErrorMessage = "ERP kullanici adi en fazla 160 karakter olabilir.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "ERP sifresi zorunludur.")]
    [MaxLength(300, ErrorMessage = "ERP sifresi en fazla 300 karakter olabilir.")]
    public string Password { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
