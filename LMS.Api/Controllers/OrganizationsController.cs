using System.Security.Claims;
using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LMS.Api.Controllers;

[ApiController, Route("api/organizations"), Authorize]
public sealed class OrganizationsController(OrganizationHandlers handlers) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<IActionResult> Mine(CancellationToken token)
        => Ok(ApiResponse<IReadOnlyList<Organization>>.Ok(await handlers.ListAsync(UserId, token)));

    // OrganizationMiddleware obtains membership and SuperAdmin from stored authorization.
    [HttpGet("{organizationId:guid}/context"), OrganizationScope]
    public IActionResult Current(Guid organizationId)
        => Ok(ApiResponse<SelectedOrganizationResponse>.Ok(handlers.Current(organizationId)));

    [HttpGet("platform/context"), Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> PlatformContext(CancellationToken token)
        => Ok(ApiResponse<PlatformAccessResponse>.Ok(await handlers.PlatformAccessAsync(UserId, token)));

    [HttpGet, Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> PlatformList([FromQuery] OrganizationListFilter filter, CancellationToken token)
        => Ok(ApiResponse<OrganizationListPage<PlatformOrganizationRow>>.Ok(await handlers.PlatformPageAsync(UserId, filter, token)));

    [HttpPost, Authorize(Roles = "SuperAdmin"), EnableRateLimiting("organization-administration")]
    public async Task<IActionResult> Create(CreateOrganizationRequest request, CancellationToken token)
    {
        var organization = await handlers.CreateByEmailAsync(UserId, request.Code, request.Name, request.InitialAdminEmail, request.Confirmed, token);
        return StatusCode(201, ApiResponse<Organization>.Ok(organization, "Organization created."));
    }

    [HttpGet("{organizationId:guid}/members"), OrganizationScope, Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Members(Guid organizationId, [FromQuery] OrganizationListFilter filter, CancellationToken token)
        => Ok(ApiResponse<OrganizationListPage<OrganizationMemberRow>>.Ok(await handlers.MembersPageAsync(organizationId, filter, token)));

    [HttpPost("{organizationId:guid}/members"), OrganizationScope, Authorize(Roles = "Admin,SuperAdmin"), EnableRateLimiting("organization-administration")]
    public async Task<IActionResult> AddMember(Guid organizationId, AddMembershipRequest request, CancellationToken token)
    {
        await handlers.AddMemberByEmailAsync(organizationId, request.Email, request.Role, request.Confirmed, token);
        return StatusCode(201, ApiResponse<object>.Ok(new { organizationId }, "Membership created."));
    }

    [HttpGet("{organizationId:guid}/members/{userId:guid}"), OrganizationScope, Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Member(Guid organizationId, Guid userId, CancellationToken token)
        => Ok(ApiResponse<OrganizationMemberRow>.Ok(await handlers.MemberAsync(organizationId,userId,token)));

    [HttpPut("{organizationId:guid}/members/{userId:guid}"), OrganizationScope, Authorize(Roles = "Admin,SuperAdmin"), EnableRateLimiting("organization-administration")]
    public async Task<IActionResult> SetMember(Guid organizationId, Guid userId, SetMembershipRequest request, CancellationToken token)
    {
        if (!request.Confirmed) throw new ArgumentException("Confirm the membership role change.");
        await handlers.SetMemberAsync(organizationId, userId, request.Role, token);
        return Ok(ApiResponse<object>.Ok(new { organizationId, userId, request.Role }, "Membership assigned."));
    }
}

public sealed record CreateOrganizationRequest(string Code, string Name, string InitialAdminEmail, bool Confirmed = false);
public sealed record SetMembershipRequest(OrganizationRole Role, bool Confirmed = false);
public sealed record AddMembershipRequest(string Email, OrganizationRole Role, bool Confirmed = false);
