using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.UnitTests.Domain;

public sealed class IntegrationBehaviorTests
{
    private static CalibraHub.Domain.Entities.Integration BuildBase() => new()
    {
        Name = "Netsis Order Push",
        SourceFormCode = "SALES_ORDER_NEW",
        TargetEndpointId = 5,
        ErrorBehavior = IntegrationErrorBehavior.Skip,
        RetryCount = 0,
    };

    [Fact]
    public void EnsureValid_HappyPath_DoesNotThrow()
    {
        var i = BuildBase();
        Action act = i.EnsureValid;
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValid_BlankName_Throws()
    {
        var i = BuildBase();
        i.Name = " ";
        Action act = i.EnsureValid;
        act.Should().Throw<DomainException>().WithMessage("*adi*");
    }

    [Fact]
    public void EnsureValid_NoEndpointAndNoProcedure_Throws()
    {
        var i = BuildBase();
        i.TargetEndpointId = null;
        Action act = i.EnsureValid;
        act.Should().Throw<DomainException>().WithMessage("*endpoint*");
    }

    [Fact]
    public void EnsureValid_NoEndpointButHasPreProcedure_DoesNotThrow()
    {
        var i = BuildBase();
        i.TargetEndpointId = null;
        i.PreProcedureName = "dbo.SnapshotOrder";
        Action act = i.EnsureValid;
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValid_RetryBehaviorWithZeroCount_Throws()
    {
        var i = BuildBase();
        i.ErrorBehavior = IntegrationErrorBehavior.Retry;
        i.RetryCount = 0;
        Action act = i.EnsureValid;
        act.Should().Throw<DomainException>().WithMessage("*RetryCount*");
    }

    [Fact]
    public void Activate_WhenValid_SetsActiveAndUpdatesTimestamp()
    {
        var i = BuildBase();
        i.IsActive = false;
        i.Activate();
        i.IsActive.Should().BeTrue();
        i.Updated.Should().NotBeNull();
    }

    [Fact]
    public void Activate_WhenInvalid_Throws()
    {
        var i = BuildBase();
        i.Name = "";
        i.IsActive = false;
        Action act = i.Activate;
        act.Should().Throw<DomainException>();
        i.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_SetsInactiveAndUpdatesTimestamp()
    {
        var i = BuildBase();
        i.IsActive = true;
        i.Deactivate();
        i.IsActive.Should().BeFalse();
        i.Updated.Should().NotBeNull();
    }

    [Fact]
    public void BumpVersion_IncrementsVersionNo()
    {
        var i = BuildBase();
        i.VersionNo = 3;
        i.BumpVersion();
        i.VersionNo.Should().Be(4);
        i.Updated.Should().NotBeNull();
    }

    [Fact]
    public void IsProcedureOnlyMode_TrueWhenNoEndpoint()
    {
        var i = BuildBase();
        i.TargetEndpointId = null;
        i.IsProcedureOnlyMode().Should().BeTrue();

        i.TargetEndpointId = 5;
        i.IsProcedureOnlyMode().Should().BeFalse();
    }
}
