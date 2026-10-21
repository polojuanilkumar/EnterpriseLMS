using LMS.Domain.Enums;
namespace LMS.Application.Features.Auditing;
public static class AuditAccess
{
    public static void Organization(Guid? selected,Guid target,Guid? actor,bool active,bool storedPlatform,OrganizationRole? storedRole)
    {
        if(selected!=target||target==Guid.Empty||actor is null||actor==Guid.Empty||!active||!storedPlatform&&storedRole!=OrganizationRole.Admin)
            throw new UnauthorizedAccessException("Select an organization with current stored Admin or SuperAdmin authorization to view its audit history.");
    }
    public static void Platform(Guid? actor,bool storedPlatform)
    {if(actor is null||actor==Guid.Empty||!storedPlatform)throw new UnauthorizedAccessException("Stored SuperAdmin authorization is required to view platform/account audit history.");}
}
