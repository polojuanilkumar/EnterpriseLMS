using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[ApiController, Authorize, Route("bff/student"), RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BffStudentController(LearnerApi api, LearnerSessions sessions) : ControllerBase
{
    private LearnerSession Current()
    {
        var session = sessions.Require(User);
        if (session.OrganizationId is null) throw new WebApiException(403);
        // Cached roles are not authority. Each operation below verifies stored Student access.
        return session;
    }
    [HttpGet("courses")]
    public async Task<ActionResult<BffStudentCourses>> Catalog(CancellationToken token)
    {
        var session = Current();
        return await api.StudentCourses(session, session.OrganizationId!.Value, true, token);
    }
    [HttpGet("my-courses")]
    public async Task<ActionResult<BffStudentCourses>> MyCourses(CancellationToken token)
    {
        var session = Current();
        return await api.StudentCourses(session, session.OrganizationId!.Value, false, token);
    }
    [HttpPost("courses/{courseId:guid}/enroll")]
    public Task<IActionResult> Enroll(Guid courseId, [FromBody] BffEnrollRequest request, CancellationToken token)
        => Change(session => api.Enroll(session, session.OrganizationId!.Value, courseId, request.ConfirmedReenrollment, token));
    [HttpPost("courses/{courseId:guid}/enrollments/{enrollmentId:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid courseId, Guid enrollmentId, [FromBody] BffCancelRequest request, CancellationToken token)
        => Change(session => api.ChangeEnrollment(session, session.OrganizationId!.Value, courseId, enrollmentId, "cancel", request.Confirmed, token));
    private async Task<IActionResult> Change(Func<LearnerSession, Task> action)
    {
        try { await action(Current()); return NoContent(); }
        catch (AuthoringValidationException ex) { return StatusCode(ex.Status, BffJson.Error(ex.Status)); }
    }
}
