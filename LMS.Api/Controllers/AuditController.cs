using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Api.Controllers;
[ApiController,Authorize,Route("api/audit"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AuditController(IAuditQueryRepository repository) : ControllerBase
{
    [HttpGet("organizations/{organizationId:guid}"),OrganizationScope,Authorize(Roles="Admin,SuperAdmin")]
    public async Task<IActionResult> Organization(Guid organizationId,[FromQuery] AuditFilter filter,CancellationToken token)
        =>Ok(ApiResponse<AuditPage>.Ok(await repository.Organization(organizationId,filter,token)));
    [HttpGet("organizations/{organizationId:guid}/{eventId:guid}"),OrganizationScope,Authorize(Roles="Admin,SuperAdmin")]
    public async Task<IActionResult> OrganizationDetail(Guid organizationId,Guid eventId,CancellationToken token)
        =>Ok(ApiResponse<AuditRow>.Ok(await repository.OrganizationDetail(organizationId,eventId,token)??throw new KeyNotFoundException("Audit event not found.")));
    [HttpGet("platform"),Authorize(Roles="SuperAdmin")]
    public async Task<IActionResult> Platform([FromQuery] AuditFilter filter,CancellationToken token)
        =>Ok(ApiResponse<AuditPage>.Ok(await repository.Platform(filter,token)));
    [HttpGet("platform/{eventId:guid}"),Authorize(Roles="SuperAdmin")]
    public async Task<IActionResult> PlatformDetail(Guid eventId,CancellationToken token)
        =>Ok(ApiResponse<AuditRow>.Ok(await repository.PlatformDetail(eventId,token)??throw new KeyNotFoundException("Audit event not found.")));
}
