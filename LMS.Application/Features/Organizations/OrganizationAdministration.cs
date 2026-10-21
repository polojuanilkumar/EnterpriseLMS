using System.Net.Mail;
using LMS.Domain.Enums;

namespace LMS.Application.Features.Organizations;

public sealed record OrganizationListFilter(int Page = 1, int PageSize = 20, string? Search = null, OrganizationRole? Role = null)
{
    public int Skip => (Page - 1) * PageSize;
    public void Validate(bool members)
    {
        if (Page is < 1 or > 10000 || PageSize is < 1 or > 50 || Search?.Length > 100
            || Role.HasValue && (!members || !Enum.IsDefined(Role.Value)))
            throw new ArgumentException("Use page 1–10000, page size 1–50, search up to 100 characters, and a valid member role.");
    }
}
public sealed record OrganizationListPage<T>(Guid? OrganizationId, int Page, int PageSize, int Total, T[] Items)
{
    public int Pages => (int)Math.Ceiling((double)Total / PageSize);
}
public sealed record OrganizationMemberRow(Guid OrganizationId, Guid UserId, string FirstName, string LastName, string Email, OrganizationRole Role)
{
    public OrganizationMemberRow() : this(Guid.Empty,Guid.Empty,"","","",default) { }
}
public sealed record PlatformOrganizationRow(Guid Id, string Code, string Name, bool IsActive)
{
    public PlatformOrganizationRow() : this(Guid.Empty,"","",false) { }
}
public sealed record PlatformAccessResponse(Guid UserId, bool IsSuperAdmin);

public static class MembershipRules
{
    public const string Unavailable = "Account unavailable or membership conflicts with current state.";
    public static string NormalizeEmail(string email)
    {
        var value = email?.Trim() ?? "";
        if (value.Length is < 3 or > 256 || !MailAddress.TryCreate(value, out var address) || address.Address != value)
            throw new ArgumentException("Enter an exact email address, without a display name.");
        return value.Normalize().ToUpperInvariant();
    }
    public static void Check(OrganizationRole oldRole, OrganizationRole newRole, int adminCount, bool assignedCourses, bool activeEnrollments)
    {
        if (!Enum.IsDefined(newRole)) throw new ArgumentException("Invalid organization role.");
        if (oldRole == newRole) return;
        if (oldRole == OrganizationRole.Admin && adminCount <= 1)
            throw new InvalidOperationException("The last organization Admin cannot be demoted. Assign another Admin first.");
        if (oldRole == OrganizationRole.Instructor && assignedCourses)
            throw new InvalidOperationException("Reassign all courses before changing this Instructor's role.");
        if (oldRole == OrganizationRole.Student && activeEnrollments)
            throw new InvalidOperationException("Resolve all Active enrollments before changing this Student's role.");
    }
}
