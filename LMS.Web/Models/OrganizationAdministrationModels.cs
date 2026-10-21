using System.ComponentModel.DataAnnotations;

namespace LMS.Web.Models;

public sealed class OrganizationFilterForm
{
    [Range(1,10000)] public int Page { get; set; } = 1;
    [Range(1,50)] public int PageSize { get; set; } = 20;
    [StringLength(100)] public string? Search { get; set; }
    [Range(1,3)] public int? Role { get; set; }
}
public sealed record AdministrationPage<T>(Guid? OrganizationId, int Page, int PageSize, int Total, T[] Items);
public sealed record PlatformAccessView(Guid UserId, bool IsSuperAdmin);
public sealed record PlatformOrganizationsPage(AdministrationPage<OrganizationView> Data, OrganizationFilterForm Filter, string? Notice = null);
public sealed class OrganizationCreateForm
{
    [Required, StringLength(50)] public string Code { get; set; } = "";
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string InitialAdminEmail { get; set; } = "";
    public bool Confirmed { get; set; }
}
