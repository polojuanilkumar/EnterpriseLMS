using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Api.Controllers;
[ApiController,OrganizationScope,Authorize(Roles="Student"),Route("api/Gradebook/courses")]
public sealed class GradebookController(ReportingHandlers handler):ControllerBase
{
    [HttpGet] public async Task<IActionResult> Courses([FromQuery]ReportFilter filter,CancellationToken token)=>Ok(ApiResponse<ReportPage<GradeCourseRow>>.Ok(await handler.GradeCourses(filter,token)));
    [HttpGet("{courseId:guid}")] public async Task<IActionResult> Course(Guid courseId,CancellationToken token)=>Ok(ApiResponse<GradeCourseRow>.Ok(await handler.GradeCourse(courseId,token)));
    [HttpGet("{courseId:guid}/quizzes")] public async Task<IActionResult> Quizzes(Guid courseId,[FromQuery]AssessmentFilter filter,CancellationToken token)=>Ok(ApiResponse<ReportPage<QuizGradeRow>>.Ok(await handler.Quizzes(courseId,filter,token)));
    [HttpGet("{courseId:guid}/assignments")] public async Task<IActionResult> Assignments(Guid courseId,[FromQuery]AssessmentFilter filter,CancellationToken token)=>Ok(ApiResponse<ReportPage<AssignmentGradeRow>>.Ok(await handler.Assignments(courseId,filter,token)));
}
