using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[Authorize,Route("learn/{organizationId:guid}/courses")]
public sealed class LearnController(LearnerApi api,LearnerSessions sessions) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Courses(Guid organizationId,CancellationToken token)
        => View(await api.MyCourses(sessions.RequireLearner(User,organizationId),organizationId,token));
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(Guid organizationId,CancellationToken token)
        =>View(await api.Catalog(sessions.RequireLearner(User,organizationId),organizationId,token));
    [HttpGet("{courseId:guid}")]
    public async Task<IActionResult> Sections(Guid organizationId,Guid courseId,CancellationToken token)
    {
        var session=sessions.RequireLearner(User,organizationId);
        var page=await api.Sections(session,organizationId,courseId,token);
        return View(page with {Learning=await api.CourseLearning(session,organizationId,courseId,token)});
    }
    [HttpGet("{courseId:guid}/sections/{sectionId:guid}")]
    public async Task<IActionResult> Lessons(Guid organizationId,Guid courseId,Guid sectionId,CancellationToken token)
        => View(await api.Lessons(sessions.RequireLearner(User,organizationId),organizationId,courseId,sectionId,token));
    [HttpGet("{courseId:guid}/sections/{sectionId:guid}/units/{lessonId:guid}")]
    public async Task<IActionResult> Unit(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
    {
        var session=sessions.RequireLearner(User,organizationId);
        var quiz=await api.LearnerQuiz(session,organizationId,courseId,sectionId,lessonId,null,token);
        return View(quiz.Unit with {Quiz=quiz,Progress=await api.LessonState(session,organizationId,courseId,sectionId,lessonId,token)});
    }
    [HttpGet("{courseId:guid}/sections/{sectionId:guid}/units/{lessonId:guid}/items/{itemId:guid}/media")]
    [HttpHead("{courseId:guid}/sections/{sectionId:guid}/units/{lessonId:guid}/items/{itemId:guid}/media")]
    public async Task Media(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid itemId,bool download,CancellationToken token)
    {
        var session=sessions.RequireLearner(User,organizationId);
        var unit=await api.Unit(session,organizationId,courseId,sectionId,lessonId,token);
        using var upstream=await api.Media(session,unit,itemId,HttpMethods.IsHead(Request.Method),Request.Headers.Range,Request.Headers.IfRange,token);
        await PrivateMediaProxy.WriteAsync(HttpContext,upstream,unit.Content.Items.Single(x=>x.Id==itemId).Type,itemId,download,token);
    }
}
