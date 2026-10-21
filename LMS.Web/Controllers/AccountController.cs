using System.Security.Claims;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace LMS.Web.Controllers;

[Authorize, Route("account")]
public sealed class AccountController(LearnerApi api,LearnerSessions sessions) : Controller
{
    [AllowAnonymous,HttpGet("login")]
    public IActionResult Login()=>View(new LoginForm());
    [AllowAnonymous,HttpPost("login"),EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginForm form,CancellationToken token)
    {
        if(!ModelState.IsValid) {form.Password="";ModelState.Remove(nameof(form.Password));return View(form);}
        ApiLogin login;
        try {login=await api.LoginAsync(form,token);}
        catch(WebApiException ex) when(ex.Status==401)
        {
            form.Password="";ModelState.Remove(nameof(form.Password));ModelState.AddModelError("","Invalid email or password.");
            Response.StatusCode=401;return View(form);
        }
        await Issue(new LearnerSession {AccessToken=login.AccessToken,UserId=login.UserId,ExpiresAt=login.ExpiresAt});
        return RedirectToAction(nameof(Organizations)); // Never accept an untrusted return URL.
    }
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        sessions.Remove(User.FindFirstValue(LearnerSessions.Claim));
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
    [HttpGet("organizations")]
    public async Task<IActionResult> Organizations(CancellationToken token)
        => View((await api.Organizations(sessions.Require(User),token)).Where(x=>x.IsActive).ToArray());
    [HttpPost("organizations")]
    public async Task<IActionResult> SelectOrganization(Guid organizationId,CancellationToken token)
    {
        var current=sessions.Require(User);
        var access=await api.ValidateSelection(current,organizationId,token);
        await Issue(new LearnerSession {AccessToken=current.AccessToken,UserId=current.UserId,ExpiresAt=current.ExpiresAt,OrganizationId=organizationId,OrganizationRole=access.Role,IsSuperAdmin=access.IsSuperAdmin});
        return Redirect(access.Home);
    }
    [AllowAnonymous,HttpGet("access-denied")]
    public ContentResult AccessDenied() => new() { StatusCode=403, ContentType="text/html; charset=utf-8", Content=AccessDeniedPage.Html };
    [HttpGet("home")]
    public async Task<IActionResult> Home(CancellationToken token)
    {
        var session=sessions.Require(User);
        if(!session.OrganizationId.HasValue)
        {
            try {await api.PlatformAccess(session,token);return Redirect("/platform");}
            catch(WebApiException ex) when(ex.Status==403){return RedirectToAction(nameof(Organizations));}
        }
        var access=await api.Access(session,session.OrganizationId.Value,token);
        if(session.OrganizationRole!=access.Role || session.IsSuperAdmin!=access.IsSuperAdmin)
            await Issue(new LearnerSession {AccessToken=session.AccessToken,UserId=session.UserId,ExpiresAt=session.ExpiresAt,
                OrganizationId=access.OrganizationId,OrganizationRole=access.Role,IsSuperAdmin=access.IsSuperAdmin});
        return Redirect(access.Home);
    }
    private Task Issue(LearnerSession session)
        => LearnerSignIn.IssueAsync(HttpContext, sessions, session);
}
