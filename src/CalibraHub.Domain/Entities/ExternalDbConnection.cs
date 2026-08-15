using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Harici (dış sistem / ERP) SQL Server bağlantı tanımı — veritabanı üzerinden içe
/// aktarımın kaynağıdır. CalibraHub bu bağlantıya YALNIZCA okuma amaçlı bağlanır;
/// çalıştırılan tek ifade türü SELECT'tir (bkz. <c>IExternalDbReader</c>).
/// Parola DPAPI ile şifreli saklanır (<c>ExternalDbSecretProtector</c>) ve istemciye
/// asla dönülmez.
/// </summary>
[Description("Harici SQL Server bağlantı tanımı: veritabanı üzerinden içe aktarımın kaynağı. Salt-okunur kullanılır, parola şifreli tutulur.")]
public sealed class ExternalDbConnection
{
    public int Id { get; set; }

    /// <summary>Bağlantı adı — benzersiz (kullanıcı kod girmez kuralı: ad üzerinden uniqueness).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Sunucu adı / adresi — "SRV01", "10.0.0.5\SQLEXPRESS", "srv:1433".</summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Kaynak veritabanı adı.</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// Kimlik doğrulama: "Sql" (kullanıcı adı + parola) veya "Integrated"
    /// (uygulama servis hesabıyla Windows kimlik doğrulama).
    /// </summary>
    public string AuthMode { get; set; } = "Sql";

    /// <summary>SQL kimlik doğrulamada kullanıcı adı. Integrated modda null.</summary>
    public string? Username { get; set; }

    /// <summary>DPAPI ile şifrelenmiş parola ("enc:v1:" önekli). İstemciye dönülmez.</summary>
    public string? PasswordEncrypted { get; set; }

    /// <summary>Bağlantı şifrelemesi (TLS). Varsayılan açık.</summary>
    public bool Encrypt { get; set; } = true;

    /// <summary>Sunucu sertifikasına güven — kendinden imzalı sertifikalı iç sunucular için.</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>Bağlantı zaman aşımı (saniye).</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Sorgu zaman aşımı (saniye) — büyük tablolardan okuma için.</summary>
    public int CommandTimeoutSeconds { get; set; } = 120;

    public bool IsActive { get; set; } = true;

    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public int? CreatedById { get; set; }
    public int? UpdatedById { get; set; }

    /// <summary>Windows kimlik doğrulama modunda mı?</summary>
    public bool IsIntegratedSecurity
        => string.Equals(AuthMode, "Integrated", StringComparison.OrdinalIgnoreCase);
}
