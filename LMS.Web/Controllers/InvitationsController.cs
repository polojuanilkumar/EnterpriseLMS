using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
namespace LMS.Web.Controllers;
[Authorize,Route("organizations/{organizationId:guid}/invitations"),EnableRateLimiting("organization-administration")]
public sealed class InvitationsController(LearnerApi api,LearnerSessions sessions) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid organizationId,int page=1,CancellationToken ct=default)
    {
        var session=sessions.Require(User,organizationId);
        var data=await api.AccountRequest<WebInvitationPage>($"api/account/organizations/{organizationId}/invitations?page={Math.Max(1,page)}",null,session,organizationId,ct,true);
        ViewBag.OrganizationId=organizationId;return View(data);
    }
    [HttpGet("create")]
    public async Task<IActionResult> Create(Guid organizationId,CancellationToken ct)
    {
        await Admin(organizationId,ct);
        ViewBag.OrganizationId=organizationId;return View(new InviteForm());
    }
    [HttpPost("create")]
    public async Task<IActionResult> Create(Guid organizationId,InviteForm form,CancellationToken ct)
    {
        ViewBag.OrganizationId=organizationId;
        if(!ModelState.IsValid)return View(form);
        try {await api.AccountRequest<JsonElement>($"api/account/organizations/{organizationId}/invitations",form,sessions.Require(User,organizationId),organizationId,ct);}
        catch(Exception ex) when(ex is WebApiException or HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested){TempData["InvitationMessage"]="Outcome uncertain or invitation denied. Inspect this list before explicitly reissuing; delivery may have failed.";}
        return RedirectToAction(nameof(Index),new {organizationId});
    }
    [HttpGet("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid organizationId,Guid id,CancellationToken ct){await Admin(organizationId,ct);ViewBag.OrganizationId=organizationId;ViewBag.Id=id;return View();}
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid organizationId,Guid id,bool confirmed,CancellationToken ct)
    {
        if(!confirmed)return BadRequest("Confirm revocation.");
        try {await api.AccountRequest<JsonElement>($"api/account/organizations/{organizationId}/invitations/{id}/revoke",new {confirmed},sessions.Require(User,organizationId),organizationId,ct);}
        catch(Exception ex) when(ex is WebApiException or HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested){TempData["InvitationMessage"]="Revocation outcome uncertain. Inspect the list before taking further action.";}
        return RedirectToAction(nameof(Index),new {organizationId});
    }
    private async Task Admin(Guid organizationId,CancellationToken ct)
    {
        var access=await api.Access(sessions.Require(User,organizationId),organizationId,ct);
        if(!access.IsSuperAdmin && access.Role!="Admin")throw new WebApiException(403);
    }
}

