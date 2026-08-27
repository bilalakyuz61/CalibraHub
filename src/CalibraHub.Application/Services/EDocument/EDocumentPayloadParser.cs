using System.Globalization;
using System.Xml.Linq;

namespace CalibraHub.Application.Services.EDocument;

/// <summary>Gelen e-belgenin bir vergi satiri (belge ya da kalem seviyesi).</summary>
public sealed record EDocumentTaxData(
    string? TaxTypeCode,
    string? Name,
    decimal? TaxableAmount,
    decimal? TaxAmount,
    decimal? TaxPercent,
    decimal? CurrencyTaxAmount,
    string? ExemptionReason,
    int? ExemptionReasonCode);

/// <summary>Gelen e-belgenin bir kalemi ve o kaleme ait vergi kirilimi.</summary>
public sealed record EDocumentLineData(
    int LineNumber,
    string? ItemCode,
    string? ItemName,
    string? BuyerItemCode,
    string? ManufacturerCode,
    decimal Quantity,
    string? UnitCode,
    decimal UnitPrice,
    string? CurrencyCode,
    decimal? DiscountAmount,
    decimal? VatRate,
    decimal? LineAmount,
    string? Description,
    IReadOnlyList<EDocumentTaxData> Taxes);

/// <summary>e-Irsaliye tasima bilgisi. Surucu/plaka alanlari SABIT (en fazla uc adet).</summary>
public sealed record EDocumentShipmentData(
    DateTime? DespatchDate,
    string? LicensePlate,
    string? TrailerPlate1,
    string? TrailerPlate2,
    string? TrailerPlate3,
    string? CarrierTaxNumber,
    string? CarrierName,
    string? CarrierCity,
    string? CarrierDistrict,
    string? CarrierCountry,
    string? CarrierPostalCode,
    string? Driver1FirstName, string? Driver1LastName, string? Driver1NationalId,
    string? Driver2FirstName, string? Driver2LastName, string? Driver2NationalId,
    string? Driver3FirstName, string? Driver3LastName, string? Driver3NationalId);

public sealed record EDocumentDetails(
    IReadOnlyList<EDocumentLineData> Lines,
    IReadOnlyList<EDocumentTaxData> DocumentTaxes,
    EDocumentShipmentData? Shipment);

/// <summary>
/// UBL-TR payload'ini (e-fatura / e-arsiv / e-irsaliye) yapisal veriye cevirir.
///
/// <para><b>Neden var:</b> kalemler bugune kadar HICBIR tablodan okunmuyordu — ekran her
/// istekte PayloadRaw icindeki XML'i bastan ayristiriyordu. Bu, kalem/vergi/tasima verisini
/// sorgulanamaz ve raporlanamaz kiliyordu. Ayristirma artik ICE AKTARIMDA BIR KEZ yapilir ve
/// sonuc IncomingDocumentLine / IncomingDocumentTax / IncomingDocumentShipment tablolarina
/// yazilir.</para>
///
/// <para><b>Namespace'e gore DEGIL yerel ada gore eslesir:</b> UBL-TR belgeleri farkli
/// entegratorlerden farkli namespace prefix'leriyle gelir; <c>LocalName</c> uzerinden okumak
/// bu farklara dayaniklidir (mevcut ekran ayristiricisi de ayni yaklasimi kullaniyor).</para>
///
/// <para><b>Asla firlatmaz:</b> bozuk/eksik bir payload ice aktarimi COKERTMEMELIDIR. Cozulemeyen
/// alan null kalir, ayristirilamayan belge <c>null</c> doner ve cagiran taraf ana kaydi yine de
/// yazar — e-belgenin kendisi (PayloadRaw) hicbir kosulda kaybolmaz.</para>
/// </summary>
public static class EDocumentPayloadParser
{
    public static EDocumentDetails? Parse(string? payloadRaw)
    {
        if (string.IsNullOrWhiteSpace(payloadRaw) || !payloadRaw.TrimStart().StartsWith('<'))
            return null;

        try
        {
            var root = XDocument.Parse(payloadRaw).Root;
            if (root is null) return null;

            var lines = new List<EDocumentLineData>();
            var lineNo = 0;

            // e-fatura/e-arsiv -> InvoiceLine, e-irsaliye -> DespatchLine
            foreach (var lineEl in Descendants(root, "InvoiceLine").Concat(Descendants(root, "DespatchLine")))
            {
                lineNo++;
                lines.Add(ParseLine(lineEl, lineNo));
            }

            // Belge (ana) seviyesi vergiler: dogrudan koke bagli TaxTotal.
            var documentTaxes = new List<EDocumentTaxData>();
            foreach (var taxTotal in root.Elements().Where(e => e.Name.LocalName == "TaxTotal"))
                documentTaxes.AddRange(ParseTaxSubtotals(taxTotal));

            return new EDocumentDetails(lines, documentTaxes, ParseShipment(root));
        }
        catch
        {
            // Bozuk XML ice aktarimi durdurmaz; ana kayit yazilmaya devam eder.
            return null;
        }
    }

    private static EDocumentLineData ParseLine(XElement lineEl, int fallbackLineNumber)
    {
        var lineNumber = ParseInt(Val(lineEl, "ID")) ?? fallbackLineNumber;

        // Miktar: faturada InvoicedQuantity, irsaliyede DeliveredQuantity.
        var qtyEl = Elem(lineEl, "InvoicedQuantity") ?? Elem(lineEl, "DeliveredQuantity");
        var quantity = ParseDecimal(qtyEl?.Value) ?? 0m;
        var unitCode = qtyEl?.Attribute("unitCode")?.Value;

        var lineAmountEl = Elem(lineEl, "LineExtensionAmount");
        var lineAmount = ParseDecimal(lineAmountEl?.Value);
        var currency = lineAmountEl?.Attribute("currencyID")?.Value;

        var priceEl = Elem(lineEl, "Price");
        var unitPrice = ParseDecimal(priceEl is null ? null : Val(priceEl, "PriceAmount")) ?? 0m;

        var itemEl = Elem(lineEl, "Item");
        string? itemName = null, sellerCode = null, buyerCode = null, manufacturerCode = null, description = null;
        if (itemEl is not null)
        {
            itemName = Val(itemEl, "Name");
            description = Val(itemEl, "Description");
            sellerCode = IdOf(itemEl, "SellersItemIdentification");
            buyerCode = IdOf(itemEl, "BuyersItemIdentification");
            manufacturerCode = IdOf(itemEl, "ManufacturersItemIdentification");
        }

        // Iskonto: ChargeIndicator=false olan AllowanceCharge (true = ek ucret, iskonto DEGIL).
        decimal? discount = null;
        foreach (var ac in lineEl.Elements().Where(e => e.Name.LocalName == "AllowanceCharge"))
        {
            var isCharge = string.Equals(Val(ac, "ChargeIndicator"), "true", StringComparison.OrdinalIgnoreCase);
            if (isCharge) continue;
            var amount = ParseDecimal(Val(ac, "Amount"));
            if (amount.HasValue) discount = (discount ?? 0m) + amount.Value;
        }

        var taxes = new List<EDocumentTaxData>();
        foreach (var taxTotal in lineEl.Elements().Where(e => e.Name.LocalName == "TaxTotal"))
            taxes.AddRange(ParseTaxSubtotals(taxTotal));

        // KDV orani: kalem vergileri icinde oranı dolu ilk satir.
        var vatRate = taxes.FirstOrDefault(t => t.TaxPercent.HasValue)?.TaxPercent;

        return new EDocumentLineData(
            lineNumber, sellerCode, itemName, buyerCode, manufacturerCode,
            quantity, unitCode, unitPrice, currency, discount, vatRate, lineAmount, description, taxes);
    }

    private static IEnumerable<EDocumentTaxData> ParseTaxSubtotals(XElement taxTotal)
    {
        foreach (var sub in taxTotal.Elements().Where(e => e.Name.LocalName == "TaxSubtotal"))
        {
            var category = Elem(sub, "TaxCategory");
            var scheme = category is null ? null : Elem(category, "TaxScheme");

            yield return new EDocumentTaxData(
                TaxTypeCode: scheme is null ? null : (Val(scheme, "TaxTypeCode") ?? Val(scheme, "ID")),
                Name: scheme is null ? null : Val(scheme, "Name"),
                TaxableAmount: ParseDecimal(Val(sub, "TaxableAmount")),
                TaxAmount: ParseDecimal(Val(sub, "TaxAmount")),
                TaxPercent: ParseDecimal(Val(sub, "Percent")),
                CurrencyTaxAmount: ParseDecimal(Val(sub, "TransactionCurrencyTaxAmount")),
                ExemptionReason: category is null ? null : Val(category, "TaxExemptionReason"),
                ExemptionReasonCode: category is null ? null : ParseInt(Val(category, "TaxExemptionReasonCode")));
        }
    }

    private static EDocumentShipmentData? ParseShipment(XElement root)
    {
        var shipment = Descendants(root, "Shipment").FirstOrDefault();
        if (shipment is null) return null;

        var delivery = Descendants(root, "Delivery").FirstOrDefault();
        var despatchDate = ParseDate(delivery is null ? null : Val(delivery, "ActualDeliveryDate"))
                           ?? ParseDate(Val(shipment, "ActualDeliveryDate"));

        // Plakalar ve suruculer ShipmentStage altinda; birden fazla stage olabilir, sirayla toplanir.
        var plates = new List<string>();
        var drivers = new List<(string? First, string? Last, string? Nid)>();

        foreach (var stage in Descendants(shipment, "ShipmentStage"))
        {
            foreach (var tm in Descendants(stage, "TransportMeans"))
            foreach (var road in Descendants(tm, "RoadTransport"))
            {
                var plate = Val(road, "LicensePlateID");
                if (!string.IsNullOrWhiteSpace(plate)) plates.Add(plate!.Trim());
            }

            foreach (var person in Descendants(stage, "DriverPerson"))
            {
                var nid = Val(person, "NationalityID") ?? IdOf(person, "IdentityDocumentReference");
                drivers.Add((Val(person, "FirstName"), Val(person, "FamilyName"), nid));
            }
        }

        var carrier = Descendants(shipment, "CarrierParty").FirstOrDefault();
        string? carrierName = null, carrierVkn = null, city = null, district = null, country = null, postal = null;
        if (carrier is not null)
        {
            var partyName = Elem(carrier, "PartyName");
            carrierName = partyName is null ? null : Val(partyName, "Name");
            var taxScheme = Elem(carrier, "PartyTaxScheme");
            carrierVkn = taxScheme is null ? null : Val(taxScheme, "CompanyID");
            carrierVkn ??= IdOf(carrier, "PartyIdentification");

            var addr = Elem(carrier, "PostalAddress");
            if (addr is not null)
            {
                city = Val(addr, "CityName");
                district = Val(addr, "CitySubdivisionName");
                postal = Val(addr, "PostalZone");
                var countryEl = Elem(addr, "Country");
                country = countryEl is null ? null : Val(countryEl, "Name");
            }
        }

        string? PlateAt(int i) => i < plates.Count ? plates[i] : null;
        (string? First, string? Last, string? Nid) DriverAt(int i) =>
            i < drivers.Count ? drivers[i] : (null, null, null);

        var d1 = DriverAt(0);
        var d2 = DriverAt(1);
        var d3 = DriverAt(2);

        // Ilk plaka cekici, sonrakiler dorse kabul edilir (UBL ayrimi yapmaz).
        return new EDocumentShipmentData(
            despatchDate, PlateAt(0), PlateAt(1), PlateAt(2), PlateAt(3),
            carrierVkn, carrierName, city, district, country, postal,
            d1.First, d1.Last, d1.Nid,
            d2.First, d2.Last, d2.Nid,
            d3.First, d3.Last, d3.Nid);
    }

    // ── yardimcilar (namespace-bagimsiz, yerel ada gore) ─────────────────────────
    private static IEnumerable<XElement> Descendants(XElement el, string localName) =>
        el.Descendants().Where(e => e.Name.LocalName == localName);

    private static XElement? Elem(XElement el, string localName) =>
        el.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? Val(XElement el, string localName)
    {
        var v = Elem(el, localName)?.Value?.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string? IdOf(XElement parent, string wrapperLocalName)
    {
        var wrapper = Elem(parent, wrapperLocalName);
        return wrapper is null ? null : Val(wrapper, "ID");
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // UBL sayilari her zaman NOKTA ondalikli ve InvariantCulture'dir. Sunucu kulturu
        // tr-TR oldugunda virgul bekleyen bir parse "1234.56" degerini 123456 yapardi.
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTime? ParseDate(string? raw) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;
}
