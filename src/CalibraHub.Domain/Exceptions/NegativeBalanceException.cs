using System.Globalization;

namespace CalibraHub.Domain.Exceptions;

/// <summary>
/// Bir stok-azaltıcı hareket (çıkış / transfer-kaynak / üretim sarfı / negatif düzeltme),
/// eksi bakiye kontrolü açık ve ilgili depoda eksi bakiyeye izin yokken bakiyeyi negatife
/// düşürdüğünde fırlatılır. Yazma transaction'ı geri alınır; controller bu mesajı kullanıcıya
/// gösterir. Tarih bazlı: kontrol, hareket tarihinden itibaren ileriye dönük hiçbir noktada
/// bakiyenin negatife düşmemesini güvence altına alır.
/// </summary>
public sealed class NegativeBalanceException : Exception
{
    public string ItemLabel { get; }
    public string LocationLabel { get; }
    public decimal Shortfall { get; }

    public NegativeBalanceException(string itemLabel, string locationLabel, decimal shortfall)
        : base($"Yetersiz stok: '{itemLabel}' malzemesi '{locationLabel}' deposunda eksi bakiyeye düşüyor " +
               $"(≈ {shortfall.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} ana birim eksik). " +
               "Bu depoda eksi bakiyeye izin verilmiyor.")
    {
        ItemLabel = itemLabel;
        LocationLabel = locationLabel;
        Shortfall = shortfall;
    }
}
