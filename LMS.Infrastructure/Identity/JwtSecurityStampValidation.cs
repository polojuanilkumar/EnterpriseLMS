using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
namespace LMS.Infrastructure.Identity;

// Shared with isolated integration composition; signature/issuer/lifetime validation precedes this check.
public static class JwtSecurityStampValidation
{
    public static async Task<bool> ValidAsync(ClaimsPrincipal? principal, UserManager<ApplicationUser> users)
    {
        var id = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var stamp = principal?.FindFirst("lms-stamp")?.Value;
        var user = id is null ? null : await users.FindByIdAsync(id);
        return user is not null && InvitationRules.StampMatches(stamp, await users.GetSecurityStampAsync(user));
    }
}
