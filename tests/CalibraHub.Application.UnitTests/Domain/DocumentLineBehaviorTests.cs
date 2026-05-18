using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.UnitTests.Domain;

/// <summary>
/// DocumentLine rich domain davranis testleri (rapor §2.2):
///   - CalculateLineTotal: matematik + invariant
///   - ChangeQuantity / ApplyDiscount / ChangeUnitPrice: setter yerine guvenli yol
/// </summary>
public sealed class DocumentLineBehaviorTests
{
    private static DocumentLine NewLine(decimal qty = 10m, decimal price = 100m, decimal discountRate = 0m) => new()
    {
        ItemId = 1,
        Quantity = qty,
        UnitPrice = price,
        DiscountRate = discountRate,
    };

    // ══════════════════════════════════════════════════════════════════
    // CalculateLineTotal
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void CalculateLineTotal_NoDiscount_QuantityTimesPrice()
    {
        var line = NewLine(qty: 5, price: 200);
        line.CalculateLineTotal();
        line.LineTotal.Should().Be(1000m);
    }

    [Fact]
    public void CalculateLineTotal_With10PercentDiscount_CorrectMath()
    {
        var line = NewLine(qty: 10, price: 100, discountRate: 10m);
        line.CalculateLineTotal();
        line.LineTotal.Should().Be(900m);   // 1000 - 100
    }

    [Fact]
    public void CalculateLineTotal_Rounding_AwayFromZero()
    {
        // 7 * 33.33 = 233.31; * 5% discount = 11.6655 -> 11.67; LineTotal = 221.64
        var line = NewLine(qty: 7, price: 33.33m, discountRate: 5m);
        line.CalculateLineTotal();
        line.LineTotal.Should().Be(221.64m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CalculateLineTotal_NonPositiveQuantity_Throws(decimal badQty)
    {
        var line = NewLine(qty: badQty);
        var act = () => line.CalculateLineTotal();
        act.Should().Throw<DomainException>().WithMessage("*Quantity*");
    }

    [Fact]
    public void CalculateLineTotal_NegativeUnitPrice_Throws()
    {
        var line = NewLine(price: -1);
        var act = () => line.CalculateLineTotal();
        act.Should().Throw<DomainException>().WithMessage("*UnitPrice*");
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(101)]
    public void CalculateLineTotal_InvalidDiscountRate_Throws(decimal badRate)
    {
        var line = NewLine(discountRate: badRate);
        var act = () => line.CalculateLineTotal();
        act.Should().Throw<DomainException>().WithMessage("*DiscountRate*");
    }

    // ══════════════════════════════════════════════════════════════════
    // ChangeQuantity
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ChangeQuantity_ValidValue_UpdatesAndRecalculates()
    {
        var line = NewLine(qty: 10, price: 100);
        line.CalculateLineTotal();

        line.ChangeQuantity(7);
        line.Quantity.Should().Be(7);
        line.LineTotal.Should().Be(700m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void ChangeQuantity_NonPositive_Throws(decimal badQty)
    {
        var line = NewLine();
        var act = () => line.ChangeQuantity(badQty);
        act.Should().Throw<DomainException>().WithMessage("*sifir*");
    }

    // ══════════════════════════════════════════════════════════════════
    // ApplyDiscount
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyDiscount_ValidRate_RecalculatesLineTotal()
    {
        var line = NewLine(qty: 10, price: 100);
        line.ApplyDiscount(20m);
        line.DiscountRate.Should().Be(20m);
        line.LineTotal.Should().Be(800m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(150)]
    public void ApplyDiscount_OutOfRange_Throws(decimal badRate)
    {
        var line = NewLine();
        var act = () => line.ApplyDiscount(badRate);
        act.Should().Throw<DomainException>().WithMessage("*DiscountRate*");
    }

    // ══════════════════════════════════════════════════════════════════
    // ChangeUnitPrice
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ChangeUnitPrice_ValidValue_RecalculatesLineTotal()
    {
        var line = NewLine(qty: 5, price: 100);
        line.ChangeUnitPrice(120m);
        line.UnitPrice.Should().Be(120m);
        line.LineTotal.Should().Be(600m);
    }

    [Fact]
    public void ChangeUnitPrice_Negative_Throws()
    {
        var line = NewLine();
        var act = () => line.ChangeUnitPrice(-5);
        act.Should().Throw<DomainException>().WithMessage("*UnitPrice*");
    }
}
