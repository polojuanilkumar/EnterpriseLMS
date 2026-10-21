using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[ApiController, Authorize, Route("bff/student/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/quiz"), RequestSizeLimit(131072)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BffStudentQuizController(LearnerApi api, LearnerSessions sessions) : ControllerBase
{
    [HttpGet, HttpGet("attempts")]
    public async Task<BffStudentQuiz> Instructions(Guid courseId, Guid sectionId, Guid lessonId, CancellationToken token)
        => LearnerApi.StudentQuizProjection(await api.StudentQuiz(sessions.Require(User), courseId, sectionId, lessonId, null, token));
    [HttpGet("attempts/{attemptId:guid}")]
    public async Task<BffStudentQuiz> Attempt(Guid courseId, Guid sectionId, Guid lessonId, Guid attemptId, CancellationToken token)
        => LearnerApi.StudentQuizProjection(await api.StudentQuiz(sessions.Require(User), courseId, sectionId, lessonId, attemptId, token));
    [HttpGet("attempts/{attemptId:guid}/questions")]
    public async Task<object> Questions(Guid courseId, Guid sectionId, Guid lessonId, Guid attemptId, CancellationToken token)
    {
        var page = await Attempt(courseId, sectionId, lessonId, attemptId, token);
        if (page.Attempt?.Status != 1) throw new WebApiException(409);
        return new { page.OrganizationId, page.CourseId, page.SectionId, page.LessonId, AttemptId = attemptId, page.Questions };
    }
    [HttpGet("attempts/{attemptId:guid}/result")]
    public async Task<object> Result(Guid courseId, Guid sectionId, Guid lessonId, Guid attemptId, CancellationToken token)
    {
        var page = await Attempt(courseId, sectionId, lessonId, attemptId, token);
        if (page.Attempt?.Status != 2 || page.Result is null) throw new WebApiException(409);
        return new { page.OrganizationId, page.CourseId, page.SectionId, page.LessonId, page.Result };
    }
    [HttpGet("attempts/{attemptId:guid}/review")]
    public async Task<object> Review(Guid courseId, Guid sectionId, Guid lessonId, Guid attemptId, CancellationToken token)
    {
        var page = await Attempt(courseId, sectionId, lessonId, attemptId, token);
        if (page.Attempt?.Status != 2 || page.Review is null) throw new WebApiException(409);
        return new { page.OrganizationId, page.CourseId, page.SectionId, page.LessonId, page.Review };
    }
    [HttpPost("start")]
    public async Task<object> Start(Guid courseId, Guid sectionId, Guid lessonId, [FromBody] BffQuizStart request, CancellationToken token)
        => new { AttemptId = await api.StudentQuizStart(sessions.Require(User), courseId, sectionId, lessonId, token) };
    [HttpPost("attempts/{attemptId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid courseId, Guid sectionId, Guid lessonId, Guid attemptId, [FromBody] BffQuizSubmit request, CancellationToken token)
    {
        try { await api.StudentQuizSubmit(sessions.Require(User), courseId, sectionId, lessonId, attemptId, request, token); return NoContent(); }
        catch (AuthoringValidationException ex) { return StatusCode(ex.Status, BffJson.Error(ex.Status)); }
    }
}
