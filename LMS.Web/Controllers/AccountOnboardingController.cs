using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
namespace LMS.Web.Controllers;

[Route("account"),AllowAnonymous,EnableRateLimiting("login"),RequestSizeLimit(16384)]
public sealed class AccountOnboardingController(LearnerApi api,LearnerSessions sessions,AccountLinks links) : Controller
{
    private void PrivatePage()
    {
        Response.Headers.CacheControl="no-store";Response.Headers["Referrer-Policy"]="no-referrer";
        Response.Headers["Content-Security-Policy"]="default-src 'none'; style-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    }
    [HttpGet("forgot-password")]
    public IActionResult Forgot(){PrivatePage();return View("Forgot",new RecoveryForm());}
    [HttpPost("forgot-password")]
    public async Task<IActionResult> Forgot(RecoveryForm form,CancellationToken ct)
    {
        PrivatePage();
        if(!ModelState.IsValid)return View("Forgot",form);
        try {await api.AccountRequest<JsonElement>("api/account/forgot-password",new {form.Email},null,null,ct);}
        catch(Exception ex) when(ex is WebApiException or HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested){ /* Generic response also covers uncertain delivery; no automatic retry. */ }
        return View("Message","If the account is eligible, password recovery instructions will be sent.");
    }
    [HttpGet("reset-password")]
    public IActionResult Reset(string? token,string? email)
    {
        PrivatePage();
        if(token is not null){links.Store(HttpContext,"reset",token,email??"");return Redirect("/account/reset-password");}
        try {var pending=links.Require(HttpContext,"reset");return View("Reset",new RecoveryForm {Email=pending.Email});}
        catch(WebApiException){return View("Message","Recovery link unavailable. Request another recovery email.");}
    }
    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(RecoveryForm form,CancellationToken ct)
    {
        PrivatePage();
        try
        {
            var pending=links.Require(HttpContext,"reset");
            await api.AccountRequest<JsonElement>("api/account/reset-password",new {pending.Email,pending.Token,form.Password},null,null,ct);
            links.Remove(HttpContext,"reset");return View("Message","Password reset. Sign in again.");
        }
        catch(Exception ex) when(ex is WebApiException or HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested){ClearPassword(form);return View("Message","Reset unavailable, password invalid, or outcome uncertain. Try signing in or request a new recovery email. Do not replay the submission.");}
        finally {form.Password="";}
    }
    [HttpGet("invitation")]
    public async Task<IActionResult> Invitation(string? token,CancellationToken ct)
    {
        PrivatePage();
        if(token is not null){links.Store(HttpContext,"invite",token,"");return Redirect("/account/invitation");}
        try
        {
            var p=links.Require(HttpContext,"invite");
            var preview=await api.AccountRequest<WebInvitationPreview>("api/account/invitations/preview",new {p.Token},null,null,ct) ?? throw new WebApiException(502);
            return View("Invitation",new RecoveryForm {Email=preview.Email,Organization=preview.Organization,Role=preview.Role==1?"Student":preview.Role==2?"Instructor":"Admin",ExistingAccount=preview.ExistingAccount});
        }
        catch(Exception ex) when(ex is WebApiException or HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested){return View("Message","Invitation unavailable. Ask your organization administrator to inspect or reissue it.");}
    }
    [HttpPost("invitation")]
    public async Task<IActionResult> Invitation(RecoveryForm form,CancellationToken ct)
    {
        PrivatePage();
        try
        {
            var p=links.Require(HttpContext,"invite");
            var session=User.Identity?.IsAuthenticated==true ? sessions.Require(User) : null;
            await api.AccountRequest<JsonElement>("api/account/invitations/accept",new {p.Token,form.FirstName,form.LastName,form.Password},session,null,ct);
            links.Remove(HttpContext,"invite");return View("Message","Invitation accepted. Sign in and select your organization.");
        }
        catch(WebApiException ex) when(ex.Status==409)
        {
            ClearPassword(form);Response.StatusCode=409;
            return View("Message","Invitation acceptance conflicts with the current account or membership. Ask an administrator to inspect the invitation and use the established role-change workflow.");
        }
        catch(Exception ex) when(ex is WebApiException or HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested){ClearPassword(form);return View("Message","Acceptance denied or outcome uncertain. Existing accounts must sign in with the matching verified email. Check your organization list or ask an administrator to inspect the invitation before submitting again.");}
        finally {form.Password="";}
    }
    private void ClearPassword(RecoveryForm form){form.Password="";ModelState.Remove(nameof(form.Password));}
}

