using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[Authorize,AutoValidateAntiforgeryToken,Route("learn/{organizationId:guid}/courses/{courseId:guid}/sections/{sectionId:guid}/units/{lessonId:guid}/quiz")]
public sealed class QuizLearningController(LearnerApi api,LearnerSessions sessions):Controller
{
    [HttpGet, HttpGet("attempts/{attemptId:guid}")]
    public async Task<IActionResult> Index(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid? attemptId,CancellationToken token)
    {
        var page=await api.LearnerQuiz(sessions.RequireLearner(User,organizationId),organizationId,courseId,sectionId,lessonId,attemptId,token);
        if(TempData["QuizNotice"] is string notice)page=page with {Notice=notice,Questions=[]};
        return View(page);
    }
    private IActionResult UnknownOutcome(string message,string path)
    {
        // Redirect to GET so refreshing the recovery page cannot replay the mutation.
        // Only a fixed notice is stored; submitted answers stay out of TempData.
        TempData["QuizNotice"]=message;
        return Redirect(path);
    }
    [HttpPost("start")]
    public async Task<IActionResult> Start(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
    {
        var session=sessions.RequireLearner(User,organizationId);
        var page=await api.LearnerQuiz(session,organizationId,courseId,sectionId,lessonId,null,token);
        try {var id=await api.StartQuiz(session,page,token);return Redirect(Request.Path.Value![..^6]+$"/attempts/{id}");}
        catch(WebApiException ex) when(ex.Status is 400 or 409){return View("Index",page with {Notice="Start was rejected. Refresh history to check the active attempt and attempt limit."});}
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException || ex is WebApiException {Status:502}){return UnknownOutcome("The start outcome is unknown. Refresh history before starting again; an attempt may already exist.",Request.Path.Value![..^6]);}
    }
    [HttpPost("attempts/{attemptId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,Guid attemptId,QuizSubmission form,CancellationToken token)
    {
        var session=sessions.RequireLearner(User,organizationId);
        var page=await api.LearnerQuiz(session,organizationId,courseId,sectionId,lessonId,attemptId,token);
        if(!ModelState.IsValid){ViewData["Answers"]=form;return View("Index",page);}
        try {await api.SubmitQuiz(session,page,form,token);return Redirect(Request.Path.Value![..^7]);}
        catch(AuthoringValidationException ex){ModelState.AddModelError("",ex.Message);}
        catch(WebApiException ex) when(ex.Status is 400 or 409){ModelState.AddModelError("","Submission was rejected. Refresh history to check status and expiration.");}
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException || ex is WebApiException {Status:502})
        {
            return UnknownOutcome("The submission outcome is unknown. Refresh history and open the result before deciding whether to submit again. Answers have not been automatically resent.",Request.Path.Value![..^7]);
        }
        ViewData["Answers"]=form;return View("Index",page);
    }
}
