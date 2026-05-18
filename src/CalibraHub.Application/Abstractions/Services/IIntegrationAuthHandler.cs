using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// IntegrationApiProfile'in AuthType + AuthConfigJson'una gore HTTP request'e
/// gerekli kimlik dogrulama header'larini ekler.
///
/// Desteklenen tipler (AuthType degerleri — case-insensitive):
///   • None / "" / null         — header eklenmez
///   • OAuth2Password           — Netsis NetOpenX standart akisi: Token URL'ine
///                                grant_type=password + username + password +
///                                extraFields (branchcode, dbname, dbuser, ...)
///                                ile POST atilir, donen access_token Bearer
///                                olarak eklenir. Token in-memory cache'lenir
///                                (expires_in - 60s veya 5dk default).
///   • Bearer / BearerStatic    — config.token alanindaki sabit token Bearer
///   • Basic / BasicAuth        — config.username + config.password Base64
///   • ApiKey                   — config.apiKeyHeader (veya headerName) ile
///                                config.apiKeyValue (veya key) header
///
/// Token expired olursa caller (HttpExecutor) 401 cevabi alinca
/// <see cref="InvalidateToken"/> cagirip yeniden deneyebilir.
/// </summary>
public interface IIntegrationAuthHandler
{
    Task ApplyAuthAsync(
        HttpRequestMessage request,
        IntegrationApiProfile profile,
        CancellationToken ct);

    /// <summary>
    /// OAuth2 token cache'inden bu profile'in token'ini siler.
    /// 401 retry akisi icin (token expired ise yeni token al).
    /// </summary>
    void InvalidateToken(Guid profileId);
}
