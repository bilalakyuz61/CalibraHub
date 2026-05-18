using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.UnitTests.Domain;

/// <summary>
/// Document entity'sinin rich domain davranis metotlari (rapor §2.2):
///   - RecalculateTotals: invariant + matematik dogrulugu
///   - MarkAsSent / Approve / Reject / Cancel: status transition kurallari
///   - IsEditable: read-only durum kontrolu
///
/// Bu testler ESKİ KOD'A dokunmaz (DocumentService hala dogrudan setter kullanir);
/// sadece yeni davranis metotlarinin kurallarini garanti altina alir. Eski kod
/// yavas yavas bu metotlara migre edildikce test kapsami otomatik artar.
/// </summary>
public sealed class DocumentBehaviorTests
{
    private static Document NewDraft(decimal subTotal = 1000m) => new()
    {
        DocumentNumber = "DOC-001",
        SubTotal = subTotal,
        Status = DocumentStatus.Draft,
    };

    // ════════════════════════════════════════════════════════════════════
    // RecalculateTotals
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void RecalculateTotals_NoDiscountNoTax_GrandTotalEqualsSubTotal()
    {
        var doc = NewDraft(1000m);
        doc.DiscountRate = 0;
        doc.TaxRate = 0;

        doc.RecalculateTotals();

        doc.DiscountAmount.Should().Be(0m);
        doc.TaxAmount.Should().Be(0m);
        doc.GrandTotal.Should().Be(1000m);
    }

    [Fact]
    public void RecalculateTotals_With20PercentTax_AppliesCorrectly()
    {
        var doc = NewDraft(1000m);
        doc.DiscountRate = 0;
        doc.TaxRate = 20m;

        doc.RecalculateTotals();

        doc.TaxAmount.Should().Be(200m);
        doc.GrandTotal.Should().Be(1200m);
    }

    [Fact]
    public void RecalculateTotals_With10PercentDiscountAnd20PercentTax_CorrectOrder()
    {
        // Onemli: tax = (subTotal - discount) * taxRate (discount once)
        var doc = NewDraft(1000m);
        doc.DiscountRate = 10m;
        doc.TaxRate = 20m;

        doc.RecalculateTotals();

        doc.DiscountAmount.Should().Be(100m);              // 1000 * 10%
        doc.TaxAmount.Should().Be(180m);                   // (1000-100) * 20%
        doc.GrandTotal.Should().Be(1080m);                 // 900 + 180
    }

    [Fact]
    public void RecalculateTotals_Rounding_AwayFromZero()
    {
        // 333.33 * 20% = 66.666 — yuvarlanmali (away from zero -> 66.67)
        var doc = NewDraft(333.33m);
        doc.DiscountRate = 0;
        doc.TaxRate = 20m;

        doc.RecalculateTotals();

        doc.TaxAmount.Should().Be(66.67m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void RecalculateTotals_DiscountRateOutOfRange_Throws(decimal badRate)
    {
        var doc = NewDraft();
        doc.DiscountRate = badRate;

        var act = () => doc.RecalculateTotals();
        act.Should().Throw<DomainException>().WithMessage("*DiscountRate*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void RecalculateTotals_TaxRateOutOfRange_Throws(decimal badRate)
    {
        var doc = NewDraft();
        doc.TaxRate = badRate;

        var act = () => doc.RecalculateTotals();
        act.Should().Throw<DomainException>().WithMessage("*TaxRate*");
    }

    [Fact]
    public void RecalculateTotals_NegativeSubTotal_Throws()
    {
        var doc = NewDraft(-50m);
        var act = () => doc.RecalculateTotals();
        act.Should().Throw<DomainException>().WithMessage("*SubTotal*");
    }

    // ════════════════════════════════════════════════════════════════════
    // MarkAsSent (Draft → Sent)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MarkAsSent_FromDraft_TransitionsToSent()
    {
        var doc = NewDraft();
        doc.MarkAsSent("admin");

        doc.Status.Should().Be(DocumentStatus.Sent);
        doc.UpdatedAt.Should().BeOnOrAfter(DateTime.UtcNow.AddSeconds(-5));
    }

    [Theory]
    [InlineData(DocumentStatus.Sent)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Cancelled)]
    [InlineData(DocumentStatus.Converted)]
    public void MarkAsSent_FromNonDraft_Throws(DocumentStatus startingStatus)
    {
        var doc = NewDraft();
        doc.Status = startingStatus;

        var act = () => doc.MarkAsSent("admin");
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    [Fact]
    public void MarkAsSent_EmptyUser_Throws()
    {
        var doc = NewDraft();
        var act = () => doc.MarkAsSent("");
        act.Should().Throw<DomainException>().WithMessage("*userName*");
    }

    // ════════════════════════════════════════════════════════════════════
    // Approve (Sent → Approved)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Approve_FromSent_TransitionsToApproved()
    {
        var doc = NewDraft();
        doc.Status = DocumentStatus.Sent;
        doc.Approve("admin");

        doc.Status.Should().Be(DocumentStatus.Approved);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Rejected)]
    [InlineData(DocumentStatus.Cancelled)]
    public void Approve_FromNonSent_Throws(DocumentStatus startingStatus)
    {
        var doc = NewDraft();
        doc.Status = startingStatus;

        var act = () => doc.Approve("admin");
        act.Should().Throw<DomainException>().WithMessage("*Sent*");
    }

    // ════════════════════════════════════════════════════════════════════
    // Reject (Sent → Rejected)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Reject_FromSent_TransitionsToRejected()
    {
        var doc = NewDraft();
        doc.Status = DocumentStatus.Sent;
        doc.Reject("admin");

        doc.Status.Should().Be(DocumentStatus.Rejected);
    }

    [Fact]
    public void Reject_WithReason_AppendsToNotes()
    {
        var doc = NewDraft();
        doc.Status = DocumentStatus.Sent;
        doc.Reject("admin", "Fiyat yuksek");

        doc.Notes.Should().Contain("Fiyat yuksek");
        doc.Notes.Should().Contain("admin");
    }

    [Fact]
    public void Reject_WithReason_PreservesExistingNotes()
    {
        var doc = NewDraft();
        doc.Status = DocumentStatus.Sent;
        doc.Notes = "Onceki not";
        doc.Reject("admin", "Vazgectik");

        doc.Notes.Should().StartWith("Onceki not");
        doc.Notes.Should().Contain("Vazgectik");
    }

    [Fact]
    public void Reject_FromNonSent_Throws()
    {
        var doc = NewDraft();   // Draft
        var act = () => doc.Reject("admin", "bla");
        act.Should().Throw<DomainException>().WithMessage("*Sent*");
    }

    // ════════════════════════════════════════════════════════════════════
    // Cancel (any non-terminal → Cancelled)
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Sent)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Rejected)]
    public void Cancel_FromNonTerminal_TransitionsToCancelled(DocumentStatus startingStatus)
    {
        var doc = NewDraft();
        doc.Status = startingStatus;
        doc.Cancel("admin");

        doc.Status.Should().Be(DocumentStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FromCancelled_Throws()
    {
        var doc = NewDraft();
        doc.Status = DocumentStatus.Cancelled;
        var act = () => doc.Cancel("admin");
        act.Should().Throw<DomainException>().WithMessage("*zaten*");
    }

    [Fact]
    public void Cancel_FromConverted_Throws()
    {
        var doc = NewDraft();
        doc.Status = DocumentStatus.Converted;
        var act = () => doc.Cancel("admin");
        act.Should().Throw<DomainException>().WithMessage("*donusturulmus*");
    }

    // ════════════════════════════════════════════════════════════════════
    // IsEditable
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(DocumentStatus.Draft, true)]
    [InlineData(DocumentStatus.Sent, true)]
    [InlineData(DocumentStatus.Rejected, true)]
    [InlineData(DocumentStatus.Approved, false)]
    [InlineData(DocumentStatus.Cancelled, false)]
    [InlineData(DocumentStatus.Converted, false)]
    [InlineData(DocumentStatus.Revised, false)]
    public void IsEditable_FollowsStateRules(DocumentStatus status, bool expectedEditable)
    {
        var doc = NewDraft();
        doc.Status = status;
        doc.IsEditable().Should().Be(expectedEditable);
    }
}
