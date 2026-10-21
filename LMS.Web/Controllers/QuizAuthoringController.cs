using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[Authorize,AutoValidateAntiforgeryToken,Route("staff/{organizationId:guid}/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/quiz")]
public sealed class QuizAuthoringController(LearnerApi api,LearnerSessions sessions):Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
        =>View(await api.QuizEditor(sessions.Require(User,organizationId),organizationId,courseId,sectionId,lessonId,token));
    [HttpPost]
    public async Task<IActionResult> Save(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,[Bind(Prefix="Form")]QuizForm form,CancellationToken token)
    {
        var session=sessions.Require(User,organizationId);
        var page=await api.QuizEditor(session,organizationId,courseId,sectionId,lessonId,token);
        if(ModelState.IsValid)
            try {await api.SaveQuiz(session,page,form,token);return Redirect(Request.Path);}
            catch(AuthoringValidationException ex){ModelState.AddModelError("",ex.Message);}
        Response.StatusCode=400;return View("Index",page with {Form=form});
    }
    [HttpPost("change")]
    public async Task<IActionResult> Change(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,string actionName,Guid? questionId,Guid? optionId,[Bind(Prefix="question")]QuestionForm question,[Bind(Prefix="option")]OptionForm option,bool confirmed,CancellationToken token)
    {
        var session=sessions.Require(User,organizationId);
        var page=await api.QuizEditor(session,organizationId,courseId,sectionId,lessonId,token);
        // Each form contains only one DTO. Ignore validation for the unused DTO.
        if(actionName!="question")foreach(var key in ModelState.Keys.Where(x=>x.StartsWith("question.",StringComparison.OrdinalIgnoreCase)).ToArray())ModelState.Remove(key);
        if(actionName!="option")foreach(var key in ModelState.Keys.Where(x=>x.StartsWith("option.",StringComparison.OrdinalIgnoreCase)).ToArray())ModelState.Remove(key);
        if(actionName is "publish" or "unpublish" or "delete" && !confirmed)ModelState.AddModelError("","Explicit confirmation is required.");
        if(ModelState.IsValid)
            try {await api.QuizChange(session,page,actionName,questionId,optionId,actionName=="question"?question:option,token);return Redirect(Request.Path.Value![..^7]);}
            catch(AuthoringValidationException ex){ModelState.AddModelError("",ex.Message);}
        ViewData["PendingQuestion"]=question;ViewData["PendingOption"]=option;
        ViewData["QuestionId"]=questionId;ViewData["OptionId"]=optionId;ViewData["ActionName"]=actionName;
        Response.StatusCode=400;return View("Index",page);
    }
}
