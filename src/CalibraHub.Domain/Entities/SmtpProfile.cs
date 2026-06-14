using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class SmtpProfile : Entity
{
    public int CompanyId { get; init; }
    public required string Name { get; init; }
    public required string FromEmail { get; init; }
    public string FromDisplayName { get; init; } = string.Empty;
    public required string Host { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    /// <summary>Normal, AppPassword, OAuth2</summary>
    public string AuthMethod { get; init; } = "Normal";
    /// <summary>OAuth2 Client ID (Gmail/Outlook OAuth akisi icin)</summary>
    public string? OAuth2ClientId { get; init; }
    /// <summary>OAuth2 Client Secret</summary>
    public string? OAuth2ClientSecret { get; init; }
    /// <summary>OAuth2 Refresh Token</summary>
    public string? OAuth2RefreshToken { get; init; }
    public int Port { get; private set; } = 587;
    public bool UseSsl { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public DateTime Created { get; init; } = DateTime.Now;
    public DateTime Updated { get; private set; } = DateTime.Now;

    public void UpdateTransport(int port, bool useSsl)
    {
        Port = port;
        UseSsl = useSsl;
        Updated = DateTime.Now;
    }

    public void Deactivate()
    {
        IsActive = false;
        Updated = DateTime.Now;
    }
}
