namespace CalibraHub.Application.Contracts;

/// <summary>Harici SQL bağlantısının istemciye dönülen hali — parola ASLA yer almaz.</summary>
public sealed record ExternalDbConnectionDto(
    int Id,
    string Name,
    bool UseHostServer,
    string ServerName,
    string DatabaseName,
    string AuthMode,
    string? Username,
    bool HasPassword,
    bool Encrypt,
    bool TrustServerCertificate,
    int ConnectTimeoutSeconds,
    int CommandTimeoutSeconds,
    bool IsActive,
    DateTime Created,
    DateTime? Updated);

/// <summary>
/// Bağlantı kaydetme isteği. <paramref name="Password"/> null ise mevcut parola korunur
/// (istemci parolayı geri almadığı için "boş = değiştirme" semantiği zorunludur).
/// </summary>
public sealed record SaveExternalDbConnectionRequest(
    int Id,
    string Name,
    bool UseHostServer,
    string ServerName,
    string DatabaseName,
    string AuthMode,
    string? Username,
    string? Password,
    bool Encrypt,
    bool TrustServerCertificate,
    int ConnectTimeoutSeconds,
    int CommandTimeoutSeconds,
    bool IsActive);

/// <summary>Bağlantı testi sonucu — sunucu sürümü teşhis için döner.</summary>
public sealed record ExternalDbTestResultDto(bool Ok, string? Error, string? ServerVersion, int? ElapsedMs);

/// <summary>Kaynak veritabanındaki okunabilir nesne (tablo veya view).</summary>
public sealed record ExternalDbObjectDto(string SchemaName, string ObjectName, string Kind)
{
    /// <summary>"dbo.Cari" biçiminde tam ad — istemci bunu tek anahtar olarak kullanır.</summary>
    public string FullName => $"{SchemaName}.{ObjectName}";
}

/// <summary>Kaynak nesnenin bir kolonu — eşleme ekranında gösterilir.</summary>
public sealed record ExternalDbColumnDto(
    string Name,
    string DataType,
    bool IsNullable,
    int? MaxLength,
    int Ordinal);

/// <summary>
/// Kaynaktan okunan ham satırlar. <paramref name="Rows"/> doğrudan
/// <c>ImportRowSet</c>'e beslenebilecek biçimde kolon adı → metin değer sözlükleridir.
/// </summary>
public sealed record ExternalDbSampleDto(
    bool Ok,
    string? Error,
    IReadOnlyList<ExternalDbColumnDto> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    int TotalRead);
