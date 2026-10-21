using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[Authorize, AutoValidateAntiforgeryToken, Route("staff/{organizationId:guid}/courses")]
public sealed class StaffController(LearnerApi api,LearnerSessions sessions) : Controller
{
    private const long MaxAuthoringRequestBytes = 202L * 1024 * 1024;
    private const string UnitRoute="{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}";
    private LearnerSession Session(Guid organizationId)=>sessions.Require(User,organizationId);
    private static string UnitUrl(Guid org,Guid course,Guid section,Guid lesson)=>$"/staff/{org}/courses/{course}/sections/{section}/lessons/{lesson}";
    [HttpGet]
    public async Task<IActionResult> Courses(Guid organizationId,CancellationToken token)
        =>View(await api.StaffCourses(Session(organizationId),organizationId,token));
    [HttpGet("{courseId:guid}")]
    public async Task<IActionResult> Sections(Guid organizationId,Guid courseId,CancellationToken token)
        =>View(await api.StaffSections(Session(organizationId),organizationId,courseId,token));
    [HttpGet("{courseId:guid}/sections/{sectionId:guid}")]
    public async Task<IActionResult> Lessons(Guid organizationId,Guid courseId,Guid sectionId,CancellationToken token)
        =>View(await api.StaffLessons(Session(organizationId),organizationId,courseId,sectionId,token));
    [HttpGet(UnitRoute)]
    public async Task<IActionResult> Lesson(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
        =>View(new StaffLessonPage(await api.StaffUnit(Session(organizationId),organizationId,courseId,sectionId,lessonId,token)));
    [HttpGet(UnitRoute+"/items/new")]
    public async Task<IActionResult> Create(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
    {
        var unit=await api.StaffUnit(Session(organizationId),organizationId,courseId,sectionId,lessonId,token);
        return View("Edit",new ContentEditPage(unit,null,new ContentForm {DisplayOrder=unit.Content.Items.Length}));
    }
    [HttpGet(UnitRoute+"/items/{itemId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid itemId,CancellationToken token)
    {
        var unit=await api.StaffUnit(Session(organizationId),organizationId,courseId,sectionId,lessonId,token);
        var item=unit.Content.Items.SingleOrDefault(x=>x.Id==itemId)??throw new WebApiException(404);
        return View(new ContentEditPage(unit,itemId,new ContentForm {Type=item.Type,Title=item.Title,DisplayOrder=item.DisplayOrder,IsRequired=item.IsRequired,
            Text=item.Text,YouTubeUrl=item.YouTubeVideoId is null?null:$"https://youtu.be/{item.YouTubeVideoId}",AssetId=item.AssetId}));
    }
    [HttpPost(UnitRoute+"/items/new")]
    [HttpPost(UnitRoute+"/items/{itemId:guid}/edit")]
    [RequestSizeLimit(MaxAuthoringRequestBytes),RequestFormLimits(MultipartBodyLengthLimit=MaxAuthoringRequestBytes)]
    public async Task<IActionResult> Save(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid? itemId,ContentForm form,CancellationToken token)
    {
        var unit=await api.StaffUnit(Session(organizationId),organizationId,courseId,sectionId,lessonId,token);
        if(itemId.HasValue&&!unit.Content.Items.Any(x=>x.Id==itemId))throw new WebApiException(404);
        if(ModelState.IsValid)
        {
            try
            {
                await api.SaveItem(Session(organizationId),organizationId,courseId,sectionId,lessonId,itemId,form,token);
                return Redirect(UnitUrl(organizationId,courseId,sectionId,lessonId));
            }
            catch(WebApiException ex) when(ex.Status is 400 or 409 or 413)
            {ModelState.AddModelError("","Content could not be saved. Check the type, fields, file format/limit and asset ownership. Successfully uploaded assets are retained.");Response.StatusCode=ex.Status;}
        }
        else Response.StatusCode=400;
        return View("Edit",new ContentEditPage(unit,itemId,form));
    }
    [HttpGet(UnitRoute+"/items/{itemId:guid}/delete")]
    public async Task<IActionResult> Delete(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid itemId,CancellationToken token)
    {
        var unit=await api.StaffUnit(Session(organizationId),organizationId,courseId,sectionId,lessonId,token);
        if(!unit.Content.Items.Any(x=>x.Id==itemId))throw new WebApiException(404);
        ViewData["ItemId"]=itemId;return View(unit);
    }
    [HttpPost(UnitRoute+"/items/{itemId:guid}/delete")]
    public async Task<IActionResult> DeleteConfirmed(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid itemId,bool confirmed,CancellationToken token)
    {
        if(!confirmed)return BadRequest("Confirm deletion first.");
        await api.DeleteItem(Session(organizationId),organizationId,courseId,sectionId,lessonId,itemId,token);
        return Redirect(UnitUrl(organizationId,courseId,sectionId,lessonId));
    }
    [HttpGet(UnitRoute+"/reorder")]
    public async Task<IActionResult> Reorder(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
        =>View(await api.StaffUnit(Session(organizationId),organizationId,courseId,sectionId,lessonId,token));
    [HttpPost(UnitRoute+"/reorder")]
    public async Task<IActionResult> Reorder(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid[] itemIds,int[] positions,CancellationToken token)
    {
        if(!ModelState.IsValid || itemIds.Length!=positions.Length || positions.Any(x=>x<0) || positions.Distinct().Count()!=positions.Length)return BadRequest("Provide a distinct nonnegative position for every item.");
        var ordered=itemIds.Zip(positions).OrderBy(x=>x.Second).Select(x=>x.First).ToArray();
        await api.ReorderItems(Session(organizationId),organizationId,courseId,sectionId,lessonId,ordered,token);
        return Redirect(UnitUrl(organizationId,courseId,sectionId,lessonId));
    }
    [HttpGet("{courseId:guid}/publication")]
    [HttpGet(UnitRoute+"/publication")]
    public async Task<IActionResult> Publication(Guid organizationId,Guid courseId,Guid? sectionId,Guid? lessonId,string actionName,CancellationToken token)
    {
        string title;
        if(lessonId.HasValue)
        {
            if(!sectionId.HasValue || actionName is not ("publish" or "unpublish"))return BadRequest();
            title=(await api.StaffUnit(Session(organizationId),organizationId,courseId,sectionId.Value,lessonId.Value,token)).Lesson.Title;
        }
        else
        {
            if(actionName is not ("publish" or "archive"))return BadRequest();
            title=(await api.StaffSections(Session(organizationId),organizationId,courseId,token)).Course.Title;
        }
        return View(new PublicationPage(organizationId,courseId,sectionId,lessonId,title,actionName));
    }
    [HttpPost("{courseId:guid}/publication")]
    [HttpPost(UnitRoute+"/publication")]
    public async Task<IActionResult> PublicationConfirmed(Guid organizationId,Guid courseId,Guid? sectionId,Guid? lessonId,string actionName,bool confirmed,CancellationToken token)
    {
        if(!confirmed)return BadRequest("Explicit confirmation is required.");
        await api.Publish(Session(organizationId),organizationId,courseId,sectionId,lessonId,actionName,token);
        return Redirect(lessonId.HasValue?UnitUrl(organizationId,courseId,sectionId!.Value,lessonId.Value):$"/staff/{organizationId}/courses/{courseId}");
    }
    [HttpGet(UnitRoute+"/items/{itemId:guid}/media"),HttpHead(UnitRoute+"/items/{itemId:guid}/media")]
    public async Task Media(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid itemId,bool download,CancellationToken token)
    {
        var session=Session(organizationId);
        var unit=await api.StaffUnit(session,organizationId,courseId,sectionId,lessonId,token);
        using var result=await api.Media(session,unit,itemId,HttpMethods.IsHead(Request.Method),Request.Headers.Range,Request.Headers.IfRange,token);
        await PrivateMediaProxy.WriteAsync(HttpContext,result,unit.Content.Items.Single(x=>x.Id==itemId).Type,itemId,download,token);
    }
}
