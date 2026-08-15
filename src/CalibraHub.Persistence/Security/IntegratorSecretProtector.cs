using System.Text;

namespace CalibraHub.Persistence.Security;

/// <summary>
/// e-Fatura entegratör gizli değerleri (Secret) için DPAPI koruması.
/// Entropy DEĞİŞTİRİLEMEZ — saklanmış mevcut değerlerin çözülebilmesi buna bağlıdır.
/// </summary>
internal static class IntegratorSecretProtector
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("CalibraHub.IntegratorSettings.Secret.v1");

    public static string Protect(string secret) => DpapiSecretProtector.Protect(secret, Entropy);

    public static string Unprotect(string value) => DpapiSecretProtector.Unprotect(value, Entropy);
}
