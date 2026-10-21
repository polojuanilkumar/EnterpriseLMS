using System.Security.Claims;
using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Organizations;
using Microsoft.AspNetCore.Http;

namespace LMS.Api.Middleware;

public sealed class OrganizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, OrganizationHandlers handlers,
        OrganizationContext organization, IOrganizationRepository repository,
        LMS.Application.Features.Auditing.AuditRequestContext? audit = null)
    {
        if(audit is not null)http.Response.Headers["X-Correlation-Id"]=audit.CorrelationId;
        var scoped = http.GetEndpoint()?.Metadata.GetMetadata<OrganizationScopeAttribute>() is not null;
        if (http.User.Identity?.IsAuthenticated != true)
        {
            if (scoped) { http.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
            await next(http); return;
        }
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId == Guid.Empty)
        { http.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }

        bool platform;
        if (scoped)
        {
            var values = http.Request.Headers["X-Organization-Id"];
            if (values.Count != 1 || !Guid.TryParse(values[0], out var id) || id == Guid.Empty)
                throw new ArgumentException("A single X-Organization-Id header is required.");
            await handlers.SelectAsync(id, userId, http.RequestAborted);
            platform = organization.IsSuperAdmin;
        }
        else platform = await repository.IsSuperAdminAsync(userId, http.RequestAborted);
        audit?.Authenticate(userId); // JWT stamp validation and stored scope checks precede this assignment.

        // Never accept global JWT organization roles. Replace them before authorization runs.
        var identity = new ClaimsIdentity(http.User.Claims.Where(c =>
            c.Type != ClaimTypes.Role && !http.User.Identities.Any(i => c.Type == i.RoleClaimType)),
            http.User.Identity.AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
        if (scoped && organization.Role.HasValue)
            identity.AddClaim(new Claim(ClaimTypes.Role, organization.Role.Value.ToString()));
        if (platform) identity.AddClaim(new Claim(ClaimTypes.Role, "SuperAdmin"));
        http.User = new ClaimsPrincipal(identity);
        await next(http);
    }
}
