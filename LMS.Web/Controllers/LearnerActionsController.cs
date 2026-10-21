using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;

[Authorize,AutoValidateAntiforgeryToken,Route("learn/{organizationId:guid}/courses")]
public sealed class LearnerActionsController(LearnerApi api,LearnerSessions sessions):Controller
{
    private IActionResult Recovery(Guid org,string target,string message)
    {
        TempData["LearningRecoveryNotice"]=message;
        TempData["LearningRecoveryTarget"]=target;
        return Redirect($"/learn/{org}/courses/recovery");
    }
    private async Task<IActionResult> Mutate(Guid org,string target,Func<LearnerSession,Task> action)
    {
        var session=sessions.RequireLearner(User,org);
        try {await action(session);return Redirect(target);}
        catch(AuthoringValidationException ex){return Recovery(org,target,ex.Message);}
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException || ex is WebApiException {Status:502})
        {return Recovery(org,target,"The outcome is unknown. Inspect the stored enrollment or progress before deciding whether to try again. No change has been automatically replayed.");}
    }
    [HttpGet("recovery")]
    public async Task<IActionResult> Recovery(Guid organizationId,CancellationToken token)
    {
        await api.LearnerAccess(sessions.RequireLearner(User,organizationId),organizationId,token);
        var root=$"/learn/{organizationId}/courses";
        var target=TempData["LearningRecoveryTarget"] as string;
        if(target is null||!(target==root||target.StartsWith(root+"/",StringComparison.Ordinal)))target=root;
        return View(new LearnerRecoveryPage(organizationId,TempData["LearningRecoveryNotice"] as string??"Inspect the stored enrollment or progress before taking another action.",target));
    }
    [HttpPost("{courseId:guid}/enroll")]
    public Task<IActionResult> Enroll(Guid organizationId,Guid courseId,bool confirmedReenrollment,CancellationToken token)
        =>Mutate(organizationId,$"/learn/{organizationId}/courses/catalog",session=>api.Enroll(session,organizationId,courseId,confirmedReenrollment,token));
    [HttpPost("{courseId:guid}/enrollments/{enrollmentId:guid}/{actionName}")]
    public Task<IActionResult> Enrollment(Guid organizationId,Guid courseId,Guid enrollmentId,string actionName,bool confirmed,CancellationToken token)
        =>Mutate(organizationId,actionName=="complete"?$"/learn/{organizationId}/courses/{courseId}":$"/learn/{organizationId}/courses",session=>api.ChangeEnrollment(session,organizationId,courseId,enrollmentId,actionName,confirmed,token));
    [HttpPost("{courseId:guid}/sections/{sectionId:guid}/units/{lessonId:guid}/progress/{actionName}")]
    public async Task<IActionResult> Lesson(Guid organizationId,Guid courseId,Guid sectionId,Guid lessonId,string actionName,[Bind(Prefix="Form")]LessonProgressForm form,CancellationToken token)
    {
        var target=$"/learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}";
        if(!ModelState.IsValid)
        {
            await api.LessonState(sessions.RequireLearner(User,organizationId),organizationId,courseId,sectionId,lessonId,token);
            return Recovery(organizationId,target,actionName=="update"
                ?"Enter a progress percentage between 0 and 100. No update was sent."
                :"Check the lesson action and confirmation. No change was sent.");
        }
        if(actionName=="complete"&&!form.Confirmed)
        {
            await api.LessonState(sessions.RequireLearner(User,organizationId),organizationId,courseId,sectionId,lessonId,token);
            return Recovery(organizationId,target,"Confirm that you want to mark this lesson complete. No completion was sent.");
        }
        return await Mutate(organizationId,target,session=>api.ChangeLessonProgress(session,organizationId,courseId,sectionId,lessonId,actionName,form,token));
    }
}
