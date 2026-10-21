using LMS.Application.Features.CourseProgress.MyCourses;
using LMS.Application.Features.CourseSections.GetCourseSectionById;
using LMS.Application.Features.Enrollments.EnrollCourse;
using LMS.Application.Features.LessonProgress.StartLesson;
using LMS.Application.Features.Lessons.Common;
using LMS.Application.Features.Lessons.PublishLesson;
using LMS.Application.Features.QuizQuestions.GetQuestionById;
using LMS.Application.Features.QuizAttempts.StartQuizAttempt;
using LMS.Application.Features.QuizAttempts.SubmitQuizAttempt;
using LMS.Application.Features.QuizAttempts.GetQuizAttempts;
using LMS.Application.Features.QuizAttempts.GetQuizAttemptResult;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizAttempts;
using LMS.Domain.Entities;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class TenantHandlerTests
{
    private static LearningFlowFixture Bind(TenantGraph graph)
    {
        var f = graph.First;
        f.Courses.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => graph.Visible<Course>().SingleOrDefault(x => x.Id == id));
        f.Sections.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => graph.Visible<CourseSection>().SingleOrDefault(x => x.Id == id));
        f.Lessons.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => graph.Visible<Lesson>().SingleOrDefault(x => x.Id == id));
        f.Quizzes.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => graph.Visible<Quiz>().SingleOrDefault(x => x.Id == id));
        return f;
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Foreign_lesson_mutation_is_404_even_for_organization_admin(bool admin)
    {
        using var graph = new TenantGraph(); var f = Bind(graph);
        var handler = new PublishLessonHandler(f.Lessons.Object,
            new EnsureLessonOwnership(f.Sections.Object, f.Courses.Object), f.UnitOfWork.Object);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(graph.Second.Lesson.Id, f.Course.InstructorId, admin));
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("owner")][InlineData("other-instructor")][InlineData("admin")]
    public async Task Selected_organization_still_requires_instructor_ownership_for_mutation(string actor)
    {
        using var graph = new TenantGraph(); var f = Bind(graph); f.Lesson.Unpublish();
        var handler = new PublishLessonHandler(f.Lessons.Object,
            new EnsureLessonOwnership(f.Sections.Object, f.Courses.Object), f.UnitOfWork.Object);
        Func<Task> act = () => handler.HandleAsync(f.Lesson.Id, actor == "owner" ? f.Course.InstructorId : f.UserId, actor == "admin");
        if (actor == "other-instructor")
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
            Assert.False(f.Lesson.IsPublished);
        }
        else { await act(); Assert.True(f.Lesson.IsPublished); }
    }

    [Fact]
    public async Task Nested_section_and_question_ids_cannot_cross_parent_routes()
    {
        using var graph = new TenantGraph(); var f = Bind(graph);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new GetCourseSectionByIdHandler(f.Sections.Object, f.Courses.Object, f.LessonReadable)
            .HandleAsync(f.Course.Id, graph.Second.Section.Id, f.UserId, true));
        var question = Assert.Single(graph.Visible<QuizQuestion>());
        var questions = new Mock<IQuizQuestionRepository>(MockBehavior.Strict);
        questions.Setup(x => x.GetByIdAsync(question.Id, It.IsAny<CancellationToken>())).ReturnsAsync(question);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new GetQuestionByIdHandler(questions.Object, f.Quizzes.Object, f.QuizReadable)
            .HandleAsync(graph.Second.Quiz.Id, question.Id, f.UserId, true));
        f.Quizzes.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Foreign_enrollment_and_progress_creation_fail_before_saving()
    {
        using var graph = new TenantGraph(); var f = Bind(graph);
        var identity = new Mock<IIdentityService>(MockBehavior.Strict);
        identity.Setup(x => x.IsInRoleAsync(f.UserId, "Student")).ReturnsAsync(true);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new EnrollCourseHandler(f.Enrollments.Object, f.Courses.Object, identity.Object, f.UnitOfWork.Object)
            .HandleAsync(new EnrollCourseRequest { CourseId = graph.Second.Course.Id }, f.UserId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new StartLessonHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
            .HandleAsync(graph.Second.Lesson.Id, f.UserId));
        f.VerifyNoProgressWrites();
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task My_courses_uses_authenticated_ID_and_only_published_owned_progress()
    {
        using var graph = new TenantGraph(); var f = Bind(graph);
        f.Enrollments.Setup(x => x.GetByUserIdAsync(f.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => graph.Visible<CourseEnrollment>().Where(x => x.UserId == f.UserId).ToList());
        var hidden = f.NewLesson(false); f.SetLessons(f.Lesson, hidden);
        f.Progress.Complete();
        var hiddenProgress = new LessonProgress(f.UserId, hidden.Id); hiddenProgress.Complete();
        var otherUser = new LessonProgress(Guid.NewGuid(), f.Lesson.Id); otherUser.Complete();
        f.SetProgress(f.Progress, f.Progress, hiddenProgress, otherUser);
        var handler = new GetMyCoursesProgressHandler(f.Enrollments.Object, f.Courses.Object, f.Lessons.Object, f.Progresses.Object);
        var result = Assert.Single(await handler.HandleAsync(f.UserId));
        Assert.Equal(f.Course.Id, result.CourseId); Assert.Equal(1, result.TotalLessons); Assert.Equal(1, result.CompletedLessons);
        graph.Selected = graph.B;
        Assert.Empty(await handler.HandleAsync(f.UserId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.HandleAsync(Guid.Empty));
    }

    [Theory]
    [InlineData("start")][InlineData("submit")][InlineData("history")][InlineData("result")]
    public async Task Foreign_quiz_attempt_routes_do_not_expose_or_modify_attempts(string operation)
    {
        using var graph = new TenantGraph(); var f = Bind(graph);
        graph.Selected = graph.B; var foreignAttempt = Assert.Single(graph.Visible<QuizAttempt>()); graph.Selected = graph.A;
        var attempts = new OfflineAttempts(graph);
        var questions = new Mock<IQuizQuestionRepository>(MockBehavior.Strict);
        var options = new Mock<IQuizOptionRepository>(MockBehavior.Strict);
        if (operation == "history")
            Assert.Empty(await new GetQuizAttemptsHandler(attempts).HandleAsync(graph.Second.Quiz.Id, foreignAttempt.UserId));
        else
        {
            Func<Task> act = operation switch
            {
                "start" => () => new StartQuizAttemptHandler(attempts, f.Quizzes.Object, f.QuizReadable, questions.Object, options.Object, TimeProvider.System)
                    .HandleAsync(graph.Second.Quiz.Id, foreignAttempt.UserId),
                "submit" => () => new SubmitQuizAttemptHandler(attempts, f.Quizzes.Object, questions.Object, options.Object, TimeProvider.System, f.QuizReadable)
                    .HandleAsync(graph.Second.Quiz.Id, foreignAttempt.Id, foreignAttempt.UserId, new()),
                _ => () => new GetQuizAttemptResultHandler(attempts).HandleAsync(f.Quiz.Id, foreignAttempt.Id, foreignAttempt.UserId)
            };
            await Assert.ThrowsAsync<KeyNotFoundException>(act);
        }
        Assert.Equal(0, attempts.Saves);
        Assert.Equal(LMS.Domain.Enums.QuizAttemptStatus.Started, foreignAttempt.Status);
    }

    [Theory]
    [InlineData("success")][InlineData("foreign-question")][InlineData("foreign-option")][InlineData("other-owner")]
    public async Task Submission_requires_owned_attempt_and_questions_options_from_its_quiz(string scenario)
    {
        using var graph = new TenantGraph(); var f = Bind(graph);
        var attempt = Assert.Single(graph.Visible<QuizAttempt>());
        var question = Assert.Single(graph.Visible<QuizQuestion>());
        var correct = Assert.Single(graph.Visible<QuizOption>());
        graph.Selected = graph.B;
        var foreignQuestion = Assert.Single(graph.Visible<QuizQuestion>());
        var foreignOption = Assert.Single(graph.Visible<QuizOption>());
        graph.Selected = graph.A;
        var questions = new Mock<IQuizQuestionRepository>(MockBehavior.Strict);
        questions.Setup(x => x.GetByQuizIdAsync(f.Quiz.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { question });
        var options = new Mock<IQuizOptionRepository>(MockBehavior.Strict);
        options.Setup(x => x.GetByQuestionIdAsync(question.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { correct, new QuizOption(question.Id, "Wrong", false, 2) });
        var attempts = new OfflineAttempts(graph);
        var handler = new SubmitQuizAttemptHandler(attempts, f.Quizzes.Object, questions.Object, options.Object, TimeProvider.System, f.QuizReadable);
        Func<Task> act = () => handler.HandleAsync(f.Quiz.Id, attempt.Id, scenario == "other-owner" ? Guid.NewGuid() : f.UserId,
            new() { Answers = [new() { QuestionId = scenario == "foreign-question" ? foreignQuestion.Id : question.Id,
                SelectedOptionIds = [scenario == "foreign-option" ? foreignOption.Id : correct.Id] }] });
        if (scenario == "success")
        {
            await act(); Assert.Equal(1, attempts.Saves); Assert.True(attempt.IsPassed);
            Assert.Equal(attempt.Id, Assert.Single(await new GetQuizAttemptsHandler(attempts).HandleAsync(f.Quiz.Id, f.UserId)).AttemptId);
        }
        else
        {
            if (scenario == "other-owner") await Assert.ThrowsAsync<KeyNotFoundException>(act);
            else await Assert.ThrowsAsync<ArgumentException>(act);
            Assert.Equal(0, attempts.Saves); Assert.Null(attempt.SubmittedAt);
        }
    }

    [Fact]
    public async Task Starting_owned_published_quiz_preserves_active_enrollment_and_attempt_limits()
    {
        using var graph = new TenantGraph(); var f = Bind(graph);
        var attempts = new OfflineAttempts(graph);
        var handler = new StartQuizAttemptHandler(attempts, f.Quizzes.Object, f.QuizReadable,
            Mock.Of<IQuizQuestionRepository>(), Mock.Of<IQuizOptionRepository>(), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(f.Quiz.Id, f.UserId));
        f.Enrollment.Complete();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.HandleAsync(f.Quiz.Id, f.UserId));
        Assert.Equal(0, attempts.Saves);
    }

    // Executes callbacks, NOT SQL transactions. The repository SQL and lock composition
    // have separate offline tests; this fake cannot establish locking/rollback behavior.
    private sealed class OfflineAttempts(TenantGraph graph) : IQuizAttemptRepository
    {
        public int Saves { get; private set; }
        public async Task<T> ExecuteTransactionAsync<T>(Guid id, Func<CancellationToken, Task<T>> action, CancellationToken token = default)
        {
            if (!graph.Visible<Quiz>().Any(x => x.Id == id)) throw new KeyNotFoundException();
            return await action(token);
        }
        public Task<IReadOnlyList<QuizAttempt>> GetByQuizAndUserAsync(Guid quiz, Guid user, CancellationToken token = default)
            => Task.FromResult<IReadOnlyList<QuizAttempt>>(graph.Visible<QuizAttempt>().Where(x => x.QuizId == quiz && x.UserId == user).ToList());
        public async Task<QuizAttempt?> GetByIdAndQuizAndUserAsync(Guid id, Guid quiz, Guid user, CancellationToken token = default)
            => (await GetByQuizAndUserAsync(quiz, user, token)).SingleOrDefault(x => x.Id == id);
        public Task SaveChangesAsync(CancellationToken token = default) { Saves++; return Task.CompletedTask; }
        public Task AddAsync(QuizAttempt attempt, CancellationToken token = default) => throw new NotSupportedException();
        public Task AddAnswersAsync(IEnumerable<QuizAttemptAnswer> answers, IEnumerable<QuizAttemptAnswerOption> options, CancellationToken token = default) => Task.CompletedTask;
        public Task<bool> HasAttemptsAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<T> ExecuteDeletionTransactionAsync<T>(Guid id, bool section, Func<IReadOnlyList<Guid>, CancellationToken, Task<T>> action, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<QuizAttemptAnswer>> GetAnswersAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<QuizAttemptAnswerOption>> GetSelectedOptionsAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
    }
}
