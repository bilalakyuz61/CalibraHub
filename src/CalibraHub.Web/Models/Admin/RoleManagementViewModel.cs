using Microsoft.AspNetCore.Mvc.Rendering;
using CalibraHub.Web.Models.Shared;

namespace CalibraHub.Web.Models.Admin;

public sealed class RoleManagementViewModel
{
    public required IReadOnlyCollection<RoleDefinitionViewModel> Roles { get; init; }
    public required IReadOnlyCollection<SelectListItem> CompanyOptions { get; init; }
    public required IReadOnlyCollection<RoleUserViewModel> Users { get; init; }
    public required GridListStateViewModel RolesListState { get; init; }
    public required GridListStateViewModel UsersListState { get; init; }
    public int? CompanyId { get; init; }
}

public sealed class RoleDefinitionViewModel
{
    public required string Name { get; init; }
    public required IReadOnlyCollection<string> Permissions { get; init; }
}

public sealed class RoleUserViewModel
{
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
}
