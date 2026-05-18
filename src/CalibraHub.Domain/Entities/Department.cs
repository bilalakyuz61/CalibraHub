using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Sirket ici departman tanimlari. CompanyId FK, ParentDepartmentId ile hiyerarsi (alt departman). Kullanicilar bu tabloya baglanir.")]
public sealed class Department
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string Name { get; set; }
    public int? ParentDepartmentId { get; set; }
    public bool IsActive { get; private set; } = true;

    public void Deactivate() => IsActive = false;
    public void Activate()   => IsActive = true;

    public void Update(string name, int? parentDepartmentId)
    {
        Name = name;
        ParentDepartmentId = parentDepartmentId;
    }
}
