using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;
[Authorize,Route("staff/{organizationId:guid}/reports/courses")]
public sealed class ReportsController(LearnerApi api,LearnerSessions sessions):Controller
{
    [HttpGet] public async Task<IActionResult> Index(Guid organizationId,[FromQuery]ReportFilterView filter,CancellationToken token)
    {var s=sessions.Require(User,organizationId);if(!ModelState.IsValid){await api.ReportingAccess(s,organizationId,true,token);return BadRequest("Invalid report filters.");}return View(await api.StaffReports(s,organizationId,filter,token));}
    [HttpGet("{courseId:guid}/learners")] public async Task<IActionResult> Learners(Guid organizationId,Guid courseId,[FromQuery]ReportFilterView filter,CancellationToken token)
    {var s=sessions.Require(User,organizationId);if(!ModelState.IsValid){await api.ReportingAccess(s,organizationId,true,token);return BadRequest("Invalid report filters.");}return View(await api.StaffReportLearners(s,organizationId,courseId,filter,token));}
}
