using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.UnitTests.Domain;

/// <summary>
/// Document.AddLine / RemoveLine — Lines koleksiyonu + otomatik recalculate.
/// Rich domain testleri: line ekleyince SubTotal + GrandTotal otomatik guncellenmeli.
/// </summary>
public sealed class DocumentAddRemoveLineTests
{
    private static Document NewDraft(decimal taxRate = 20m) => new()
    {
        DocumentNumber = "DOC-001",
        Status = DocumentStatus.Draft,
        TaxRate = taxRate,
    };

    private static DocumentLine NewLine(int id = 0, decimal qty = 10m, decimal price = 100m) => new()
    {
        Id = id,
        ItemId = 1,
        Quantity = qty,
        UnitPrice = price,
    };

    // ══════════════════════════════════════════════════════════════════
    // AddLine
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void AddLine_FirstLine_LineNoIs1()
    {
        var doc = NewDraft();
        doc.AddLine(NewLine(qty: 5, price: 100));

        doc.Lines.Should().ContainSingle();
        doc.Lines[0].LineNo.Should().Be(1);
        doc.Lines[0].LineTotal.Should().Be(500m);
    }

    [Fact]
    public void AddLine_SecondLine_LineNoIncrements()
    {
        var doc = NewDraft();
        doc.AddLine(NewLine(qty: 2, price: 50));
        doc.AddLine(NewLine(qty: 3, price: 200));

        doc.Lines.Should().HaveCount(2);
        doc.Lines[0].LineNo.Should().Be(1);
        doc.Lines[1].LineNo.Should().Be(2);
    }

    [Fact]
    public void AddLine_TriggersRecalculate_SubTotalAndGrandTotalUpdated()
    {
        // 2 line: 100 + 200 = 300 SubTotal; %20 vergi = 360 GrandTotal
        var doc = NewDraft(taxRate: 20m);
        doc.AddLine(NewLine(qty: 1, price: 100));
        doc.AddLine(NewLine(qty: 1, price: 200));

        doc.SubTotal.Should().Be(300m);
        doc.TaxAmount.Should().Be(60m);
        doc.GrandTotal.Should().Be(360m);
    }

    [Fact]
    public void AddLine_NullLine_Throws()
    {
        var doc = NewDraft();
        var act = () => doc.AddLine(null!);
        act.Should().Throw<DomainException>().WithMessage("*null*");
    }

    [Fact]
    public void AddLine_ItemIdZero_Throws()
    {
        var doc = NewDraft();
        var badLine = NewLine();
        badLine.ItemId = 0;
        var act = () => doc.AddLine(badLine);
        act.Should().Throw<DomainException>().WithMessage("*ItemId*");
    }

    [Theory]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Cancelled)]
    [InlineData(DocumentStatus.Converted)]
    public void AddLine_NonEditableStatus_Throws(DocumentStatus status)
    {
        var doc = NewDraft();
        doc.Status = status;
        var act = () => doc.AddLine(NewLine());
        act.Should().Throw<DomainException>().WithMessage("*duzenlenemez*");
    }

    [Fact]
    public void AddLine_InvalidLineQuantity_Throws()
    {
        var doc = NewDraft();
        var bad = NewLine(qty: 0);  // Quantity > 0 olmali
        var act = () => doc.AddLine(bad);
        act.Should().Throw<DomainException>().WithMessage("*Quantity*");
    }

    // ══════════════════════════════════════════════════════════════════
    // RemoveLine
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void RemoveLine_ExistingId_RemovesAndRecalculates()
    {
        var doc = NewDraft(taxRate: 0);
        var line1 = NewLine(id: 100, qty: 1, price: 100);
        var line2 = NewLine(id: 200, qty: 1, price: 50);
        doc.AddLine(line1);
        doc.AddLine(line2);
        doc.SubTotal.Should().Be(150m);

        doc.RemoveLine(100);

        doc.Lines.Should().ContainSingle();
        doc.Lines[0].Id.Should().Be(200);
        doc.SubTotal.Should().Be(50m);
        doc.GrandTotal.Should().Be(50m);
    }

    [Fact]
    public void RemoveLine_NonExistentId_Throws()
    {
        var doc = NewDraft();
        doc.AddLine(NewLine(id: 1));

        var act = () => doc.RemoveLine(999);
        act.Should().Throw<DomainException>().WithMessage("*bulunamadi*");
    }

    [Theory]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Cancelled)]
    public void RemoveLine_NonEditableStatus_Throws(DocumentStatus status)
    {
        var doc = NewDraft();
        doc.AddLine(NewLine(id: 1));
        doc.Status = status;

        var act = () => doc.RemoveLine(1);
        act.Should().Throw<DomainException>().WithMessage("*duzenlenemez*");
    }

    // ══════════════════════════════════════════════════════════════════
    // RecalculateSubTotalFromLines
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void RecalculateSubTotalFromLines_EmptyLines_ZeroSubTotal()
    {
        var doc = NewDraft();
        doc.RecalculateSubTotalFromLines();
        doc.SubTotal.Should().Be(0m);
    }

    [Fact]
    public void RecalculateSubTotalFromLines_SumsAllLineTotals()
    {
        var doc = NewDraft();
        doc.AddLine(NewLine(qty: 2, price: 100));   // 200
        doc.AddLine(NewLine(qty: 3, price: 50));    // 150
        doc.SubTotal.Should().Be(350m);
    }
}
