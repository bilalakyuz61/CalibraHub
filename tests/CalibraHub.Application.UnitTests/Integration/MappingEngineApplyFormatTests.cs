using CalibraHub.Application.Services.Integration;

namespace CalibraHub.Application.UnitTests.Integration;

/// <summary>
/// MappingEngine.ApplyFormat — entegrasyon mapping'inde her alanın hedef tipe
/// (decimal/date/bool/string) cevrilmesi + format pattern uygulanmasi.
/// Bu metot pure (DB veya I/O yok), test edilmesi kolay + degeri yuksek.
/// </summary>
public sealed class MappingEngineApplyFormatTests
{
    // ── DateTime ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyFormat_DateTime_DefaultPattern_ReturnsIsoString()
    {
        var dt = new DateTime(2026, 5, 17, 14, 30, 45);
        var result = MappingEngine.ApplyFormat(dt, "datetime", null);
        result.Should().Be("2026-05-17T14:30:45");
    }

    [Fact]
    public void ApplyFormat_DateTime_CustomPattern_UsesPattern()
    {
        var dt = new DateTime(2026, 5, 17);
        var result = MappingEngine.ApplyFormat(dt, "date", "yyyy-MM-dd");
        result.Should().Be("2026-05-17");
    }

    [Fact]
    public void ApplyFormat_DateString_TurkishCulture_ParsesAndFormats()
    {
        // "tr-TR" formatinda kullanici girdisi - parse edilip ISO'ya cevrilmeli
        var input = "17.05.2026";
        var result = MappingEngine.ApplyFormat(input, "date", "yyyy-MM-dd");
        result.Should().Be("2026-05-17");
    }

    // ── Decimal ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyFormat_Decimal_DefaultF2_ReturnsTwoDecimalPlaces()
    {
        var result = MappingEngine.ApplyFormat(123.456m, "decimal", null);
        result.Should().Be(123.46m);
    }

    [Fact]
    public void ApplyFormat_Decimal_CustomFormat_F4_KeepsFourDecimals()
    {
        var result = MappingEngine.ApplyFormat(123.456789m, "numeric", "F4");
        result.Should().Be(123.4568m);
    }

    [Fact]
    public void ApplyFormat_StringWithDot_ParsedAsDecimal()
    {
        // Invariant culture: 1234.56 -> 1234.56
        var result = MappingEngine.ApplyFormat("1234.56", "decimal", "F2");
        result.Should().Be(1234.56m);
    }

    [Fact]
    public void ApplyFormat_StringWithComma_ParsedAsTurkishDecimal()
    {
        // Turkish culture: 1234,56 -> 1234.56 — onceden BUG'liydi (invariant comma'yi binlik
        // ayirici olarak yiyor 123456.00 donuyordu). TryParseDecimalString heuristik fix ile cozuldu.
        var result = MappingEngine.ApplyFormat("1234,56", "decimal", "F2");
        result.Should().Be(1234.56m);
    }

    [Fact]
    public void ApplyFormat_StringWithTurkishThousandAndDecimal_ParsesCorrectly()
    {
        // "1.234,56" -> tr-TR: binlik nokta + virgul decimal -> 1234.56
        var result = MappingEngine.ApplyFormat("1.234,56", "decimal", "F2");
        result.Should().Be(1234.56m);
    }

    [Fact]
    public void ApplyFormat_StringWithEnglishThousandAndDecimal_ParsesCorrectly()
    {
        // "1,234.56" -> invariant: binlik virgul + nokta decimal -> 1234.56
        var result = MappingEngine.ApplyFormat("1,234.56", "decimal", "F2");
        result.Should().Be(1234.56m);
    }

    [Fact]
    public void ApplyFormat_BigNumber_TurkishFormat_ParsesCorrectly()
    {
        // "1.234.567,89" -> tr-TR: 1234567.89
        var result = MappingEngine.ApplyFormat("1.234.567,89", "decimal", "F2");
        result.Should().Be(1234567.89m);
    }

    // ── Int ──────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyFormat_Int_DecimalSource_Truncates()
    {
        var result = MappingEngine.ApplyFormat(42.7m, "int", null);
        result.Should().Be(42);
    }

    [Fact]
    public void ApplyFormat_Int_StringSource_Parses()
    {
        var result = MappingEngine.ApplyFormat("99", "integer", null);
        result.Should().Be(99);
    }

    // ── Bool ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public void ApplyFormat_Bool_RecognizesCommonStrings(string input, bool expected)
    {
        var result = MappingEngine.ApplyFormat(input, "bool", null);
        result.Should().Be(expected);
    }

    [Fact]
    public void ApplyFormat_Bool_IntZero_ReturnsFalse()
    {
        var result = MappingEngine.ApplyFormat(0, "boolean", null);
        result.Should().Be(false);
    }

    [Fact]
    public void ApplyFormat_Bool_IntNonZero_ReturnsTrue()
    {
        var result = MappingEngine.ApplyFormat(7, "boolean", null);
        result.Should().Be(true);
    }

    // ── String + Format Pattern ─────────────────────────────────────────

    [Theory]
    [InlineData("hello", "upper", "HELLO")]
    [InlineData("HELLO", "lower", "hello")]
    [InlineData("  spaced  ", "trim", "spaced")]
    public void ApplyFormat_String_PatternTransforms(string input, string pattern, string expected)
    {
        var result = MappingEngine.ApplyFormat(input, "string", pattern);
        result.Should().Be(expected);
    }

    [Fact]
    public void ApplyFormat_String_UnknownPattern_ReturnsAsIs()
    {
        var result = MappingEngine.ApplyFormat("Hello", "text", "weird-pattern");
        result.Should().Be("Hello");
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Fact]
    public void ApplyFormat_NullValue_ReturnsNull()
    {
        var result = MappingEngine.ApplyFormat(null, "string", null);
        result.Should().BeNull();
    }

    [Fact]
    public void ApplyFormat_UnknownType_PassesThroughAsString()
    {
        var result = MappingEngine.ApplyFormat(42, "weird-type", null);
        result.Should().Be("42");
    }
}
