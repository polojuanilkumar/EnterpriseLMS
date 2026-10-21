using System.Security.Claims;
using LMS.Api.Middleware;
using LMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LMS.Api.Controllers;

[ApiController, Route("api/account"), RequestSizeLimit(16384), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AccountRecoveryController(IAccountOnboarding accounts) : ControllerBase
{
    [HttpGet("antiforgery"), AllowAnonymous]
    public IActionResult Antiforgery([FromServices] Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery)
    {
        Response.Headers.CacheControl="no-store";
        var tokens=antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new {Token=tokens.RequestToken});
    }
    [HttpGet("session"), Authorize]
    public IActionResult Session() => Ok();
    [HttpPost("forgot-password"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("account-recovery")]
    public async Task<IActionResult> Forgot(ForgotPasswordRequest request,CancellationToken ct)
    {
        try { await accounts.Forgot(request.Email,ct); }
        catch(AccountDeliveryException) { /* Same response; monitoring must not identify the account. */ }
        return Ok(new { Message=AccountOnboarding.ForgotResponse });
    }
    [HttpPost("reset-password"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("account-recovery")]
    public async Task<IActionResult> Reset(ResetPasswordRequest request,CancellationToken ct)
        => await accounts.Reset(request.Email,request.Token,request.Password,ct) ? Ok(new {Message="Password reset. Sign in again."})
            : BadRequest(new {Message="Reset unavailable or password invalid. Request another recovery email."});
    [HttpPost("invitations/preview"), AllowAnonymous, EnableRateLimiting("account-recovery")]
    public async Task<IActionResult> Preview(InvitationToken request,CancellationToken ct)
        => Ok(await accounts.Preview(request.Token,ct));
    [HttpPost("invitations/accept"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("account-recovery")]
    public async Task<IActionResult> Accept(AcceptInvitation request,CancellationToken ct)
    {
        var actor=Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id) ? id : (Guid?)null;
        await accounts.Accept(request,actor,ct);return Ok(new {Message="Invitation accepted. Sign in to select your organization."});
    }
    [HttpGet("organizations/{organizationId:guid}/invitations"), OrganizationScope, Authorize(Roles="Admin,SuperAdmin")]
    public async Task<IActionResult> List(Guid organizationId,int page=1,int pageSize=20,CancellationToken ct=default)
        => Ok(await accounts.List(organizationId,page,pageSize,ct));
    [HttpPost("organizations/{organizationId:guid}/invitations"), OrganizationScope, Authorize(Roles="Admin,SuperAdmin"), ValidateAntiForgeryToken, EnableRateLimiting("organization-administration")]
    public async Task<IActionResult> Create(Guid organizationId,CreateInvitationRequest request,CancellationToken ct)
    {
        try {return Ok(await accounts.Create(organizationId,request.Email,request.Role,request.Reissue,request.Confirmed,ct));}
        catch(AccountDeliveryException){return StatusCode(503,new {Message="Delivery was not confirmed. Inspect the invitation list before explicitly reissuing."});}
    }
    [HttpPost("organizations/{organizationId:guid}/invitations/{id:guid}/revoke"), OrganizationScope, Authorize(Roles="Admin,SuperAdmin"), ValidateAntiForgeryToken, EnableRateLimiting("organization-administration")]
    public async Task<IActionResult> Revoke(Guid organizationId,Guid id,ConfirmInvitation request,CancellationToken ct)
    {await accounts.Revoke(organizationId,id,request.Confirmed,ct);return Ok();}
}
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email,string Token,string Password);
public sealed record InvitationToken(string Token);
public sealed record ConfirmInvitation(bool Confirmed);
public sealed record CreateInvitationRequest(string Email,LMS.Domain.Enums.OrganizationRole Role,bool Reissue=false,bool Confirmed=false);
