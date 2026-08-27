using CalibraHub.Application.Collaboration;
using Xunit;

namespace CalibraHub.Tests.Security;

/// <summary>
/// 2026-08-27 — Eşzamanlı düzenleme kilidi sunucu tarafında zorlanmaya başladı.
/// Kilit anahtarı İSTEMCİNİN ürettiği biçimle saklanır (collaboration.js →
/// <c>normalizeToken</c>). Sunucu farklı normalize ederse anahtar tutmaz ve guard
/// SESSİZCE "kilit yok" der — yani hiç çalışmaz, ama hata da vermez. Bu testler o
/// pariteyi sabitler; JS tarafı değişirse burası kırmızıya döner.
///
/// JS algoritması: camelCase sınırına '-', alfanümerik olmayanlar '-', tekrarlı ve
/// baş/son tireler atılır, sonra toLowerCase.
/// </summary>
public class CollaborationLockGuardTests
{
    [Theory]
    // Belge ekranlarının gerçek form kodları (DocumentTypeFormMap.Header)
    [InlineData("SALES_QUOTE_EDIT", "sales-quote-edit")]
    [InlineData("SALES_ORDER_EDIT", "sales-order-edit")]
    [InlineData("PURCHASE_ORDER_EDIT", "purchase-order-edit")]
    // camelCase / PascalCase sınırı
    [InlineData("salesQuote", "sales-quote")]
    [InlineData("WorkOrder", "work-order")]
    // Rakam-harf sınırı ve tekrarlı ayraçlar
    [InlineData("form2Code", "form2-code")]
    [InlineData("A__B--C", "a-b-c")]
    [InlineData("  _SALES_  ", "sales")]
    // Boş / anlamsız girdi
    [InlineData("", "")]
    [InlineData("___", "")]
    [InlineData(null, "")]
    public void NormalizeToken_IstemciJsIleAyniSonucuUretir(string? input, string expected)
        => Assert.Equal(expected, RecordKeyNormalizer.NormalizeToken(input));
}
