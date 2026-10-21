using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;
[Authorize,AutoValidateAntiforgeryToken]
[Route("staff/{organizationId:guid}/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/assignments")]
[Route("learn/{organizationId:guid}/courses/{courseId:guid}/sections/{sectionId:guid}/units/{lessonId:guid}/assignments")]
public sealed class AssignmentsController(LearnerApi api,LearnerSessions sessions):Controller
{
    private bool Staff=>Request.Path.StartsWithSegments("/staff");
    private Task<AssignmentPage> Page(Guid org,Guid course,Guid section,Guid lesson,Guid? assignment,Guid? submission,CancellationToken token)
        =>api.AssignmentPage(sessions.Require(User,org),org,course,section,lesson,Staff,assignment,submission,token);
    private async Task<IActionResult> Change(Func<Task> action,string target,string view,AssignmentPage page)
    {
        if(!ModelState.IsValid){Response.StatusCode=400;return View(view,page);}
        try {await action();return Redirect(target);}
        catch(AuthoringValidationException ex){ModelState.AddModelError("",ex.Message);Response.StatusCode=400;return View(view,page);}
        catch(Exception ex) when(ex is HttpRequestException or System.Text.Json.JsonException || ex is OperationCanceledException&&!HttpContext.RequestAborted.IsCancellationRequested || ex is WebApiException {Status:502})
        {Response.StatusCode=502;return View("Recovery",new AssignmentRecoveryPage(target,"The outcome is unknown. Open the stored assignment or submission to check its state. Nothing has been automatically replayed; do not repeat a final submission or grade without checking."));}
    }
    [HttpGet]
    public async Task<IActionResult> Index(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
        =>View(await Page(organizationId,courseId,sectionId,lessonId,null,null,token));
    [HttpGet("new"),HttpGet("{assignmentId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid? assignmentId,CancellationToken token)
    {var page=await Page(organizationId,courseId,sectionId,lessonId,assignmentId,null,token);if(!page.Staff)throw new WebApiException(403);return View(page);}
    [HttpPost("new"),HttpPost("{assignmentId:guid}/edit")]
    public async Task<IActionResult> Save(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid? assignmentId,[Bind(Prefix="Form")]AssignmentForm form,CancellationToken token)
    {
        var page=await Page(organizationId,courseId,sectionId,lessonId,assignmentId,null,token);if(!page.Staff)throw new WebApiException(403);
        if(page.Assignment?.Frozen==true)
        {
            // Disabled fields are restored from authorized stored state, never rounded browser dates
            // or normalized textarea line endings. The API still rejects prohibited direct edits.
            form.Instructions=page.Form.Instructions;form.DueAtUtc=page.Form.DueAtUtc;form.MaximumMarks=page.Form.MaximumMarks;
            foreach(var key in new[]{"Form.Instructions","Form.DueAtUtc","Form.MaximumMarks"})ModelState.Remove(key);
        }
        return await Change(()=>api.SaveAssignment(sessions.Require(User,organizationId),page,form,token),page.Root,"Edit",page with {Form=form});
    }
    [HttpGet("{assignmentId:guid}")]
    public async Task<IActionResult> Detail(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,CancellationToken token)
        =>View(await Page(organizationId,courseId,sectionId,lessonId,assignmentId,null,token));
    [HttpPost("{assignmentId:guid}/publication")]
    public async Task<IActionResult> Publish(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,bool published,bool confirmed,CancellationToken token)
    {
        var page=await Page(organizationId,courseId,sectionId,lessonId,assignmentId,null,token);
        return await Change(()=>api.PublishAssignment(sessions.Require(User,organizationId),page,published,confirmed,token),page.Root+"/"+assignmentId,"Detail",page);
    }
    [HttpPost("{assignmentId:guid}/submit")]
    [RequestSizeLimit(28311552),RequestFormLimits(MultipartBodyLengthLimit=28311552)]
    public async Task<IActionResult> Submit(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,[Bind(Prefix="SubmissionForm")]AssignmentSubmissionForm form,CancellationToken token)
    {
        var page=await Page(organizationId,courseId,sectionId,lessonId,assignmentId,null,token);
        if(Request.HasFormContentType&&Request.Form.Files.Count>1)ModelState.AddModelError("","Only one PDF is accepted.");
        return await Change(()=>api.SubmitAssignment(sessions.Require(User,organizationId),page,form,token),page.Root+"/"+assignmentId,"Detail",page with {SubmissionForm=form});
    }
    [HttpGet("{assignmentId:guid}/submissions/{submissionId:guid}")]
    public async Task<IActionResult> Review(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,Guid submissionId,CancellationToken token)
        =>View(await Page(organizationId,courseId,sectionId,lessonId,assignmentId,submissionId,token));
    [HttpPost("{assignmentId:guid}/submissions/{submissionId:guid}/grade")]
    public async Task<IActionResult> Grade(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,Guid submissionId,[Bind(Prefix="GradeForm")]AssignmentGradeForm form,CancellationToken token)
    {
        var page=await Page(organizationId,courseId,sectionId,lessonId,assignmentId,submissionId,token);
        return await Change(()=>api.GradeAssignment(sessions.Require(User,organizationId),page,form,token),page.Root+$"/{assignmentId}/submissions/{submissionId}","Review",page with {GradeForm=form});
    }
    [HttpGet("{assignmentId:guid}/submissions/{submissionId:guid}/attachment")]
    public async Task<IActionResult> Attachment(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,Guid submissionId,CancellationToken token)
    {
        var page=await Page(organizationId,courseId,sectionId,lessonId,assignmentId,submissionId,token);
        using var response=await api.AssignmentDownload(sessions.Require(User,organizationId),page,token);
        // This is a separate assignment download route; never authorize via lesson media.
        Response.Headers.CacheControl="private, no-store";Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers["Content-Security-Policy"]="sandbox; default-src 'none'";
        Response.Headers.ContentDisposition=$"attachment; filename=\"assignment-{submissionId:N}.pdf\"";
        Response.ContentType="application/pdf";
        await response.Content.CopyToAsync(Response.Body,token);return new EmptyResult();
    }
    [HttpGet("~/learn/{organizationId:guid}/assignments/history")]
    public async Task<IActionResult> History(Guid organizationId,CancellationToken token)
        =>View(await api.AssignmentHistory(sessions.RequireLearner(User,organizationId),organizationId,token));
}
