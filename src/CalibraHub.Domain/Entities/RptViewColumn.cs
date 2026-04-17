using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class RptViewColumn
{
    public int Id { get; set; }
    public int ViewId { get; set; }
    public required string ColName { get; set; }
    public required string DisplayName { get; set; }
    public ReportDataType DataType { get; set; }
    public bool IsFilterable { get; set; } = true;
    public bool IsGroupable { get; set; }
    public bool IsAggregatable { get; set; }
    public ReportAggregate? DefaultAggregate { get; set; }
    public int Ordinal { get; set; }
    public ReportContextBinding ContextBinding { get; set; } = ReportContextBinding.None;
}
