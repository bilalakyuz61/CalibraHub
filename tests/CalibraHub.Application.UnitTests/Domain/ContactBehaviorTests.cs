using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.UnitTests.Domain;

public sealed class ContactBehaviorTests
{
    private static Contact Build(
        string code = "MUS-001",
        string title = "ACME Ltd",
        string? taxNumber = null,
        string? idNumber = null,
        string? email = null,
        string? waPhone = null) =>
        new()
        {
            CompanyId = 1,
            AccountCode = code,
            AccountTitle = title,
            TaxNumber = taxNumber,
            IdentityNumber = idNumber,
            Email = email,
            WaPhone = waPhone,
        };

    [Fact]
    public void EnsureValid_HappyPath_DoesNotThrow()
    {
        var c = Build(taxNumber: "1234567890", email: "info@acme.com", waPhone: "905551234567");
        Action act = c.EnsureValid;
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValid_BlankAccountCode_Throws()
    {
        var c = Build(code: " ");
        Action act = c.EnsureValid;
        act.Should().Throw<DomainException>().WithMessage("*kod*");
    }

    [Fact]
    public void EnsureValid_TaxNumberNot10Digits_Throws()
    {
        var c = Build(taxNumber: "12345");
        Action act = c.EnsureValid;
        act.Should().Throw<DomainException>().WithMessage("*10 hane*");
    }

    [Fact]
    public void EnsureValid_IdentityNumberNot11Digits_Throws()
    {
        var c = Build(idNumber: "12345678");
        Action act = c.EnsureValid;
        act.Should().Throw<DomainException>().WithMessage("*11 hane*");
    }

    [Fact]
    public void EnsureValid_InvalidEmail_Throws()
    {
        var c = Build(email: "not-an-email");
        Action act = c.EnsureValid;
        act.Should().Throw<DomainException>().WithMessage("*e-posta*");
    }

    [Fact]
    public void EnsureValid_WaPhoneTooShort_Throws()
    {
        var c = Build(waPhone: "12345");
        Action act = c.EnsureValid;
        act.Should().Throw<DomainException>().WithMessage("*WhatsApp*");
    }

    [Fact]
    public void NormalizePhone_StripsNonDigits()
    {
        Contact.NormalizePhone("+90 (533) 444-5566").Should().Be("905334445566");
        Contact.NormalizePhone(" ").Should().BeNull();
        Contact.NormalizePhone(null).Should().BeNull();
    }

    [Fact]
    public void IsCorporate_TrueWhenTaxNumberPresent()
    {
        Build(taxNumber: "1234567890").IsCorporate().Should().BeTrue();
        Build(idNumber: "12345678901").IsCorporate().Should().BeFalse();
    }
}
