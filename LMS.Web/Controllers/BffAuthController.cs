using System.Security.Claims;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LMS.Web.Controllers;

[ApiController, Authorize, Route("bff"), RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BffAuthController(LearnerApi api, LearnerSessions sessions) : ControllerBase
{
    [AllowAnonymous, HttpGet("antiforgery")]
    public ActionResult<BffAntiforgery> Antiforgery([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return new BffAntiforgery(tokens.RequestToken!, "X-CSRF-TOKEN");
    }

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    public async Task<ActionResult<BffLogin>> Login([FromBody] LoginForm form, CancellationToken token)
    {
        ApiLogin login;
        try { login = await api.LoginAsync(form, token); }
        catch (WebApiException ex) when (ex.Status == 401)
        {
            return Unauthorized(new BffError("invalid_credentials", "Invalid email or password."));
        }
        await LearnerSignIn.IssueAsync(HttpContext, sessions, new LearnerSession {
            AccessToken = login.AccessToken, UserId = login.UserId, ExpiresAt = login.ExpiresAt
        });
        return new BffLogin(login.UserId, login.ExpiresAt);
    }

    [HttpGet("session")]
    public async Task<ActionResult<BffSession>> Session(CancellationToken token)
    {
        var current = sessions.Require(User);
        OrganizationAccess? access = null;
        OrganizationView? selected = null;
        string? recovery = null;
        if (current.OrganizationId is Guid org)
        {
            try {
                selected = (await api.Organizations(current, token)).SingleOrDefault(x => x.Id == org && x.IsActive);
                if (selected is null) throw new WebApiException(403);
                access = await api.Access(current, org, token);
            }
            catch (WebApiException ex) when (ex.Status == 403) { access = null; recovery = "organization_selection_cleared"; }
        }
        bool platform = access?.IsSuperAdmin ?? false;
        if (access is null)
        {
            try { await api.PlatformAccess(current, token); platform = true; }
            catch (WebApiException ex) when (ex.Status == 403) { platform = false; }
        }
        if (current.OrganizationId != access?.OrganizationId || current.OrganizationRole != access?.Role || current.IsSuperAdmin != platform)
            await LearnerSignIn.IssueAsync(HttpContext, sessions, new LearnerSession {
                AccessToken = current.AccessToken, UserId = current.UserId, ExpiresAt = current.ExpiresAt,
                OrganizationId = access?.OrganizationId, OrganizationRole = access?.Role, IsSuperAdmin = platform
            });
        return new BffSession(current.UserId, current.ExpiresAt,
            access is null ? null : new BffOrganization(access.OrganizationId, access.Role, selected!.Name, selected.Code), platform,
            access is null && !platform, access?.Home ?? (platform ? "/platform" : null), recovery);
    }

    [HttpGet("organizations")]
    public async Task<ActionResult<BffOrganizationOption[]>> Organizations(CancellationToken token)
        => (await api.Organizations(sessions.Require(User), token))
            .Where(x => x.IsActive && x.Id != Guid.Empty)
            .Select(x => new BffOrganizationOption(x.Id, x.Name, x.Code)).ToArray();

    [HttpPost("organization")]
    public async Task<ActionResult<BffSession>> SelectOrganization([FromBody] BffOrganizationSelection selection, CancellationToken token)
    {
        if (selection.OrganizationId == Guid.Empty)
            return BadRequest(new BffError("validation_failed", "Choose a valid organization."));
        var current = sessions.Require(User);
        var access = await api.ValidateSelection(current, selection.OrganizationId, token);
        await LearnerSignIn.IssueAsync(HttpContext, sessions, new LearnerSession {
            AccessToken = current.AccessToken, UserId = current.UserId, ExpiresAt = current.ExpiresAt,
            OrganizationId = access.OrganizationId, OrganizationRole = access.Role, IsSuperAdmin = access.IsSuperAdmin
        });
        // Re-read current access and metadata, including revocation races, through the same recovery path.
        return await Session(token);
    }
    // Anonymous is intentional: an expired/revoked cookie can still be cleared. CSRF remains required.
    [AllowAnonymous, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        sessions.Remove(User.FindFirstValue(LearnerSessions.Claim));
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
