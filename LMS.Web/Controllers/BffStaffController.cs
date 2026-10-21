using System.ComponentModel.DataAnnotations;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[ApiController, Authorize, AutoValidateAntiforgeryToken, Route("bff/staff/courses")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(1048576)]
public sealed class BffStaffController(LearnerApi api, LearnerSessions sessions) : ControllerBase
{
    private const string Section = "{courseId:guid}/sections/{sectionId:guid}";
    private const string Unit = Section + "/lessons/{lessonId:guid}";
    private LearnerSession Current() => sessions.Require(User);
    private static Guid Org(LearnerSession session) => session.OrganizationId ?? throw new WebApiException(403);
    [HttpGet] public Task<StaffCoursesPage> Courses(CancellationToken token) { var s=Current(); return api.StaffCourses(s,Org(s),token); }
    [HttpGet("new"), HttpGet("{courseId:guid}/edit")]
    public Task<CourseEditor> CourseEditor([FromRoute] Guid? courseId, [FromQuery] OrganizationFilterForm filter, CancellationToken token)
    { var s=Current(); return api.CourseEditor(s,Org(s),courseId,token,filter); }
    [HttpPost, HttpPut("{courseId:guid}")]
    public Task<IActionResult> SaveCourse([FromRoute] Guid? courseId, CourseForm form, CancellationToken token)
    { var s=Current(); return Change(()=>api.SaveCourse(s,Org(s),courseId,form,token)); }
    [HttpGet("{courseId:guid}")]
    public Task<SectionsPage> Sections(Guid courseId,CancellationToken token) {var s=Current();return api.StaffSections(s,Org(s),courseId,token);}
    [HttpGet("{courseId:guid}/sections/new"), HttpGet(Section+"/edit")]
    public Task<SectionEditor> SectionEditor(Guid courseId,[FromRoute] Guid? sectionId,CancellationToken token) {var s=Current();return api.SectionEditor(s,Org(s),courseId,sectionId,token);}
    [HttpPost("{courseId:guid}/sections"), HttpPut(Section)]
    public Task<IActionResult> SaveSection(Guid courseId,[FromRoute] Guid? sectionId,SectionForm form,CancellationToken token) {var s=Current();return Change(()=>api.SaveSection(s,Org(s),courseId,sectionId,form,token));}
    [HttpGet(Section)]
    public Task<LessonsPage> Lessons(Guid courseId,Guid sectionId,CancellationToken token) {var s=Current();return api.StaffLessons(s,Org(s),courseId,sectionId,token);}
    [HttpGet(Section+"/lessons/new"), HttpGet(Unit+"/edit")]
    public Task<LessonEditor> LessonEditor(Guid courseId,Guid sectionId,[FromRoute] Guid? lessonId,CancellationToken token) {var s=Current();return api.LessonEditor(s,Org(s),courseId,sectionId,lessonId,token);}
    [HttpPost(Section+"/lessons"), HttpPut(Unit)]
    public Task<IActionResult> SaveLesson(Guid courseId,Guid sectionId,[FromRoute] Guid? lessonId,LessonForm form,CancellationToken token) {var s=Current();return Change(()=>api.SaveLesson(s,Org(s),courseId,sectionId,lessonId,form,token));}
    [HttpGet(Unit)]
    public async Task<object> Lesson(Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token) {var s=Current();var unit=await api.StaffUnit(s,Org(s),courseId,sectionId,lessonId,token);var legacy=unit.Content.Legacy;var embed=SafeContent.LegacyYouTube(legacy?.VideoUrl);return new {unit.OrganizationId,unit.CourseId,unit.SectionId,unit.Lesson,Content=new {unit.Content.Items,Legacy=legacy is null?null:new {legacy.Content,legacy.VideoUrl,YouTubeVideoId=embed is null?null:embed[(embed.LastIndexOf('/')+1)..]}}};}
    [HttpPost(Unit+"/items"), HttpPut(Unit+"/items/{itemId:guid}")]
    public Task<IActionResult> SaveItem(Guid courseId,Guid sectionId,Guid lessonId,[FromRoute] Guid? itemId,[FromBody] ContentForm form,CancellationToken token)
    {var s=Current();return Change(()=>api.SaveItem(s,Org(s),courseId,sectionId,lessonId,itemId,form,token));}
    [HttpPost(Unit+"/items/{itemId:guid}/delete")]
    public Task<IActionResult> Delete(Guid courseId,Guid sectionId,Guid lessonId,Guid itemId,StaffConfirmation form,CancellationToken token)
    {var s=Current();if(!form.Confirmed)return Task.FromResult<IActionResult>(BadRequest(BffJson.Error(400)));return Change(()=>api.DeleteItem(s,Org(s),courseId,sectionId,lessonId,itemId,token));}
    [HttpPut(Unit+"/items/reorder")]
    public Task<IActionResult> Reorder(Guid courseId,Guid sectionId,Guid lessonId,StaffOrder form,CancellationToken token)
    {var s=Current();return Change(()=>api.ReorderItems(s,Org(s),courseId,sectionId,lessonId,form.ItemIds,token));}
    [HttpPost("{courseId:guid}/publication"), HttpPost(Unit+"/publication")]
    public Task<IActionResult> Publish(Guid courseId,[FromRoute] Guid? sectionId,[FromRoute] Guid? lessonId,StaffPublication form,CancellationToken token)
    {var s=Current();if(!form.Confirmed)return Task.FromResult<IActionResult>(BadRequest(BffJson.Error(400)));return Change(()=>api.Publish(s,Org(s),courseId,sectionId,lessonId,form.Action,token));}
    [HttpGet("{courseId:guid}/readiness")]
    public Task<ReadinessPage> Readiness(Guid courseId,CancellationToken token) {var s=Current();return api.Readiness(s,Org(s),courseId,token);}
    [HttpPost(Unit+"/uploads")]
    [RequestSizeLimit(202L*1024*1024), RequestFormLimits(MultipartBodyLengthLimit=202L*1024*1024)]
    public Task<AssetView> Upload(Guid courseId,Guid sectionId,Guid lessonId,[FromForm] StaffUpload form,CancellationToken token)
    {var s=Current();return api.StaffUpload(s,Org(s),courseId,sectionId,lessonId,form.Type,form.File,token);}
    [HttpGet(Unit+"/items/{itemId:guid}/media"),HttpHead(Unit+"/items/{itemId:guid}/media")]
    public async Task Media(Guid courseId,Guid sectionId,Guid lessonId,Guid itemId,bool download,CancellationToken token)
    {
        var s=Current();var unit=await api.StaffUnit(s,Org(s),courseId,sectionId,lessonId,token);
        using var response=await api.Media(s,unit,itemId,HttpMethods.IsHead(Request.Method),Request.Headers.Range,Request.Headers.IfRange,token);
        await PrivateMediaProxy.WriteAsync(HttpContext,response,unit.Content.Items.Single(x=>x.Id==itemId).Type,itemId,download,token);
    }
    private async Task<IActionResult> Change(Func<Task> action)
    {try {await action();return NoContent();}catch(AuthoringValidationException ex){return StatusCode(ex.Status,BffJson.Error(ex.Status));}}
}
public class StaffConfirmation { public bool Confirmed {get;set;} }
public sealed class StaffPublication : StaffConfirmation { [Required] public string Action {get;set;}=""; }
public sealed class StaffOrder { [Required,MaxLength(10000)] public Guid[] ItemIds {get;set;}=[]; }
public sealed class StaffUpload { [Range(3,5)] public int Type {get;set;} [Required] public IFormFile File {get;set;}=null!; }
