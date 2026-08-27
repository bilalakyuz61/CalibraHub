using CalibraHub.Application.Services.EDocument;
using Xunit;

namespace CalibraHub.Tests.Services;

/// <summary>
/// UBL-TR payload ayristiricisi. Bu testler VERITABANI ISTEMEZ.
///
/// <para>Ornek XML'ler namespace prefix'i TASIR ve elemanlar bilerek farkli prefix'lerle
/// yazilmistir: gercek e-belgeler entegratore gore farkli prefix kullanir, ayristirici
/// namespace'e degil YEREL ADA bakmalidir.</para>
/// </summary>
public sealed class EDocumentPayloadParserTests
{
    private const string InvoiceXml = """
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cac:TaxTotal>
            <cbc:TaxAmount currencyID="TRY">36.00</cbc:TaxAmount>
            <cac:TaxSubtotal>
              <cbc:TaxableAmount currencyID="TRY">180.00</cbc:TaxableAmount>
              <cbc:TaxAmount currencyID="TRY">36.00</cbc:TaxAmount>
              <cbc:Percent>20</cbc:Percent>
              <cac:TaxCategory>
                <cac:TaxScheme><cbc:Name>KDV</cbc:Name><cbc:TaxTypeCode>0015</cbc:TaxTypeCode></cac:TaxScheme>
              </cac:TaxCategory>
            </cac:TaxSubtotal>
          </cac:TaxTotal>
          <cac:InvoiceLine>
            <cbc:ID>1</cbc:ID>
            <cbc:InvoicedQuantity unitCode="C62">3</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID="TRY">180.00</cbc:LineExtensionAmount>
            <cac:AllowanceCharge>
              <cbc:ChargeIndicator>false</cbc:ChargeIndicator>
              <cbc:Amount currencyID="TRY">20.00</cbc:Amount>
            </cac:AllowanceCharge>
            <cac:AllowanceCharge>
              <cbc:ChargeIndicator>true</cbc:ChargeIndicator>
              <cbc:Amount currencyID="TRY">5.00</cbc:Amount>
            </cac:AllowanceCharge>
            <cac:TaxTotal>
              <cac:TaxSubtotal>
                <cbc:TaxableAmount currencyID="TRY">180.00</cbc:TaxableAmount>
                <cbc:TaxAmount currencyID="TRY">36.00</cbc:TaxAmount>
                <cbc:Percent>20</cbc:Percent>
                <cac:TaxCategory>
                  <cac:TaxScheme><cbc:Name>KDV</cbc:Name></cac:TaxScheme>
                </cac:TaxCategory>
              </cac:TaxSubtotal>
            </cac:TaxTotal>
            <cac:Item>
              <cbc:Name>Vida M8</cbc:Name>
              <cbc:Description>Paslanmaz</cbc:Description>
              <cac:SellersItemIdentification><cbc:ID>STK-001</cbc:ID></cac:SellersItemIdentification>
              <cac:BuyersItemIdentification><cbc:ID>ALICI-9</cbc:ID></cac:BuyersItemIdentification>
            </cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="TRY">66.67</cbc:PriceAmount></cac:Price>
          </cac:InvoiceLine>
        </Invoice>
        """;

    private const string DespatchXml = """
        <DespatchAdvice xmlns="urn:oasis:names:specification:ubl:schema:xsd:DespatchAdvice-2"
                        xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                        xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cac:Shipment>
            <cac:ShipmentStage>
              <cac:TransportMeans>
                <cac:RoadTransport><cbc:LicensePlateID>34ABC123</cbc:LicensePlateID></cac:RoadTransport>
              </cac:TransportMeans>
              <cac:TransportMeans>
                <cac:RoadTransport><cbc:LicensePlateID>34DORSE1</cbc:LicensePlateID></cac:RoadTransport>
              </cac:TransportMeans>
              <cac:DriverPerson>
                <cbc:FirstName>Ahmet</cbc:FirstName>
                <cbc:FamilyName>Yilmaz</cbc:FamilyName>
                <cbc:NationalityID>12345678901</cbc:NationalityID>
              </cac:DriverPerson>
              <cac:DriverPerson>
                <cbc:FirstName>Mehmet</cbc:FirstName>
                <cbc:FamilyName>Kaya</cbc:FamilyName>
                <cbc:NationalityID>98765432109</cbc:NationalityID>
              </cac:DriverPerson>
            </cac:ShipmentStage>
            <cac:CarrierParty>
              <cac:PartyName><cbc:Name>Hizli Lojistik A.S.</cbc:Name></cac:PartyName>
              <cac:PartyTaxScheme><cbc:CompanyID>1112223334</cbc:CompanyID></cac:PartyTaxScheme>
              <cac:PostalAddress>
                <cbc:CityName>Istanbul</cbc:CityName>
                <cbc:CitySubdivisionName>Kartal</cbc:CitySubdivisionName>
              </cac:PostalAddress>
            </cac:CarrierParty>
          </cac:Shipment>
          <cac:DespatchLine>
            <cbc:ID>1</cbc:ID>
            <cbc:DeliveredQuantity unitCode="KGM">12.5</cbc:DeliveredQuantity>
            <cac:Item><cbc:Name>Sac Levha</cbc:Name></cac:Item>
          </cac:DespatchLine>
        </DespatchAdvice>
        """;

    [Fact]
    public void Fatura_kalemi_ve_vergisi_ayristirilir()
    {
        var d = EDocumentPayloadParser.Parse(InvoiceXml);

        Assert.NotNull(d);
        var line = Assert.Single(d!.Lines);

        Assert.Equal(1, line.LineNumber);
        Assert.Equal("STK-001", line.ItemCode);
        Assert.Equal("Vida M8", line.ItemName);
        Assert.Equal("ALICI-9", line.BuyerItemCode);
        Assert.Equal("Paslanmaz", line.Description);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal("C62", line.UnitCode);
        Assert.Equal("TRY", line.CurrencyCode);
        Assert.Equal(180.00m, line.LineAmount);

        // UBL sayilari NOKTA ondaliklidir; sunucu kulturu tr-TR olsa bile 66.67 -> 6667 OLMAMALI.
        Assert.Equal(66.67m, line.UnitPrice);

        // Yalniz ChargeIndicator=false olan iskontodur; true olan (5.00) EK UCRET, dahil edilmez.
        Assert.Equal(20.00m, line.DiscountAmount);

        Assert.Equal(20m, line.VatRate);
        var lineTax = Assert.Single(line.Taxes);
        Assert.Equal("KDV", lineTax.Name);
        Assert.Equal(180.00m, lineTax.TaxableAmount);
        Assert.Equal(36.00m, lineTax.TaxAmount);
    }

    [Fact]
    public void Belge_seviyesi_vergi_kalem_vergisinden_AYRI_toplanir()
    {
        var d = EDocumentPayloadParser.Parse(InvoiceXml);

        var docTax = Assert.Single(d!.DocumentTaxes);
        Assert.Equal("0015", docTax.TaxTypeCode);
        Assert.Equal(36.00m, docTax.TaxAmount);

        // Kalem vergisi belge vergisine karismamali (tabloda LineId ile ayrisiyor).
        Assert.Single(d.Lines[0].Taxes);
    }

    [Fact]
    public void Irsaliyede_surucu_ve_plakalar_okunur()
    {
        var d = EDocumentPayloadParser.Parse(DespatchXml);

        Assert.NotNull(d);
        var ship = d!.Shipment;
        Assert.NotNull(ship);

        // Ilk plaka cekici, sonraki dorse.
        Assert.Equal("34ABC123", ship!.LicensePlate);
        Assert.Equal("34DORSE1", ship.TrailerPlate1);
        Assert.Null(ship.TrailerPlate2);

        Assert.Equal("Ahmet", ship.Driver1FirstName);
        Assert.Equal("Yilmaz", ship.Driver1LastName);
        Assert.Equal("12345678901", ship.Driver1NationalId);
        Assert.Equal("Mehmet", ship.Driver2FirstName);
        Assert.Null(ship.Driver3FirstName);

        Assert.Equal("Hizli Lojistik A.S.", ship.CarrierName);
        Assert.Equal("1112223334", ship.CarrierTaxNumber);
        Assert.Equal("Istanbul", ship.CarrierCity);
        Assert.Equal("Kartal", ship.CarrierDistrict);

        // Irsaliye kalemi DespatchLine'dan gelir (InvoiceLine degil).
        var line = Assert.Single(d.Lines);
        Assert.Equal("Sac Levha", line.ItemName);
        Assert.Equal(12.5m, line.Quantity);
        Assert.Equal("KGM", line.UnitCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ \"json\": true }")]
    [InlineData("<Invoice><bozuk")]
    public void Bozuk_veya_bos_payload_ISTISNA_FIRLATMAZ(string? payload)
    {
        // Ice aktarim asla cokmemeli: ayristirilamayan belge null doner, ana kayit yine yazilir.
        var d = EDocumentPayloadParser.Parse(payload);
        Assert.Null(d);
    }

    [Fact]
    public void Kalemsiz_belge_bos_liste_dondurur_null_DEGIL()
    {
        var xml = "<Invoice xmlns:cbc=\"x\"><cbc:ID>A1</cbc:ID></Invoice>";
        var d = EDocumentPayloadParser.Parse(xml);

        Assert.NotNull(d);
        Assert.Empty(d!.Lines);
        Assert.Empty(d.DocumentTaxes);
        Assert.Null(d.Shipment);
    }
}
