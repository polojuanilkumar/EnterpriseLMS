using LMS.Web.Models;
namespace LMS.Web.Services;

public sealed partial class LearnerApi
{
    public async Task<LearnerQuizPage> StudentQuiz(LearnerSession session, Guid course, Guid section, Guid lesson, Guid? attempt, CancellationToken token)
    {
        // Reuse the viewer's stored Student/enrollment/parent checks, then MVC disclosure rules.
        await StudentUnit(session, course, section, lesson, token);
        var page = await LearnerQuiz(session, session.OrganizationId!.Value, course, section, lesson, attempt, token);
        if (page.Quiz is { IsPublished: false }) throw new WebApiException(404);
        if (attempt.HasValue && page.Attempt is null) throw new WebApiException(404);
        if (page.Attempts.Any(x => x.AttemptId == Guid.Empty || x.Status is < 1 or > 3)
            || page.Attempts.Select(x => x.AttemptId).Distinct().Count() != page.Attempts.Length) throw new WebApiException(502);
        if (page.Questions.Select(x => x.Question.Id).Distinct().Count() != page.Questions.Length
            || page.Questions.Any(x => x.Question.Id == Guid.Empty || x.Question.QuestionType is < 1 or > 3 || x.Options.Any(o => o.Id == Guid.Empty) || x.Options.Select(o => o.Id).Distinct().Count() != x.Options.Length)) throw new WebApiException(502);
        return page;
    }
    public static BffStudentQuiz StudentQuizProjection(LearnerQuizPage page)
    {
        static QuizAttemptView Safe(QuizAttemptView a) => a.Status == 2 ? a : a with { EarnedMarks = null, IsPassed = null };
        return new(page.Unit.OrganizationId, page.Unit.CourseId, page.Unit.SectionId, page.Unit.Lesson.Id, page.Quiz, page.Active,
            page.Attempts.Select(Safe).ToArray(), page.Attempt is null ? null : Safe(page.Attempt),
            page.Questions.Select(x => new BffQuizQuestion(x.Question.Id, x.Question.QuestionText, x.Question.QuestionType,
                x.Question.DisplayOrder, x.Question.Marks, x.Options)).ToArray(), page.Result, page.Review);
    }
    public async Task<Guid> StudentQuizStart(LearnerSession session, Guid course, Guid section, Guid lesson, CancellationToken token)
    {
        var page = await StudentQuiz(session, course, section, lesson, null, token);
        if (page.Quiz is null) throw new WebApiException(404);
        if (!page.Active) throw new WebApiException(403);
        var id = await StartQuiz(session, page, token);
        if (id == Guid.Empty) throw new WebApiException(502);
        // Validate a newly returned ID against stored owner history before exposing it.
        await StudentQuiz(session, course, section, lesson, id, token);
        return id;
    }
    public async Task StudentQuizSubmit(LearnerSession session, Guid course, Guid section, Guid lesson, Guid attempt, BffQuizSubmit request, CancellationToken token)
    {
        if (!request.Confirmed || request.Answers is null || request.Answers.Any(x => x is null || x.SelectedOptionIds is null)) throw new WebApiException(400);
        var page = await StudentQuiz(session, course, section, lesson, attempt, token);
        if (!page.Active) throw new WebApiException(403);
        await SubmitQuiz(session, page, new() { Answers = request.Answers.Select(x => new QuizAnswerForm { QuestionId = x.QuestionId, SelectedOptionIds = x.SelectedOptionIds }).ToList() }, token);
    }
}
