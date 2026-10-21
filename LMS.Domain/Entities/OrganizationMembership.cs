using LMS.Domain.Enums;

namespace LMS.Domain.Entities;

public sealed class OrganizationMembership
{
    private OrganizationMembership() { }
    public OrganizationMembership(Guid organizationId, Guid userId, OrganizationRole role)
    {
        if (organizationId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Organization and user IDs are required.");
        Id = Guid.NewGuid(); OrganizationId = organizationId; UserId = userId;
        ChangeRole(role);
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; }
    public void ChangeRole(OrganizationRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentException("Invalid organization role.");
        Role = role;
    }
}
