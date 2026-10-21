using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.Courses.AssignCourseInstructor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Api.Controllers;

[ApiController, OrganizationScope, Authorize(Roles = "Admin,SuperAdmin")]
public sealed class CourseInstructorsController(AssignCourseInstructorHandler handler) : ControllerBase
{
    [HttpPut("api/Courses/{courseId:guid}/instructor")]
    public async Task<IActionResult> Assign(Guid courseId, AssignInstructorRequest request, CancellationToken token)
    {
        await handler.HandleAsync(courseId, request.InstructorId, token);
        return Ok(ApiResponse<object>.Ok(new { courseId, request.InstructorId }, "Instructor assigned."));
    }
}
public sealed record AssignInstructorRequest(Guid InstructorId);
