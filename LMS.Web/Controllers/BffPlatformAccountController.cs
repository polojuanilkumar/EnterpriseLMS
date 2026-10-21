using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace LMS.Web.Controllers;

[ApiController,Authorize,AutoValidateAntiforgeryToken,Route("bff/platform"),RequestSizeLimit(16384)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class BffPlatformController(LearnerApi api,LearnerSessions sessions):ControllerBase
{
    [HttpGet("organizations")]
    public Task<PlatformOrganizationsPage> Organizations([FromQuery]OrganizationFilterForm filter,CancellationToken ct)=>api.PlatformOrganizations(sessions.Require(User),filter,ct);
    [HttpPost("organizations"),EnableRateLimiting("organization-administration")]
    public async Task<IActionResult> Create(OrganizationCreateForm form,CancellationToken ct)
    {
        try {await api.CreateOrganization(sessions.Require(User),form,ct);return NoContent();}
        catch(WebApiException ex) when(ex.Status is 400 or 409)
        {return StatusCode(ex.Status,new BffError(ex.Status==409?"conflict":"validation_failed",ex.SafeMessage??"Organization code is in use or initial Admin assignment is unavailable."));}
    }
    [HttpGet("audit")]
    public async Task<BffPlatformAuditPage> Audit([FromQuery]AuditViewFilter filter,CancellationToken ct)
    {
        var p=await api.AuditPage(sessions.Require(User),null,filter,ct);p.Filter.Validate();
        if(!p.Filter.FromUtc.HasValue||!p.Filter.ToUtc.HasValue)throw new WebApiException(502);
        return new(p.Page,p.PageSize,p.Total,p.Filter,p.Items.Select(PlatformAudit).ToArray(),["account.password-reset"],AuditDisplay.Outcomes);
    }
    [HttpGet("audit/{id:guid}")]
    public async Task<BffPlatformAuditRow> Detail(Guid id,CancellationToken ct)=>PlatformAudit(await api.AuditDetail(sessions.Require(User),null,id,ct));
    public static BffPlatformAuditRow PlatformAudit(AuditViewRow row)
    {
        // Current durable platform/account history contains only password-reset observations.
        // Organization creation belongs to organization history, even when performed by SuperAdmin.
        if(row.OrganizationId is not null||row.Scope!="account"||row.Action!="account.password-reset"||row.ResourceType!="account"
            ||row.Id==Guid.Empty||row.ResourceId==Guid.Empty||row.ActorUserId==Guid.Empty||row.TimestampUtc.Offset!=TimeSpan.Zero
            ||!Guid.TryParseExact(row.CorrelationId,"N",out _)||row.Outcome!="succeeded")throw new WebApiException(502);
        using var metadata=JsonDocument.Parse(row.Metadata);
        if(metadata.RootElement.ValueKind!=JsonValueKind.Object||metadata.RootElement.EnumerateObject().Any())throw new WebApiException(502);
        return new(row.Id,row.TimestampUtc,row.ActorUserId,row.Scope,row.Action,row.ResourceType,row.ResourceId,row.Outcome,row.CorrelationId,new Dictionary<string,string>());
    }
}

[ApiController,AllowAnonymous,AutoValidateAntiforgeryToken,Route("bff/account"),EnableRateLimiting("login"),RequestSizeLimit(16384)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class BffAccountController(LearnerApi api,LearnerSessions sessions,AccountLinks links):ControllerBase
{
    [HttpPost("forgot-password")]
    public async Task<IActionResult> Forgot(BffForgot form,CancellationToken ct)
    {
        try {await api.AccountRequest<JsonElement>("api/account/forgot-password",new {form.Email},null,null,ct);}
        catch(Exception ex) when(ex is WebApiException or HttpRequestException or JsonException || ex is OperationCanceledException&&!ct.IsCancellationRequested){}
        return Ok(new {message="If the account is eligible, password recovery instructions will be sent."});
    }
    [HttpGet("reset-password")]
    public object ResetStatus(){links.Require(HttpContext,"reset");return new {available=true};}
    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(BffReset form,CancellationToken ct)
    {
        var p=links.Require(HttpContext,"reset");
        // Remove before dispatch: an uncertain outcome must never be replayed with this private link session.
        links.Remove(HttpContext,"reset");
        try {await api.AccountRequest<JsonElement>("api/account/reset-password",new {p.Email,p.Token,form.Password},null,null,ct);return NoContent();}
        finally {form.Password="";}
    }
    [HttpGet("invitation")]
    public async Task<WebInvitationPreview> Preview(CancellationToken ct)
    {
        var p=links.Require(HttpContext,"invite");
        var preview=await api.AccountRequest<WebInvitationPreview>("api/account/invitations/preview",new {p.Token},null,null,ct)??throw new WebApiException(502);
        if(preview.Role is <1 or >3)throw new WebApiException(502);return preview;
    }
    [HttpPost("invitation")]
    public async Task<IActionResult> Accept(BffAccept form,CancellationToken ct)
    {
        if(!form.Confirmed)throw new WebApiException(400);
        var p=links.Require(HttpContext,"invite");
        var session=User.Identity?.IsAuthenticated==true?sessions.Require(User):null;
        links.Remove(HttpContext,"invite");
        try {await api.AccountRequest<JsonElement>("api/account/invitations/accept",new {p.Token,form.FirstName,form.LastName,form.Password},session,null,ct);return NoContent();}
        finally {form.Password="";}
    }
}
public sealed class BffForgot {[Required,EmailAddress,StringLength(256)]public string Email{get;set;}="";}
public sealed class BffReset {[Required,StringLength(1024)]public string Password{get;set;}="";}
public sealed class BffAccept
{
    [StringLength(100)]public string FirstName{get;set;}="";
    [StringLength(100)]public string LastName{get;set;}="";
    [StringLength(1024)]public string Password{get;set;}="";
    public bool Confirmed{get;set;}
}

// The token-bearing entry never boots Angular or loads third-party resources.
[AllowAnonymous,Route("account/angular"),EnableRateLimiting("login")]
public sealed class AngularAccountEntryController(AccountLinks links):ControllerBase
{
    [HttpGet("reset-password")]
    public IActionResult Reset(string? token,string? email)=>Capture("reset",token??"",email??"","reset-password");
    [HttpGet("invitation")]
    public IActionResult Invitation(string? token)=>Capture("invite",token??"","","invitation");
    private IActionResult Capture(string purpose,string token,string email,string page)
    {
        Response.Headers.CacheControl="no-store";Response.Headers["Referrer-Policy"]="no-referrer";
        Response.Headers["Content-Security-Policy"]="default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        if(string.IsNullOrWhiteSpace(token)||email.Length>256)throw new WebApiException(400);
        links.Store(HttpContext,purpose,token,email);
        return Redirect("/app/account/"+page);
    }
}

public sealed record BffPlatformAuditRow(Guid Id,DateTimeOffset TimestampUtc,Guid? ActorUserId,string Scope,string Action,string ResourceType,Guid ResourceId,string Outcome,string CorrelationId,IReadOnlyDictionary<string,string> Metadata);
public sealed record BffPlatformAuditPage(int Page,int PageSize,int Total,AuditViewFilter Filter,BffPlatformAuditRow[] Items,string[] Actions,string[] Outcomes);
