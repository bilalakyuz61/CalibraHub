using System.Text.Json.Nodes;
using CalibraHub.Application.Services.Integration;

namespace CalibraHub.Application.UnitTests.Integration;

/// <summary>
/// MappingEngine.SetJsonPath — nested JSON path olusturma ("FatUst.CariKod") +
/// array indexing ("Kalemler[].StokKod" -> JSON object zinciri). Bu metot
/// entegrasyon mapping engine'in cekirdegi; her kural bu metotla hedefe yazar.
/// </summary>
public sealed class MappingEngineSetJsonPathTests
{
    [Fact]
    public void SetJsonPath_TopLevel_SetsKeyOnRoot()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "CariKod", "MUS001");
        ((string?)root["CariKod"]).Should().Be("MUS001");
    }

    [Fact]
    public void SetJsonPath_TwoLevels_CreatesNestedObject()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "FatUst.CariKod", "MUS001");

        root["FatUst"].Should().NotBeNull();
        root["FatUst"].Should().BeOfType<JsonObject>();
        ((string?)root["FatUst"]!["CariKod"]).Should().Be("MUS001");
    }

    [Fact]
    public void SetJsonPath_ThreeLevels_CreatesDeeplyNested()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "FatUst.Detay.VergiNo", "1234567890");
        ((string?)root["FatUst"]!["Detay"]!["VergiNo"]).Should().Be("1234567890");
    }

    [Fact]
    public void SetJsonPath_TwoCallsSameParent_BothKeysCoexist()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "FatUst.CariKod", "MUS001");
        MappingEngine.SetJsonPath(root, "FatUst.Tarih", "2026-05-17");

        ((string?)root["FatUst"]!["CariKod"]).Should().Be("MUS001");
        ((string?)root["FatUst"]!["Tarih"]).Should().Be("2026-05-17");
    }

    [Fact]
    public void SetJsonPath_OverwritesExistingValue()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "X", "first");
        MappingEngine.SetJsonPath(root, "X", "second");
        ((string?)root["X"]).Should().Be("second");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetJsonPath_EmptyPath_IsNoOp(string? path)
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, path!, "value");
        root.Count.Should().Be(0);
    }

    [Fact]
    public void SetJsonPath_NumericValue_PreservesNumericType()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "Adet", 42);
        ((int?)root["Adet"]).Should().Be(42);
    }

    [Fact]
    public void SetJsonPath_DecimalValue_PreservesDecimal()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "BirimFiyat", 99.95m);
        ((decimal?)root["BirimFiyat"]).Should().Be(99.95m);
    }

    [Fact]
    public void SetJsonPath_BoolValue_PreservesBool()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "Aktif", true);
        ((bool?)root["Aktif"]).Should().Be(true);
    }

    [Fact]
    public void SetJsonPath_NullValue_SetsExplicitNull()
    {
        var root = new JsonObject();
        MappingEngine.SetJsonPath(root, "FatUst.Iskonto", null);
        // ContainsKey true ama value null olmali (FormatPattern uygulanmamis null degeri)
        root["FatUst"].Should().NotBeNull();
        root["FatUst"]!.AsObject().ContainsKey("Iskonto").Should().BeTrue();
        root["FatUst"]!["Iskonto"].Should().BeNull();
    }
}
