using LMS.Domain.Enums;

namespace LMS.Application.Features.Organizations;

// Scoped per request. Initialized only after stored membership/platform privilege is checked.
public sealed class OrganizationContext
{
    public Guid? OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole? Role { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    public bool CanManageContent => IsSuperAdmin || Role is OrganizationRole.Admin or OrganizationRole.Instructor;
    public void Initialize(Guid organizationId, Guid userId, OrganizationRole? role, bool isSuperAdmin)
    {
        if (OrganizationId.HasValue) throw new InvalidOperationException("Organization cannot change within a request.");
        if (organizationId == Guid.Empty || userId == Guid.Empty || (!isSuperAdmin && !role.HasValue)
            || (role.HasValue && !Enum.IsDefined(role.Value)))
            throw new UnauthorizedAccessException("Verified organization membership is required.");
        OrganizationId = organizationId; UserId = userId; Role = role; IsSuperAdmin = isSuperAdmin;
    }
}
