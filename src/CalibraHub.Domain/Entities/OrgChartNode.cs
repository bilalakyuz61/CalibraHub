using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class OrgChartNode : EntityInt
{
    public int ChartId { get; init; }

    // User türü için UserId (nullable — Department/Personnel/Vacant için boş)
    public int? UserId { get; init; }

    // Heterojen parent referansı (node.Id üzerinden — tüm türler için geçerli)
    public int? ParentNodeId { get; set; }

    // Geriye dönük uyumluluk — User türünde ParentNodeId ile eşdeğer,
    // repository migration sonrası ParentNodeId'yi kullanır.
    public int? ParentUserId { get; init; }

    public string? PositionTitle { get; init; }
    public int SortOrder { get; init; }

    // v2 alanları
    public OrgChartNodeType NodeType { get; init; } = OrgChartNodeType.User;
    public int? DepartmentId { get; init; }
    public int? PersonnelId { get; init; }
}
