using System.Net.Http.Json;
using LMS.Web.Models;
namespace LMS.Web.Services;

public sealed partial class LearnerApi
{
    private async Task<QuizView?> LessonQuiz(LearnerSession session, UnitPage unit, CancellationToken token)
    {
        QuizView quiz;
        try { quiz=await Get<QuizView>($"{unit.Lesson.Id}/quiz",session,unit.OrganizationId,token); }
        catch(WebApiException ex) when(ex.Status==404) { return null; }
        if(quiz.LessonId!=unit.Lesson.Id)throw new WebApiException(404);
        return quiz;
    }
    public async Task<QuizEditorPage> QuizEditor(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,CancellationToken token)
    {
        var unit=await StaffUnit(session,org,course,section,lesson,token);
        var quiz=await LessonQuiz(session,unit,token);
        var rows=new List<StaffQuestionPage>();
        if(quiz is not null)
        {
            var questions=await Get<QuestionView[]>($"api/Quizzes/{quiz.Id}/questions",session,org,token);
            if(questions.Any(x=>x.QuizId!=quiz.Id))throw new WebApiException(404);
            foreach(var q in questions.OrderBy(x=>x.DisplayOrder))
            {
                var options=await Get<StaffOptionView[]>($"api/Quizzes/{quiz.Id}/questions/{q.Id}/options",session,org,token);
                if(options.Any(x=>x.QuestionId!=q.Id))throw new WebApiException(404);
                rows.Add(new(q,options.OrderBy(x=>x.DisplayOrder).ToArray()));
            }
        }
        return new(unit,quiz,rows.ToArray(),quiz is null?new():new(){Title=quiz.Title,Description=quiz.Description,PassingPercentage=quiz.PassingPercentage,TimeLimitInMinutes=quiz.TimeLimitInMinutes,MaxAttempts=quiz.MaxAttempts});
    }
    public async Task SaveQuiz(LearnerSession session,QuizEditorPage page,QuizForm form,CancellationToken token)
        => await AuthoringChange(page.Quiz is null?HttpMethod.Post:HttpMethod.Put,$"{page.Unit.Lesson.Id}/quiz",form,session,page.Unit.OrganizationId,token);
    public async Task QuizChange(LearnerSession session,QuizEditorPage page,string action,Guid? question,Guid? option,object? form,CancellationToken token)
    {
        var quiz=page.Quiz??throw new WebApiException(404);
        var path=$"api/Quizzes/{quiz.Id}/questions";
        if(question.HasValue)
        {
            var row=page.Questions.SingleOrDefault(x=>x.Question.Id==question)??throw new WebApiException(404);
            path+=$"/{question}";
            if(option.HasValue&&!row.Options.Any(x=>x.Id==option))throw new WebApiException(404);
        }
        if(action is "publish" or "unpublish")
            await AuthoringChange(HttpMethod.Patch,$"{page.Unit.Lesson.Id}/quiz/{action}",new {},session,page.Unit.OrganizationId,token);
        else if(action=="question")
            await AuthoringChange(question.HasValue?HttpMethod.Put:HttpMethod.Post,path,form!,session,page.Unit.OrganizationId,token);
        else if(action=="option"&&question.HasValue)
            await AuthoringChange(option.HasValue?HttpMethod.Put:HttpMethod.Post,path+"/options"+(option.HasValue?$"/{option}":""),form!,session,page.Unit.OrganizationId,token);
        else if(action=="delete"&&question.HasValue)
            await AuthoringChange(HttpMethod.Delete,path+(option.HasValue?$"/options/{option}":""),new {},session,page.Unit.OrganizationId,token);
        else throw new WebApiException(400);
    }
    public async Task<LearnerQuizPage> LearnerQuiz(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,Guid? attempt,CancellationToken token)
    {
        if(session.OrganizationId!=org)throw new WebApiException(403);
        var unit=await Unit(session,org,course,section,lesson,token);
        var quiz=await LessonQuiz(session,unit,token);
        if(quiz is null)return new(unit,null,[],false,[]);
        var enrollments=await Get<EnrollmentView[]>("api/Enrollments/me",session,org,token);
        var active=enrollments.Any(x=>x.CourseId==course&&x.Status==1);
        var attempts=await Get<QuizAttemptView[]>($"api/Quizzes/{quiz.Id}/attempts",session,org,token);
        var selected=attempt.HasValue?attempts.SingleOrDefault(x=>x.AttemptId==attempt)??throw new WebApiException(404):null;
        var rows=new List<QuizQuestionPage>();
        QuizResultView? result=null;QuizReviewView? review=null;
        if(selected?.Status==1)
        {
            var questions=await Get<QuestionView[]>($"api/Quizzes/{quiz.Id}/questions",session,org,token);
            if(questions.Any(x=>x.QuizId!=quiz.Id))throw new WebApiException(404);
            foreach(var q in questions.OrderBy(x=>x.DisplayOrder))
                rows.Add(new(q,(await Get<LearnerOptionView[]>($"api/Quizzes/{quiz.Id}/questions/{q.Id}/options/student",session,org,token)).OrderBy(x=>x.DisplayOrder).ToArray()));
        }
        else if(selected?.Status==2)
        {
            result=await Get<QuizResultView>($"api/Quizzes/{quiz.Id}/attempts/{selected.AttemptId}/result",session,org,token);
            review=await Get<QuizReviewView>($"api/Quizzes/{quiz.Id}/attempts/{selected.AttemptId}/answers",session,org,token);
            if(result.AttemptId!=selected.AttemptId||review.AttemptId!=selected.AttemptId)throw new WebApiException(404);
        }
        return new(unit,quiz,attempts,active,rows.ToArray(),selected,result,review);
    }
    public async Task<Guid> StartQuiz(LearnerSession session,LearnerQuizPage page,CancellationToken token)
    {
        var existing=page.Attempts.FirstOrDefault(x=>x.Status==1&&x.Deadline.ToUniversalTime()>DateTime.UtcNow);
        if(existing is not null)return existing.AttemptId;
        using var request=Request(HttpMethod.Post,$"api/Quizzes/{page.Quiz?.Id??throw new WebApiException(404)}/attempts/start",session,page.Unit.OrganizationId);
        using var response=await http.SendAsync(request,token);
        return (await Data<QuizStartView>(response,token)).AttemptId;
    }
    public async Task SubmitQuiz(LearnerSession session,LearnerQuizPage page,QuizSubmission form,CancellationToken token)
    {
        var attempt=page.Attempt??throw new WebApiException(404);
        // Completed attempts still go through the API's repeat-submission policy.
        if(form.Answers is null||form.Answers.Any(a=>a is null||a.SelectedOptionIds is null))throw new WebApiException(400);
        if(attempt.Status==1 && (form.Answers.Select(x=>x.QuestionId).Distinct().Count()!=form.Answers.Count||form.Answers.Any(a=>!page.Questions.Any(q=>q.Question.Id==a.QuestionId&&a.SelectedOptionIds.All(id=>q.Options.Any(o=>o.Id==id))))))throw new WebApiException(400);
        await AuthoringChange(HttpMethod.Post,$"api/Quizzes/{page.Quiz!.Id}/attempts/{attempt.AttemptId}/submit",form,session,page.Unit.OrganizationId,token);
    }
}
