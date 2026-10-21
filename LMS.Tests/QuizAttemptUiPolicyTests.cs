using System.Text.Json;
using LMS.Application.Features.QuizAttempts.GetQuizAttemptAnswers;
using LMS.Application.Features.QuizAttempts.GetQuizAttemptResult;
using LMS.Application.Features.QuizAttempts.GetQuizAttempts;
using LMS.Application.Features.QuizAttempts.StartQuizAttempt;
using LMS.Application.Features.QuizAttempts.SubmitQuizAttempt;
using LMS.Application.Features.QuizOptions.GetStudentOptions;
using LMS.Application.Interfaces.QuizAttempts;
using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;
using Xunit;
namespace LMS.Tests;

public sealed class QuizAttemptUiPolicyTests
{
    private sealed class Fixture
    {
        public LearningFlowFixture Flow;
        public LearnerWebTests.Clock Clock=new();
        public Mock<IQuizAttemptRepository> Attempts=new();
        public Mock<IQuizQuestionRepository> Questions=new();
        public Mock<IQuizOptionRepository> Options=new();
        public List<QuizAttempt> History=[];
        public QuizQuestion Question;
        public QuizOption[] Choices;
        public Fixture(EnrollmentStatus status=EnrollmentStatus.Active,QuestionType type=QuestionType.SingleChoice)
        {
            Flow=new(status);Question=new(Flow.Quiz.Id,"Question",type,1,2);
            Choices=[new(Question.Id,"A",true,1),new(Question.Id,"B",type==QuestionType.MultipleChoice,2)];
            Questions.Setup(x=>x.GetByQuizIdAsync(Flow.Quiz.Id,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{Question});
            Questions.Setup(x=>x.GetByIdAsync(Question.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Question);
            Options.Setup(x=>x.GetByQuestionIdAsync(Question.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Choices);
            Attempts.Setup(x=>x.GetByQuizAndUserAsync(Flow.Quiz.Id,Flow.UserId,It.IsAny<CancellationToken>())).ReturnsAsync(History);
        }
        public QuizAttempt Add()=>AddAt(Clock.Now.UtcDateTime.AddMinutes(10));
        public QuizAttempt AddAt(DateTime deadline){var attempt=new QuizAttempt(Flow.Quiz.Id,Flow.UserId,History.Count+1,Clock.Now.UtcDateTime,deadline,2,70);History.Add(attempt);return attempt;}
        public SubmitQuizAttemptHandler Submit=>new(Attempts.Object,Flow.Quizzes.Object,Questions.Object,Options.Object,Clock,Flow.QuizReadable);
        public SubmitQuizAttemptRequest Payload=>new(){Answers=[new(){QuestionId=Question.Id,SelectedOptionIds=Choices.Where(x=>x.IsCorrect).Select(x=>x.Id).ToList()}]};
    }
    // Generic private outcome types cannot be named here. Match the generic transaction method
    // with an invocation callback, executing application logic without SQL or a connection.
    private static Mock<IQuizAttemptRepository> Transactions()
    {
        var mock=new Mock<IQuizAttemptRepository>();
        mock.Setup(x=>x.ExecuteTransactionAsync(It.IsAny<Guid>(),It.IsAny<Func<CancellationToken,Task<It.IsAnyType>>>(),It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(invocation=>invocation.Arguments[1].GetType().GetMethod("Invoke")!.Invoke(invocation.Arguments[1],[invocation.Arguments[2]])!));
        return mock;
    }
    [Theory]
    [InlineData(QuestionType.SingleChoice)] [InlineData(QuestionType.MultipleChoice)] [InlineData(QuestionType.TrueFalse)]
    public async Task Real_scoring_handles_all_types_and_repeat_is_rejected(QuestionType type)
    {
        var f=new Fixture(type:type);f.Attempts=Transactions();var attempt=f.Add();
        f.Attempts.Setup(x=>x.GetByQuizAndUserAsync(f.Flow.Quiz.Id,f.Flow.UserId,It.IsAny<CancellationToken>())).ReturnsAsync(f.History);
        var result=await f.Submit.HandleAsync(f.Flow.Quiz.Id,attempt.Id,f.Flow.UserId,f.Payload);
        Assert.Equal(2,result.EarnedMarks);Assert.True(result.IsPassed);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Submit.HandleAsync(f.Flow.Quiz.Id,attempt.Id,f.Flow.UserId,f.Payload));
        f.Attempts.Verify(x=>x.AddAnswersAsync(It.IsAny<IEnumerable<QuizAttemptAnswer>>(),It.IsAny<IEnumerable<QuizAttemptAnswerOption>>(),It.IsAny<CancellationToken>()),Times.Once);
    }
    [Theory]
    [InlineData(0,true)] [InlineData(1,false)]
    public async Task Server_deadline_boundary_remains_authoritative(int seconds,bool accepted)
    {
        var f=new Fixture();f.Attempts=Transactions();var attempt=f.AddAt(f.Clock.Now.UtcDateTime.AddSeconds(-seconds));
        f.Attempts.Setup(x=>x.GetByQuizAndUserAsync(f.Flow.Quiz.Id,f.Flow.UserId,It.IsAny<CancellationToken>())).ReturnsAsync(f.History);
        var error=await Record.ExceptionAsync(()=>f.Submit.HandleAsync(f.Flow.Quiz.Id,attempt.Id,f.Flow.UserId,f.Payload));
        Assert.Equal(accepted,error is null);Assert.Equal(accepted?QuizAttemptStatus.Submitted:QuizAttemptStatus.Expired,attempt.Status);
        f.Attempts.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Once);
    }
    [Fact]
    public async Task Completed_enrollment_denies_start_and_submit_but_keeps_owner_history_results_review()
    {
        var f=new Fixture(EnrollmentStatus.Completed);f.Attempts=Transactions();var attempt=f.Add();
        f.Attempts.Setup(x=>x.GetByQuizAndUserAsync(f.Flow.Quiz.Id,f.Flow.UserId,It.IsAny<CancellationToken>())).ReturnsAsync(f.History);
        var start=new StartQuizAttemptHandler(f.Attempts.Object,f.Flow.Quizzes.Object,f.Flow.QuizReadable,f.Questions.Object,f.Options.Object,f.Clock);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>start.HandleAsync(f.Flow.Quiz.Id,f.Flow.UserId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Submit.HandleAsync(f.Flow.Quiz.Id,attempt.Id,f.Flow.UserId,f.Payload));
        attempt.Submit(f.Clock.Now.UtcDateTime,2,true);
        f.Attempts.Setup(x=>x.GetByIdAndQuizAndUserAsync(attempt.Id,f.Flow.Quiz.Id,f.Flow.UserId,It.IsAny<CancellationToken>())).ReturnsAsync(attempt);
        f.Attempts.Setup(x=>x.GetAnswersAsync(attempt.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<QuizAttemptAnswer>());
        f.Attempts.Setup(x=>x.GetSelectedOptionsAsync(attempt.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<QuizAttemptAnswerOption>());
        Assert.Single(await new GetQuizAttemptsHandler(f.Attempts.Object).HandleAsync(f.Flow.Quiz.Id,f.Flow.UserId));
        Assert.True((await new GetQuizAttemptResultHandler(f.Attempts.Object).HandleAsync(f.Flow.Quiz.Id,attempt.Id,f.Flow.UserId)).IsPassed);
        Assert.Empty((await new GetQuizAttemptAnswersHandler(f.Attempts.Object).HandleAsync(f.Flow.Quiz.Id,attempt.Id,f.Flow.UserId)).Answers);
        var stranger=Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>new GetQuizAttemptResultHandler(f.Attempts.Object).HandleAsync(f.Flow.Quiz.Id,attempt.Id,stranger));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>new GetQuizAttemptAnswersHandler(f.Attempts.Object).HandleAsync(Guid.NewGuid(),attempt.Id,f.Flow.UserId));
        f.Attempts.Verify(x=>x.AddAsync(It.IsAny<QuizAttempt>(),It.IsAny<CancellationToken>()),Times.Never);
        f.Attempts.Verify(x=>x.AddAnswersAsync(It.IsAny<IEnumerable<QuizAttemptAnswer>>(),It.IsAny<IEnumerable<QuizAttemptAnswerOption>>(),It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Student_option_contract_drops_correctness_and_checks_question_parent()
    {
        var f=new Fixture();var handler=new GetStudentOptionsHandler(f.Options.Object,f.Questions.Object,f.Flow.Quizzes.Object,f.Flow.QuizReadable);
        var options=await handler.HandleAsync(f.Flow.Quiz.Id,f.Question.Id,f.Flow.UserId);
        var json=JsonSerializer.Serialize(options);Assert.DoesNotContain("Correct",json);Assert.DoesNotContain("QuestionId",json);Assert.Equal(2,options.Count);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>handler.HandleAsync(f.Flow.Quiz.Id,Guid.NewGuid(),f.Flow.UserId));
        f.Flow.Quiz.Unpublish();await Assert.ThrowsAsync<KeyNotFoundException>(()=>handler.HandleAsync(f.Flow.Quiz.Id,f.Question.Id,f.Flow.UserId));
    }
    [Fact]
    public async Task Real_start_returns_server_deadline_rejects_active_attempt_and_counts_expired_slots()
    {
        var f=new Fixture();f.Attempts=Transactions();
        f.Attempts.Setup(x=>x.GetByQuizAndUserAsync(f.Flow.Quiz.Id,f.Flow.UserId,It.IsAny<CancellationToken>())).ReturnsAsync(f.History);
        f.Attempts.Setup(x=>x.AddAsync(It.IsAny<QuizAttempt>(),It.IsAny<CancellationToken>())).Callback((QuizAttempt attempt,CancellationToken _)=>f.History.Add(attempt)).Returns(Task.CompletedTask);
        var handler=new StartQuizAttemptHandler(f.Attempts.Object,f.Flow.Quizzes.Object,f.Flow.QuizReadable,f.Questions.Object,f.Options.Object,f.Clock);
        var result=await handler.HandleAsync(f.Flow.Quiz.Id,f.Flow.UserId);
        Assert.Equal(f.Clock.Now.UtcDateTime.AddMinutes(f.Flow.Quiz.TimeLimitInMinutes),result.Deadline);Assert.Equal(DateTimeKind.Utc,result.Deadline.Kind);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>handler.HandleAsync(f.Flow.Quiz.Id,f.Flow.UserId));Assert.Single(f.History);
        f.Clock.Now=f.Clock.Now.AddMinutes(11);await handler.HandleAsync(f.Flow.Quiz.Id,f.Flow.UserId);
        Assert.Equal(QuizAttemptStatus.Expired,f.History[0].Status);Assert.Equal(2,f.History.Count);
        f.Clock.Now=f.Clock.Now.AddMinutes(11);await Assert.ThrowsAsync<InvalidOperationException>(()=>handler.HandleAsync(f.Flow.Quiz.Id,f.Flow.UserId));
        Assert.All(f.History,x=>Assert.Equal(QuizAttemptStatus.Expired,x.Status));
    }
    [Theory]
    [InlineData(QuestionType.SingleChoice)] [InlineData(QuestionType.MultipleChoice)] [InlineData(QuestionType.TrueFalse)]
    public async Task Publication_reports_existing_correct_option_validation(QuestionType type)
    {
        var f=new Fixture(type:type);f.Flow.Quiz.Unpublish();f.Attempts=Transactions();
        f.Flow.Quizzes.Setup(x=>x.GetIdByLessonIdAsync(f.Flow.Lesson.Id,It.IsAny<CancellationToken>())).ReturnsAsync(f.Flow.Quiz.Id);
        var ownership=new LMS.Application.Features.Quizzes.Common.EnsureQuizOwnership(f.Flow.Lessons.Object,f.Flow.Quizzes.Object,
            new LMS.Application.Features.Lessons.Common.EnsureLessonOwnership(f.Flow.Sections.Object,f.Flow.Courses.Object));
        var handler=new LMS.Application.Features.Quizzes.PublishQuiz.PublishQuizHandler(f.Flow.Quizzes.Object,f.Questions.Object,f.Options.Object,
            new LMS.Application.Features.Quizzes.Common.EnsureQuizEditable(f.Flow.Quizzes.Object,f.Attempts.Object),ownership);
        f.Options.Setup(x=>x.GetByQuestionIdAsync(f.Question.Id,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{new QuizOption(f.Question.Id,"A",false,1),new QuizOption(f.Question.Id,"B",false,2)});
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>handler.HandleAsync(f.Flow.Lesson.Id,f.Flow.Course.InstructorId,false,true));
        Assert.Contains("correct",error.Message);Assert.False(f.Flow.Quiz.IsPublished);
        f.Flow.Quizzes.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Staff_option_endpoint_blocks_unassigned_instructor_before_reading_answer_keys()
    {
        var f=new Fixture();var actor=Guid.NewGuid();
        var ownership=new LMS.Application.Features.Quizzes.Common.EnsureQuizOwnership(f.Flow.Lessons.Object,f.Flow.Quizzes.Object,
            new LMS.Application.Features.Lessons.Common.EnsureLessonOwnership(f.Flow.Sections.Object,f.Flow.Courses.Object));
        var handler=new LMS.Application.Features.QuizOptions.GetOptions.GetOptionsHandler(f.Options.Object,f.Questions.Object);
        var controller=new LMS.Api.Controllers.QuizQuestionsController(null!,null!,null!,null!,null!,null!,handler,null!,null!,null!);
        controller.ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new(System.Security.Claims.ClaimTypes.NameIdentifier,actor.ToString()),new(System.Security.Claims.ClaimTypes.Role,"Instructor")],"test"))}};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>controller.GetOptions(f.Flow.Quiz.Id,f.Question.Id,default,ownership));
        f.Options.Verify(x=>x.GetByQuestionIdAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>()),Times.Never);
    }
}
