using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[ApiController, Authorize, Route("bff/student/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/assignments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BffStudentAssignmentsController(LearnerApi api, LearnerSessions sessions) : ControllerBase
{
    [HttpGet, HttpGet("{assignmentId:guid}"), HttpGet("{assignmentId:guid}/submissions/{submissionId:guid}")]
    public async Task<object> Read(Guid courseId, Guid sectionId, Guid lessonId, Guid? assignmentId, Guid? submissionId, CancellationToken token)
        => LearnerApi.StudentAssignmentProjection(await api.StudentAssignment(sessions.Require(User), courseId, sectionId, lessonId, assignmentId, submissionId, token));
    [HttpPost("{assignmentId:guid}/submit"), Consumes("multipart/form-data")]
    [RequestSizeLimit(28311552), RequestFormLimits(MultipartBodyLengthLimit = 28311552)]
    public async Task<IActionResult> Submit(Guid courseId, Guid sectionId, Guid lessonId, Guid assignmentId, [FromForm] BffAssignmentUpload form, CancellationToken token)
    {
        var fields = await Request.ReadFormAsync(token);
        if (fields.Keys.Any(x => !new[] { "Text", "Confirmed", "__RequestVerificationToken" }.Contains(x, StringComparer.OrdinalIgnoreCase))
            || fields.Files.Count > 1 || fields.Files.Any(x => !x.Name.Equals("File", StringComparison.OrdinalIgnoreCase))) throw new WebApiException(400);
        var s = sessions.Require(User);
        var page = await api.StudentAssignment(s, courseId, sectionId, lessonId, assignmentId, null, token);
        try { await api.StudentSubmitAssignment(s, page, form, token); return NoContent(); }
        catch (AuthoringValidationException ex) { return StatusCode(ex.Status, BffJson.Error(ex.Status)); }
    }
    [HttpGet("{assignmentId:guid}/submissions/{submissionId:guid}/attachment")]
    [HttpHead("{assignmentId:guid}/submissions/{submissionId:guid}/attachment")]
    public async Task Attachment(Guid courseId, Guid sectionId, Guid lessonId, Guid assignmentId, Guid submissionId, CancellationToken token)
    {
        var s = sessions.Require(User);
        var page = await api.StudentAssignment(s, courseId, sectionId, lessonId, assignmentId, submissionId, token);
        using var upstream = await api.AssignmentDownload(s, page, token, HttpMethods.IsHead(Request.Method));
        Response.ContentType = "application/pdf";
        Response.Headers.ContentDisposition = $"attachment; filename=\"assignment-{submissionId:N}.pdf\"";
        Response.Headers.CacheControl = "private, no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        if (upstream.Content.Headers.ContentLength is {} length) Response.ContentLength = length;
        if (!HttpMethods.IsHead(Request.Method)) await upstream.Content.CopyToAsync(Response.Body, token);
    }
}

[ApiController, Authorize, Route("bff/student"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BffStudentGradesController(LearnerApi api, LearnerSessions sessions) : ControllerBase
{
    private LearnerSession Current() { var s = sessions.Require(User); if (!s.OrganizationId.HasValue) throw new WebApiException(403); return s; }
    [HttpGet("assignments/history")]
    public async Task<AssignmentHistoryPage> History(CancellationToken token)
    { var s = Current(); return await api.AssignmentHistory(s, s.OrganizationId!.Value, token); }
    [HttpGet("grades")]
    public async Task<object> Grades([FromQuery] ReportFilterView filter, CancellationToken token)
    {
        var s = Current(); var page = await api.MyGrades(s, s.OrganizationId!.Value, filter, token);
        return new { page.OrganizationId, Courses = new { page.Courses.Page, page.Courses.PageSize, page.Courses.Total, Items = page.Courses.Items.Select(LearnerApi.StudentGradeCourse) } };
    }
    [HttpGet("grades/courses/{courseId:guid}")]
    public async Task<object> Course(Guid courseId, int quizPage = 1, int assignmentPage = 1, int pageSize = 20, int? quizStatus = null, int? assignmentStatus = null, CancellationToken token = default)
    {
        var s = Current(); var page = await api.CourseGrades(s, s.OrganizationId!.Value, courseId, quizPage, assignmentPage, pageSize, quizStatus, assignmentStatus, token);
        return new { page.OrganizationId, Course = LearnerApi.StudentGradeCourse(page.Course), Quizzes = LearnerApi.StudentGradePage(page.Quizzes with { Items = page.Quizzes.Items.Select(LearnerApi.StudentQuizGrade).ToArray() }), Assignments = LearnerApi.StudentGradePage(page.Assignments with { Items = page.Assignments.Items.Select(LearnerApi.StudentAssignmentGrade).ToArray() }) };
    }
}
