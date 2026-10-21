using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LMS.Web.Controllers;

[Authorize, AutoValidateAntiforgeryToken, Route("platform")]
public sealed class PlatformController(LearnerApi api, LearnerSessions sessions) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        await api.PlatformAccess(sessions.Require(User),token);
        return View();
    }
    [HttpGet("organizations")]
    public async Task<IActionResult> Organizations([FromQuery] OrganizationFilterForm filter,CancellationToken token)
    {
        if(!ModelState.IsValid)throw new WebApiException(400);
        return View(await api.PlatformOrganizations(sessions.Require(User),filter,token));
    }
    [HttpGet("organizations/create")]
    public async Task<IActionResult> Create(CancellationToken token)
    {
        await api.PlatformAccess(sessions.Require(User),token);
        return View(new OrganizationCreateForm());
    }
    [HttpPost("organizations/create"),EnableRateLimiting("organization-administration")]
    public async Task<IActionResult> Create(OrganizationCreateForm form,CancellationToken token)
    {
        var session=sessions.Require(User);await api.PlatformAccess(session,token);
        if(!form.Confirmed)ModelState.AddModelError("Confirmed","Confirm creation and initial Admin assignment.");
        if(ModelState.IsValid)
        {
            try
            {
                await api.CreateOrganization(session,form,token);
                TempData["OrganizationNotice"]="Organization created with its initial Admin. No organization was automatically selected.";
                return Redirect("/platform/organizations");
            }
            catch(WebApiException ex) when(ex.Status is 400 or 409 or 429)
            {ModelState.AddModelError("",ex.SafeMessage ?? (ex.Status==429?"Too many requests. Wait before trying again.":"Organization code is already in use or account assignment is unavailable."));Response.StatusCode=ex.Status;}
            catch(Exception ex) when(ex is HttpRequestException or System.Text.Json.JsonException || ex is WebApiException {Status:502} || ex is OperationCanceledException && !token.IsCancellationRequested)
            {
                TempData["OrganizationNotice"]="Creation outcome is unknown. Search the stored organization code before making another request. Creation was not retried.";
                return Redirect("/platform/organizations");
            }
        }
        else Response.StatusCode=400;
        form.Confirmed=false;ModelState.Remove(nameof(form.Confirmed));
        return View(form);
    }
}
