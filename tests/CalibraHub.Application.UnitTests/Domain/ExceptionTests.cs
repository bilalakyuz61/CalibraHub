using CalibraHub.Domain.Common;

namespace CalibraHub.Application.UnitTests.Domain;

/// <summary>
/// Domain.Common exception sinif sozlesmeleri:
///   - DomainException.ThrowIf: condition-true atisi yapar
///   - NotFoundException: resourceType+id formatlama
///   - ValidationException: field-bazli errors dict
/// </summary>
public sealed class ExceptionTests
{
    // ── DomainException ──────────────────────────────────────────────────

    [Fact]
    public void DomainException_ThrowIf_True_Throws()
    {
        var act = () => DomainException.ThrowIf(true, "bozuk");
        act.Should().Throw<DomainException>().WithMessage("bozuk");
    }

    [Fact]
    public void DomainException_ThrowIf_False_NoThrow()
    {
        var act = () => DomainException.ThrowIf(false, "asla");
        act.Should().NotThrow();
    }

    // ── NotFoundException ────────────────────────────────────────────────

    [Fact]
    public void NotFoundException_TypeAndId_ProducesFormattedMessage()
    {
        var ex = new NotFoundException("Document", "42");

        ex.Message.Should().Contain("Document").And.Contain("42");
        ex.ResourceType.Should().Be("Document");
        ex.ResourceId.Should().Be("42");
    }

    [Fact]
    public void NotFoundException_PlainMessage_NoResourceMetadata()
    {
        var ex = new NotFoundException("Kaydi bulamadik");

        ex.Message.Should().Be("Kaydi bulamadik");
        ex.ResourceType.Should().BeNull();
        ex.ResourceId.Should().BeNull();
    }

    // ── ValidationException ──────────────────────────────────────────────

    [Fact]
    public void ValidationException_SingleMessage_PopulatesEmptyKeyError()
    {
        var ex = new ValidationException("Email zorunlu");

        ex.Message.Should().Be("Email zorunlu");
        ex.Errors.Should().HaveCount(1);
        ex.Errors[""].Should().ContainSingle().Which.Should().Be("Email zorunlu");
    }

    [Fact]
    public void ValidationException_FieldErrors_PreservesDictionary()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Email"]    = new[] { "Format gecersiz" },
            ["Password"] = new[] { "Min 8 karakter", "1 rakam zorunlu" },
        };

        var ex = new ValidationException(errors);

        ex.Errors.Should().HaveCount(2);
        ex.Errors["Email"].Should().ContainSingle();
        ex.Errors["Password"].Should().HaveCount(2);
    }

    [Fact]
    public void ValidationException_NullDictionary_DefaultsToEmpty()
    {
        var ex = new ValidationException(errors: null!);
        ex.Errors.Should().BeEmpty();
    }
}
