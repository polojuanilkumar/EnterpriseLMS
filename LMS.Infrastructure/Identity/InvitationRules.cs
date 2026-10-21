using LMS.Domain.Enums;
namespace LMS.Infrastructure.Identity;

public static class InvitationRules
{
    public static void Authority(bool activeOrganization,bool platform,bool organizationAdmin)
    {
        if(!activeOrganization || !(platform || organizationAdmin))throw new UnauthorizedAccessException("Invitation authority is no longer available.");
    }
    public static void SelectedOrganization(Guid? selected,Guid target,Guid actor)
    {
        if(selected!=target || actor==Guid.Empty)throw new UnauthorizedAccessException("Select the target organization.");
    }
    public static void ExistingAccount(ApplicationUser user,Guid? actor,string invitedNormalizedEmail,bool platform)
    {
        if(actor!=user.Id || !user.EmailConfirmed || user.NormalizedEmail!=invitedNormalizedEmail)
            throw new UnauthorizedAccessException("Sign in with the matching verified email.");
        if(platform)throw new InvalidOperationException("Use the established platform membership workflow.");
    }
    public static void Membership(OrganizationRole? existing,OrganizationRole invited)
    {
        if(existing.HasValue && existing!=invited)throw new InvalidOperationException("Membership role conflicts. Use the established role-change workflow.");
    }
    public static bool StampMatches(string? tokenStamp,string? storedStamp)
        => !string.IsNullOrEmpty(tokenStamp) && !string.IsNullOrEmpty(storedStamp) && tokenStamp==storedStamp;
}
