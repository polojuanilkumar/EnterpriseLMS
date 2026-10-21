using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;
[Authorize]
public sealed class AuditController(LearnerApi api,LearnerSessions sessions) : Controller
{
    [HttpGet("admin/{organizationId:guid}/audit")]
    public async Task<IActionResult> Organization(Guid organizationId,[FromQuery] AuditViewFilter filter,CancellationToken token)
    {
        if(!ModelState.IsValid)throw new WebApiException(400);
        var page=await api.AuditPage(sessions.Require(User,organizationId),organizationId,filter,token);
        ViewBag.BasePath=$"/admin/{organizationId}/audit";return View("Index",page);
    }
    [HttpGet("admin/{organizationId:guid}/audit/{eventId:guid}")]
    public async Task<IActionResult> OrganizationDetail(Guid organizationId,Guid eventId,CancellationToken token)
    {
        ViewBag.BasePath=$"/admin/{organizationId}/audit";
        return View("Detail",await api.AuditDetail(sessions.Require(User,organizationId),organizationId,eventId,token));
    }
    [HttpGet("platform/audit")]
    public async Task<IActionResult> Platform([FromQuery] AuditViewFilter filter,CancellationToken token)
    {
        if(!ModelState.IsValid)throw new WebApiException(400);
        ViewBag.BasePath="/platform/audit";return View("Index",await api.AuditPage(sessions.Require(User),null,filter,token));
    }
    [HttpGet("platform/audit/{eventId:guid}")]
    public async Task<IActionResult> PlatformDetail(Guid eventId,CancellationToken token)
    {ViewBag.BasePath="/platform/audit";return View("Detail",await api.AuditDetail(sessions.Require(User),null,eventId,token));}
}
