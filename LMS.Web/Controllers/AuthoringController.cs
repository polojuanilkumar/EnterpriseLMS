using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[Authorize,AutoValidateAntiforgeryToken,Route("staff/{organizationId:guid}/courses")]
public sealed class AuthoringController(LearnerApi api,LearnerSessions sessions):Controller
{
    private LearnerSession Session(Guid org)=>sessions.Require(User,org);
    [HttpGet("new"),HttpGet("{courseId:guid}/edit")]
    public async Task<IActionResult> Course(Guid organizationId,Guid? courseId,CancellationToken token,int instructorPage=1,string? instructorSearch=null)
        =>View(await api.CourseEditor(Session(organizationId),organizationId,courseId,token,new(){Page=instructorPage,PageSize=50,Search=instructorSearch}));
    [HttpPost("new"),HttpPost("{courseId:guid}/edit")]
    public async Task<IActionResult> SaveCourse(Guid organizationId,Guid? courseId,[Bind(Prefix="Form")] CourseForm form,CancellationToken token,int instructorPage=1,string? instructorSearch=null)
    {
        var page=await api.CourseEditor(Session(organizationId),organizationId,courseId,token,new(){Page=instructorPage,PageSize=50,Search=instructorSearch});
        if(ModelState.IsValid)
        {
            try {await api.SaveCourse(Session(organizationId),organizationId,courseId,form,token);return Redirect($"/staff/{organizationId}/courses"+(courseId.HasValue?$"/{courseId}":""));}
            catch(AuthoringValidationException ex) {ModelState.AddModelError("",ex.Message);}
        }
        Response.StatusCode=400;return View("Course",page with {Form=form});
    }
    [HttpGet("{courseId:guid}/sections/new"),HttpGet("{courseId:guid}/sections/{sectionId:guid}/edit")]
    public async Task<IActionResult> Section(Guid organizationId,Guid courseId,Guid? sectionId,CancellationToken token)
        =>View(await api.SectionEditor(Session(organizationId),organizationId,courseId,sectionId,token));
    [HttpPost("{courseId:guid}/sections/new"),HttpPost("{courseId:guid}/sections/{sectionId:guid}/edit")]
    public async Task<IActionResult> SaveSection(Guid organizationId,Guid courseId,Guid? sectionId,[Bind(Prefix="Form")] SectionForm form,CancellationToken token)
    {
        var page=await api.SectionEditor(Session(organizationId),organizationId,courseId,sectionId,token);
        if(ModelState.IsValid)
        {
            try {await api.SaveSection(Session(organizationId),organizationId,courseId,sectionId,form,token);return Redirect($"/staff/{organizationId}/courses/{courseId}");}
            catch(AuthoringValidationException ex) {ModelState.AddModelError("",ex.Message);}
        }
        Response.StatusCode=400;return View("Section",page with {Form=form});
    }
    [HttpGet("{courseId:guid}/sections/{sectionId:guid}/lessons/new"),HttpGet("{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/edit")]
    public async Task<IActionResult> Lesson(Guid organizationId,Guid courseId,Guid sectionId,Guid? lessonId,CancellationToken token)
        =>View(await api.LessonEditor(Session(organizationId),organizationId,courseId,sectionId,lessonId,token));
    [HttpPost("{courseId:guid}/sections/{sectionId:guid}/lessons/new"),HttpPost("{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/edit")]
    public async Task<IActionResult> SaveLesson(Guid organizationId,Guid courseId,Guid sectionId,Guid? lessonId,[Bind(Prefix="Form")] LessonForm form,CancellationToken token)
    {
        var page=await api.LessonEditor(Session(organizationId),organizationId,courseId,sectionId,lessonId,token);
        if(ModelState.IsValid)
        {
            try {await api.SaveLesson(Session(organizationId),organizationId,courseId,sectionId,lessonId,form,token);return Redirect($"/staff/{organizationId}/courses/{courseId}/sections/{sectionId}");}
            catch(AuthoringValidationException ex) {ModelState.AddModelError("",ex.Message);}
        }
        Response.StatusCode=400;return View("Lesson",page with {Form=form});
    }
    [HttpGet("{courseId:guid}/readiness")]
    public async Task<IActionResult> Readiness(Guid organizationId,Guid courseId,CancellationToken token)
        =>View(await api.Readiness(Session(organizationId),organizationId,courseId,token));
}
