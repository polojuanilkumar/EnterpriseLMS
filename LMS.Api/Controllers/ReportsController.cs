using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Api.Controllers;
[ApiController,OrganizationScope,Authorize(Roles="Instructor,Admin,SuperAdmin"),Route("api/Reports/courses")]
public sealed class ReportsController(ReportingHandlers handler):ControllerBase
{
    [HttpGet] public async Task<IActionResult> Courses([FromQuery]ReportFilter filter,CancellationToken token)=>Ok(ApiResponse<ReportPage<CourseReportRow>>.Ok(await handler.Courses(filter,token)));
    [HttpGet("{courseId:guid}/learners")] public async Task<IActionResult> Learners(Guid courseId,[FromQuery]ReportFilter filter,CancellationToken token)=>Ok(ApiResponse<CourseLearnerReport>.Ok(await handler.Learners(courseId,filter,token)));
}
