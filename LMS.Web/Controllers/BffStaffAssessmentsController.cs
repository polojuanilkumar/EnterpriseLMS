using System.Text.RegularExpressions;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;
[ApiController,Authorize,AutoValidateAntiforgeryToken,RequestSizeLimit(1048576)]
[Route("bff/staff/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class BffStaffAssessmentsController(LearnerApi api,LearnerSessions sessions):ControllerBase
{
    private LearnerSession Current()=>sessions.Require(User);
    private Task<QuizEditorPage> Quiz(LearnerSession s,Guid c,Guid sec,Guid l,Guid? id,CancellationToken t)=>CheckedQuiz(s,c,sec,l,id,t);
    private async Task<QuizEditorPage> CheckedQuiz(LearnerSession s,Guid c,Guid sec,Guid l,Guid? id,CancellationToken t)
    {var page=await api.QuizEditor(s,s.OrganizationId??throw new WebApiException(403),c,sec,l,t);if(id.HasValue&&page.Quiz?.Id!=id)throw new WebApiException(404);return page;}
    [HttpGet("quiz")]
    public async Task<object> ReadQuiz(Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
    {var page=await Quiz(Current(),courseId,sectionId,lessonId,null,token);return new {Quiz=page.Quiz is null?null:QuizRow(page.Quiz),Questions=page.Questions.Select(row=>new {Question=new {row.Question.Id,row.Question.QuizId,row.Question.QuestionText,row.Question.QuestionType,row.Question.DisplayOrder,Marks=row.Question.Marks.ToString(System.Globalization.CultureInfo.InvariantCulture)},row.Options}),Form=new {page.Form.Title,page.Form.Description,PassingPercentage=page.Form.PassingPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture),page.Form.TimeLimitInMinutes,page.Form.MaxAttempts}};}
    [HttpPost("quiz"),HttpPut("quiz/{quizId:guid}")]
    public async Task<IActionResult> SaveQuiz(Guid courseId,Guid sectionId,Guid lessonId,[FromRoute]Guid? quizId,QuizForm form,CancellationToken token)
    {var s=Current();var p=await Quiz(s,courseId,sectionId,lessonId,quizId,token);if(!quizId.HasValue&&p.Quiz is not null)return Conflict(new BffError("conflict","A quiz already exists. Refresh stored state before editing."));return await Change(()=>api.SaveQuiz(s,p,form,token));}
    [HttpPost("quiz/{quizId:guid}/questions"),HttpPut("quiz/{quizId:guid}/questions/{questionId:guid}")]
    public async Task<IActionResult> Question(Guid courseId,Guid sectionId,Guid lessonId,Guid quizId,[FromRoute]Guid? questionId,QuestionForm form,CancellationToken token)
    {var s=Current();var p=await Quiz(s,courseId,sectionId,lessonId,quizId,token);return await Change(()=>api.QuizChange(s,p,"question",questionId,null,form,token));}
    [HttpPost("quiz/{quizId:guid}/questions/{questionId:guid}/options"),HttpPut("quiz/{quizId:guid}/questions/{questionId:guid}/options/{optionId:guid}")]
    public async Task<IActionResult> Option(Guid courseId,Guid sectionId,Guid lessonId,Guid quizId,Guid questionId,[FromRoute]Guid? optionId,OptionForm form,CancellationToken token)
    {var s=Current();var p=await Quiz(s,courseId,sectionId,lessonId,quizId,token);return await Change(()=>api.QuizChange(s,p,"option",questionId,optionId,form,token));}
    [HttpPost("quiz/{quizId:guid}/questions/{questionId:guid}/delete")]
    [HttpPost("quiz/{quizId:guid}/questions/{questionId:guid}/options/{optionId:guid}/delete")]
    public async Task<IActionResult> DeletePart(Guid courseId,Guid sectionId,Guid lessonId,Guid quizId,Guid questionId,[FromRoute]Guid? optionId,StaffConfirmation form,CancellationToken token)
    {if(!form.Confirmed)return BadRequest(BffJson.Error(400));var s=Current();var p=await Quiz(s,courseId,sectionId,lessonId,quizId,token);return await Change(()=>api.QuizChange(s,p,"delete",questionId,optionId,null,token));}
    [HttpPost("quiz/{quizId:guid}/delete")]
    public async Task<IActionResult> DeleteQuiz(Guid courseId,Guid sectionId,Guid lessonId,Guid quizId,StaffConfirmation form,CancellationToken token)
    {if(!form.Confirmed)return BadRequest(BffJson.Error(400));var s=Current();var p=await Quiz(s,courseId,sectionId,lessonId,quizId,token);return await Change(()=>api.StaffDeleteQuiz(s,p,token));}
    [HttpPost("quiz/{quizId:guid}/publication")]
    public async Task<IActionResult> PublishQuiz(Guid courseId,Guid sectionId,Guid lessonId,Guid quizId,StaffPublication form,CancellationToken token)
    {if(!form.Confirmed||form.Action is not ("publish" or "unpublish"))return BadRequest(BffJson.Error(400));var s=Current();var p=await Quiz(s,courseId,sectionId,lessonId,quizId,token);return await Change(()=>api.QuizChange(s,p,form.Action,null,null,null,token));}
    [HttpGet("assignments"),HttpGet("assignments/{assignmentId:guid}"),HttpGet("assignments/{assignmentId:guid}/submissions/{submissionId:guid}")]
    public async Task<object> Assignment(Guid courseId,Guid sectionId,Guid lessonId,[FromRoute]Guid? assignmentId,[FromRoute]Guid? submissionId,CancellationToken token)
    {var p=await api.StaffAssignment(Current(),courseId,sectionId,lessonId,assignmentId,submissionId,token);return new {Assignments=p.Assignments.Select(AssignmentRow),Assignment=p.Assignment is null?null:AssignmentRow(p.Assignment),Submission=p.Submission is null?null:WorkRow(p.Submission)};}
    [HttpGet("assignments/{assignmentId:guid}/submissions")]
    public async Task<object> Submissions(Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,int page=1,int pageSize=20,CancellationToken token=default)
    {var s=Current();var p=await api.StaffAssignment(s,courseId,sectionId,lessonId,assignmentId,null,token);var rows=await api.StaffAssignmentSubmissions(s,p,page,pageSize,token);return new {rows.Page,rows.PageSize,rows.Total,Items=rows.Items.Select(WorkRow)};}
    [HttpPost("assignments"),HttpPut("assignments/{assignmentId:guid}")]
    public async Task<IActionResult> SaveAssignment(Guid courseId,Guid sectionId,Guid lessonId,[FromRoute]Guid? assignmentId,StaffAssignmentForm form,CancellationToken token)
    {var s=Current();var p=await api.StaffAssignment(s,courseId,sectionId,lessonId,assignmentId,null,token);return await Change(()=>api.StaffSaveAssignment(s,p,form,token));}
    [HttpPost("assignments/{assignmentId:guid}/publication")]
    public async Task<IActionResult> PublishAssignment(Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,StaffAssessmentPublication form,CancellationToken token)
    {var s=Current();var p=await api.StaffAssignment(s,courseId,sectionId,lessonId,assignmentId,null,token);return await Change(()=>api.PublishAssignment(s,p,form.IsPublished,form.Confirmed,token));}
    [HttpPost("assignments/{assignmentId:guid}/submissions/{submissionId:guid}/grade")]
    public async Task<IActionResult> Grade(Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,Guid submissionId,AssignmentGradeForm form,CancellationToken token)
    {var s=Current();var p=await api.StaffAssignment(s,courseId,sectionId,lessonId,assignmentId,submissionId,token);return await Change(()=>api.GradeAssignment(s,p,form,token));}
    [HttpGet("assignments/{assignmentId:guid}/submissions/{submissionId:guid}/attachment"),HttpHead("assignments/{assignmentId:guid}/submissions/{submissionId:guid}/attachment")]
    public async Task Attachment(Guid courseId,Guid sectionId,Guid lessonId,Guid assignmentId,Guid submissionId,CancellationToken token)
    {
        var s=Current();var p=await api.StaffAssignment(s,courseId,sectionId,lessonId,assignmentId,submissionId,token);
        using var response=await api.AssignmentDownload(s,p,token,HttpMethods.IsHead(Request.Method));
        Response.ContentType="application/pdf";Response.Headers.ContentDisposition=$"attachment; filename=\"assignment-{submissionId:N}.pdf\"";
        Response.Headers.CacheControl="private, no-store";Response.Headers["X-Content-Type-Options"]="nosniff";Response.Headers["Content-Security-Policy"]="sandbox; default-src 'none'";
        if(response.Content.Headers.ContentLength is {} length)Response.ContentLength=length;
        if(!HttpMethods.IsHead(Request.Method))await response.Content.CopyToAsync(Response.Body,token);
    }
    private static object QuizRow(QuizView q)=>new {q.Id,q.LessonId,q.Title,q.Description,PassingPercentage=q.PassingPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture),q.TimeLimitInMinutes,q.MaxAttempts,q.IsPublished};
    private static object AssignmentRow(AssignmentView a)=>new {a.Id,a.LessonId,a.Title,a.Instructions,a.DueAtUtc,MaximumMarks=a.MaximumMarks.ToString(System.Globalization.CultureInfo.InvariantCulture),a.IsPublished,a.Frozen};
    private static object WorkRow(AssignmentSubmissionView s)=>new {s.Id,s.AssignmentId,s.UserId,s.Text,s.HasAttachment,s.SubmittedAt,s.InstructionsSnapshot,MaximumMarksSnapshot=s.MaximumMarksSnapshot.ToString(System.Globalization.CultureInfo.InvariantCulture),Grade=s.Grade is null?null:new {Marks=s.Grade.Marks.ToString(System.Globalization.CultureInfo.InvariantCulture),s.Grade.Feedback,s.Grade.GradedAt}};
    private async Task<IActionResult> Change(Func<Task> action)
    {
        try{await action();return NoContent();}
        catch(AuthoringValidationException ex)
        {
            // Only known business-policy text may cross the BFF error boundary. No diagnostics or arbitrary upstream body.
            var allowed=new[]{"Add at least one question before publishing or attempting the quiz.","Every quiz question must have positive marks.","Quiz questions, options, passing percentage, time limit and deletion are locked once an attempt exists.","Unpublish the quiz before editing or deleting it or changing its questions or options.","Instructions, due date and maximum marks are frozen once submissions exist.","This submission has already been graded. The original grade is retained."};
            var message=allowed.Contains(ex.Message)||Regex.IsMatch(ex.Message,@"\A(?:Question [0-9]+ (?:needs at least two options|must have exactly one correct option|needs at least two correct options|has an unsupported question type)|True/False question [0-9]+ needs exactly two options and one correct option)\.\z")?ex.Message:"Check required fields, numeric bounds, ordering and current stored state. No automatic retry was made.";
            return StatusCode(ex.Status,new BffError(ex.Status==409?"conflict":"validation_failed",message));
        }
    }
}
public sealed class StaffAssessmentPublication:StaffConfirmation {public bool IsPublished {get;set;}}
