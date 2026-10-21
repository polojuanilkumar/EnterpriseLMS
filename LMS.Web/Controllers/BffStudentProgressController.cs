using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[ApiController, Authorize, Route("bff/student/courses/{courseId:guid}"), RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BffStudentProgressController(LearnerApi api, LearnerSessions sessions) : ControllerBase
{
    private LearnerSession Current() => sessions.Require(User);
    [HttpGet("progress")]
    public Task<BffStudentProgress> Course(Guid courseId, CancellationToken token)
        => api.StudentProgress(Current(), courseId, null, null, token);
    [HttpGet("sections/{sectionId:guid}/lessons/{lessonId:guid}/progress")]
    public Task<BffStudentProgress> Lesson(Guid courseId, Guid sectionId, Guid lessonId, CancellationToken token)
        => api.StudentProgress(Current(), courseId, sectionId, lessonId, token);
    [HttpPost("sections/{sectionId:guid}/lessons/{lessonId:guid}/progress/start")]
    public Task<IActionResult> Start(Guid courseId, Guid sectionId, Guid lessonId, [FromBody] BffProgressAction request, CancellationToken token)
        => Change(() => api.StudentChangeLesson(Current(), courseId, sectionId, lessonId, "start", 0, token));
    [HttpPatch("sections/{sectionId:guid}/lessons/{lessonId:guid}/progress")]
    public Task<IActionResult> Update(Guid courseId, Guid sectionId, Guid lessonId, [FromBody] BffProgressUpdate request, CancellationToken token)
        => Change(() => api.StudentChangeLesson(Current(), courseId, sectionId, lessonId, "update", request.ProgressPercentage!.Value, token));
    [HttpPost("sections/{sectionId:guid}/lessons/{lessonId:guid}/progress/complete")]
    public Task<IActionResult> Complete(Guid courseId, Guid sectionId, Guid lessonId, [FromBody] BffProgressAction request, CancellationToken token)
        => Change(() => api.StudentChangeLesson(Current(), courseId, sectionId, lessonId, "complete", 0, token));
    [HttpPost("enrollments/{enrollmentId:guid}/complete")]
    public Task<IActionResult> CompleteEnrollment(Guid courseId, Guid enrollmentId, [FromBody] BffCompleteEnrollment request, CancellationToken token)
        => Change(() => api.StudentCompleteEnrollment(Current(), courseId, enrollmentId, request.Confirmed, token));
    private async Task<IActionResult> Change(Func<Task> action)
    {
        try { await action(); return NoContent(); }
        catch (AuthoringValidationException ex) { return StatusCode(ex.Status, BffJson.Error(ex.Status)); }
    }
}
