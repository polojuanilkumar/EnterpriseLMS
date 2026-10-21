using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[ApiController, Authorize, Route("bff/student/courses/{courseId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BffStudentViewerController(LearnerApi api, LearnerSessions sessions) : ControllerBase
{
    private LearnerSession Current() => sessions.Require(User);
    private static object Course(CourseView c) => new { c.Id, c.Title, c.Description, c.CategoryName, c.Level, c.DurationInMinutes };
    [HttpGet, HttpGet("sections")]
    public async Task<object> Sections(Guid courseId, CancellationToken token)
    {
        var page = await api.StudentSections(Current(), courseId, token);
        return new { page.OrganizationId, Course = Course(page.Course), page.Sections };
    }
    [HttpGet("sections/{sectionId:guid}"), HttpGet("sections/{sectionId:guid}/lessons")]
    public async Task<object> Lessons(Guid courseId, Guid sectionId, CancellationToken token)
    {
        var page = await api.StudentLessons(Current(), courseId, sectionId, token);
        return new { page.OrganizationId, Course = Course(page.Course), page.Section, page.Lessons };
    }
    [HttpGet("sections/{sectionId:guid}/lessons/{lessonId:guid}")]
    [HttpGet("sections/{sectionId:guid}/lessons/{lessonId:guid}/content-items")]
    public async Task<object> Lesson(Guid courseId, Guid sectionId, Guid lessonId, CancellationToken token)
    {
        var unit = await api.StudentUnit(Current(), courseId, sectionId, lessonId, token);
        var items = unit.Content.Items.Select(x => new { x.Id, x.Type, x.Title, x.DisplayOrder, x.IsRequired,
            Text = x.Type == 1 ? x.Text : null,
            YouTubeVideoId = x.Type == 2 && SafeContent.YouTubeEmbed(x.YouTubeVideoId) is not null ? x.YouTubeVideoId : null });
        var legacy = unit.Content.Legacy;
        var embed = SafeContent.LegacyYouTube(legacy?.VideoUrl);
        return new { unit.OrganizationId, unit.CourseId, unit.SectionId, unit.Lesson, Items = items,
            Legacy = legacy is null ? null : new { legacy.Content, YouTubeVideoId = embed is null ? null : embed[(embed.LastIndexOf('/') + 1)..] } };
    }
    [HttpGet("sections/{sectionId:guid}/lessons/{lessonId:guid}/items/{itemId:guid}/media")]
    [HttpHead("sections/{sectionId:guid}/lessons/{lessonId:guid}/items/{itemId:guid}/media")]
    public async Task Media(Guid courseId, Guid sectionId, Guid lessonId, Guid itemId, bool download, CancellationToken token)
    {
        var session = Current();
        var unit = await api.StudentUnit(session, courseId, sectionId, lessonId, token);
        using var upstream = await api.Media(session, unit, itemId, HttpMethods.IsHead(Request.Method), Request.Headers.Range, Request.Headers.IfRange, token);
        await PrivateMediaProxy.WriteAsync(HttpContext, upstream, unit.Content.Items.Single(x => x.Id == itemId).Type, itemId, download, token);
    }
}
