using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LMS.Web.Services;

public static class LearnerSignIn
{
    public static async Task IssueAsync(HttpContext http, LearnerSessions sessions, LearnerSession session)
    {
        var id = sessions.Create(session);
        sessions.Remove(http.User.FindFirstValue(LearnerSessions.Claim));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(LearnerSessions.Claim, id), new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString())
        }, CookieAuthenticationDefaults.AuthenticationScheme));
        try
        {
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { ExpiresUtc = session.ExpiresAt, IsPersistent = false, AllowRefresh = false });
            http.User = principal;
        }
        catch { sessions.Remove(id); throw; }
    }
}
