using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.UnitTests.Domain;

/// <summary>
/// WorkOrder davranis testleri (rapor §2.4 Domain davranisi yayma).
/// Status state machine: Planned → Released → InProgress → Completed → Closed
/// </summary>
public sealed class WorkOrderBehaviorTests
{
    private static WorkOrder NewWorkOrder(WorkOrderStatus status = WorkOrderStatus.Planned, decimal planned = 100m) =>
        new()
        {
            OrderNumber = "WO-001",
            OrderDate = new DateTime(2026, 5, 18),
            ItemId = 1, CompanyId = 1,
            PlannedQuantity = planned,
            Status = status,
        };

    [Fact]
    public void Release_FromPlanned_TransitionsToReleased()
    {
        var wo = NewWorkOrder();
        wo.Release();
        wo.Status.Should().Be(WorkOrderStatus.Released);
    }

    [Fact]
    public void Release_FromInProgress_Throws()
    {
        var wo = NewWorkOrder(WorkOrderStatus.InProgress);
        Action act = () => wo.Release();
        act.Should().Throw<DomainException>().WithMessage("*Planned*");
    }

    [Fact]
    public void StartProduction_FromReleased_TransitionsToInProgress_AndSetsActualStart()
    {
        var wo = NewWorkOrder(WorkOrderStatus.Released);
        wo.StartProduction();
        wo.Status.Should().Be(WorkOrderStatus.InProgress);
        wo.ActualStartDate.Should().NotBeNull();
    }

    [Fact]
    public void RegisterProduction_AccumulatesQuantityAndScrap()
    {
        var wo = NewWorkOrder(WorkOrderStatus.InProgress, planned: 100m);
        wo.RegisterProduction(quantity: 30m, scrap: 2m);
        wo.RegisterProduction(quantity: 25m, scrap: 1m);

        wo.ProducedQuantity.Should().Be(55m);
        wo.ScrapQuantity.Should().Be(3m);
    }

    [Fact]
    public void RegisterProduction_FromReleased_AutoTransitionsToInProgress()
    {
        var wo = NewWorkOrder(WorkOrderStatus.Released);
        wo.RegisterProduction(quantity: 10m);
        wo.Status.Should().Be(WorkOrderStatus.InProgress);
        wo.ActualStartDate.Should().NotBeNull();
    }

    [Fact]
    public void RegisterProduction_NegativeQuantity_Throws()
    {
        var wo = NewWorkOrder(WorkOrderStatus.InProgress);
        Action act = () => wo.RegisterProduction(-5m);
        act.Should().Throw<DomainException>().WithMessage("*negatif*");
    }

    [Fact]
    public void MarkAsCompleted_WhenProducedReachesPlanned_TransitionsToCompleted()
    {
        var wo = NewWorkOrder(WorkOrderStatus.InProgress, planned: 50m);
        wo.RegisterProduction(50m);
        wo.MarkAsCompleted();
        wo.Status.Should().Be(WorkOrderStatus.Completed);
        wo.ActualEndDate.Should().NotBeNull();
    }

    [Fact]
    public void MarkAsCompleted_WhenProducedBelowPlanned_Throws()
    {
        var wo = NewWorkOrder(WorkOrderStatus.InProgress, planned: 100m);
        wo.RegisterProduction(30m);
        Action act = () => wo.MarkAsCompleted();
        act.Should().Throw<DomainException>().WithMessage("*altinda*");
    }

    [Fact]
    public void Close_FromCompleted_TransitionsToClosed()
    {
        var wo = NewWorkOrder(WorkOrderStatus.InProgress, planned: 10m);
        wo.RegisterProduction(10m);
        wo.MarkAsCompleted();
        wo.Close();
        wo.Status.Should().Be(WorkOrderStatus.Closed);
        wo.IsFinalized().Should().BeTrue();
    }

    [Fact]
    public void Close_FromInProgress_Throws()
    {
        var wo = NewWorkOrder(WorkOrderStatus.InProgress);
        Action act = () => wo.Close();
        act.Should().Throw<DomainException>().WithMessage("*Completed*");
    }

    [Fact]
    public void Cancel_FromPlanned_TransitionsToCancelled()
    {
        var wo = NewWorkOrder(WorkOrderStatus.Planned);
        wo.Cancel();
        wo.Status.Should().Be(WorkOrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FromClosed_Throws()
    {
        var wo = NewWorkOrder(WorkOrderStatus.Closed);
        Action act = () => wo.Cancel();
        act.Should().Throw<DomainException>().WithMessage("*Closed*");
    }

    [Fact]
    public void IsInProduction_TrueForReleasedAndInProgress()
    {
        NewWorkOrder(WorkOrderStatus.Released).IsInProduction().Should().BeTrue();
        NewWorkOrder(WorkOrderStatus.InProgress).IsInProduction().Should().BeTrue();
        NewWorkOrder(WorkOrderStatus.Planned).IsInProduction().Should().BeFalse();
        NewWorkOrder(WorkOrderStatus.Closed).IsInProduction().Should().BeFalse();
    }
}
