using CalibraHub.Application.Contracts;
using CalibraHub.Application.Mapping;
using CalibraHub.Domain.Entities;
using Mapster;

namespace CalibraHub.Application.UnitTests.Mapping;

/// <summary>
/// MapsterConfig — entity ↔ DTO mapping kurallari. Rapor §2.4 cozumu icin
/// sozlesme garantisi: yeni alan eklenince auto-map calismaya devam etmeli.
/// </summary>
public sealed class MapsterConfigTests
{
    private static TypeAdapterConfig BuildConfig()
    {
        var c = new TypeAdapterConfig();
        MapsterConfig.Configure(c);
        return c;
    }

    [Fact]
    public void Document_To_DocumentDto_BasicFields_AutoMap()
    {
        var doc = new Document
        {
            Id = 42,
            DocumentNumber = "ORD-2026-001",
            DocumentDate = new DateTime(2026, 5, 17),
            ContactId = 100,
            ContactName = "ACME",
            Currency = "TRY",
            SubTotal = 1000m,
            DiscountRate = 10m,
            DiscountAmount = 100m,
            TaxRate = 20m,
            TaxAmount = 180m,
            GrandTotal = 1080m,
        };

        var config = BuildConfig();
        var dto = doc.Adapt<DocumentDto>(config);

        dto.Id.Should().Be(42);
        dto.DocumentNumber.Should().Be("ORD-2026-001");
        dto.ContactId.Should().Be(100);
        dto.ContactName.Should().Be("ACME");
        dto.Currency.Should().Be("TRY");
        dto.SubTotal.Should().Be(1000m);
        dto.GrandTotal.Should().Be(1080m);
    }

    [Fact]
    public void Document_With_Lines_AdaptedSuccessfully()
    {
        var doc = new Document { DocumentNumber = "X" };
        doc.Lines.Add(new DocumentLine { ItemId = 1, Quantity = 1, UnitPrice = 1, LineTotal = 1 });
        doc.Lines.Add(new DocumentLine { ItemId = 2, Quantity = 1, UnitPrice = 1, LineTotal = 1 });

        var config = BuildConfig();
        var dto = doc.Adapt<DocumentDto>(config);

        dto.Should().NotBeNull();
        dto.DocumentNumber.Should().Be("X");
    }

    [Fact]
    public void DocumentLine_To_DocumentLineDto_AutoMap()
    {
        var line = new DocumentLine
        {
            Id = 1,
            DocumentId = 100,
            LineNo = 5,
            ItemId = 50,
            Quantity = 3m,
            UnitPrice = 250m,
            LineTotal = 750m,
            MaterialCode = "ITEM-001",
            MaterialName = "Test Item",
        };

        var config = BuildConfig();
        var dto = line.Adapt<DocumentLineDto>(config);

        dto.Id.Should().Be(1);
        dto.DocumentId.Should().Be(100);
        dto.LineNo.Should().Be(5);
        dto.ItemId.Should().Be(50);
        dto.Quantity.Should().Be(3m);
        dto.UnitPrice.Should().Be(250m);
        dto.MaterialCode.Should().Be("ITEM-001");
    }

    [Fact]
    public void Collection_AdaptToDtoList_PreservesOrder()
    {
        var docs = new[]
        {
            new Document { Id = 1, DocumentNumber = "A" },
            new Document { Id = 2, DocumentNumber = "B" },
            new Document { Id = 3, DocumentNumber = "C" },
        };

        var config = BuildConfig();
        var dtos = docs.Adapt<List<DocumentDto>>(config);

        dtos.Should().HaveCount(3);
        dtos[0].Id.Should().Be(1);
        dtos[2].Id.Should().Be(3);
    }

    [Fact]
    public void Contact_To_ContactDto_AutoMap_AccountTypeCast()
    {
        var contact = new Contact
        {
            Id = 7,
            CompanyId = 1,
            AccountType = CalibraHub.Domain.Enums.ContactType.Customer,
            AccountCode = "MUS-001",
            AccountTitle = "ACME Ltd",
            TaxNumber = "1234567890",
            Phone = "555-1234",
            Email = "info@acme.com",
            IsActive = true,
            CreatedAt = new DateTime(2026, 5, 17),
        };

        var config = BuildConfig();
        var dto = contact.Adapt<ContactDto>(config);

        dto.Id.Should().Be(7);
        dto.AccountCode.Should().Be("MUS-001");
        dto.AccountTitle.Should().Be("ACME Ltd");
        dto.AccountType.Should().Be((byte)CalibraHub.Domain.Enums.ContactType.Customer);
        dto.TaxNumber.Should().Be("1234567890");
        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Item_To_ItemDto_AutoMap()
    {
        var item = new CalibraHub.Domain.Entities.Item
        {
            Id = 42,
            CompanyId = 1,
            Code = "MAT-001",
            Name = "Test Malzeme",
            TypeId = 5,
            UnitId = 1,
            Combinations = true,
            TaxRate = 18m,
            CreateDate = new DateTime(2026, 5, 1),
        };

        var config = BuildConfig();
        var dto = item.Adapt<ItemDto>(config);

        dto.Id.Should().Be(42);
        dto.Code.Should().Be("MAT-001");
        dto.Name.Should().Be("Test Malzeme");
        dto.TypeId.Should().Be(5);
        dto.UnitId.Should().Be(1);
        dto.Combinations.Should().BeTrue();
        dto.TaxRate.Should().Be(18m);
        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Company_To_CompanyDto_AutoMap()
    {
        var company = new Company
        {
            Id = 1,
            Name = "ACME Holding",
            Title = "ACME Holding A.S.",
            Address = "Buyukdere Cad. No: 1",
            TaxOffice = "Maslak",
            TaxNumber = "1234567890",
            IsEDocumentApprovalEnabled = true,
        };

        var config = BuildConfig();
        var dto = company.Adapt<CompanyDto>(config);

        dto.Id.Should().Be(1);
        dto.Name.Should().Be("ACME Holding");
        dto.Title.Should().Be("ACME Holding A.S.");
        dto.IsEDocumentApprovalEnabled.Should().BeTrue();
        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Department_To_DepartmentDto_AutoMap()
    {
        var dept = new Department
        {
            Id = 3,
            CompanyId = 1,
            Name = "Satis Departmani",
        };

        var config = BuildConfig();
        var dto = dept.Adapt<DepartmentDto>(config);

        dto.Id.Should().Be(3);
        dto.CompanyId.Should().Be(1);
        dto.Name.Should().Be("Satis Departmani");
        dto.IsActive.Should().BeTrue();
    }
}
