using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;
[Authorize,Route("learn/{organizationId:guid}/grades")]
public sealed class GradebookController(LearnerApi api,LearnerSessions sessions):Controller
{
    [HttpGet] public async Task<IActionResult> Index(Guid organizationId,[FromQuery]ReportFilterView filter,CancellationToken token)
    {var s=sessions.RequireLearner(User,organizationId);if(!ModelState.IsValid){await api.ReportingAccess(s,organizationId,false,token);return BadRequest("Invalid gradebook filters.");}return View(await api.MyGrades(s,organizationId,filter,token));}
    [HttpGet("courses/{courseId:guid}")] public async Task<IActionResult> Course(Guid organizationId,Guid courseId,int quizPage=1,int assignmentPage=1,int pageSize=20,int? quizStatus=null,int? assignmentStatus=null,CancellationToken token=default)
    {var s=sessions.RequireLearner(User,organizationId);if(!ModelState.IsValid){await api.ReportingAccess(s,organizationId,false,token);return BadRequest("Invalid gradebook filters.");}return View(await api.CourseGrades(s,organizationId,courseId,quizPage,assignmentPage,pageSize,quizStatus,assignmentStatus,token));}
}
