using LMS.Application.Features.CourseProgress.GetProgress;
using LMS.Application.Features.LessonProgress.GetProgress;
using LMS.Application.Features.Lessons.GetLessonById;
using LMS.Application.Features.Quizzes.GetQuiz;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class LearningAccessTests
{
    [Theory]
    [InlineData(EnrollmentStatus.Active)]
    [InlineData(EnrollmentStatus.Completed)]
    public async Task Active_and_completed_learners_can_read_without_mutating_progress(EnrollmentStatus status)
    {
        var f = new LearningFlowFixture(status);
        f.Progress.Complete();
        var accessedAt = f.Progress.LastAccessedAt;
        var completedAt = f.Progress.CompletedAt;

        var lesson = await new GetLessonByIdHandler(f.Lessons.Object, f.LessonReadable)
            .HandleAsync(f.Lesson.Id, f.UserId, false);
        var quiz = await new GetQuizHandler(f.Quizzes.Object, f.QuizReadable)
            .HandleAsync(f.Lesson.Id, f.UserId, false);
        var progress = await new GetLessonProgressHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
            .HandleAsync(f.Lesson.Id, f.UserId);
        var course = await new GetCourseProgressHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
            .HandleAsync(f.Course.Id, f.UserId);

        Assert.Equal(f.Lesson.Id, lesson.Id);
        Assert.Equal(f.Quiz.Id, quiz.Id);
        Assert.Equal(accessedAt, progress.LastAccessedAt);
        Assert.Equal(accessedAt, f.Progress.LastAccessedAt);
        Assert.Equal(completedAt, f.Progress.CompletedAt);
        Assert.Equal(status, course.EnrollmentStatus);
        Assert.Equal(f.Enrollment.CompletedAt, course.EnrollmentCompletedAt);
        Assert.True(course.IsCompleted);
        Assert.Equal(100m, course.ProgressPercentage);
        Assert.Equal(status, f.Enrollment.Status);
        f.VerifyNoProgressWrites();
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_or_missing_enrollment_cannot_read(bool missing)
    {
        var f = new LearningFlowFixture(EnrollmentStatus.Cancelled);
        if (missing)
            f.Enrollments.Setup(x => x.GetByCourseAndUserAsync(f.Course.Id, f.UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((CourseEnrollment?)null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.LessonReadable.CheckAsync(f.Lesson, f.UserId, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.QuizReadable.CheckAsync(f.Quiz, f.UserId, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new GetCourseProgressHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
                .HandleAsync(f.Course.Id, f.UserId));
        f.VerifyNoProgressWrites();
    }

    [Theory]
    [InlineData(EnrollmentStatus.Active, "course")]
    [InlineData(EnrollmentStatus.Completed, "course")]
    [InlineData(EnrollmentStatus.Active, "lesson")]
    [InlineData(EnrollmentStatus.Completed, "lesson")]
    [InlineData(EnrollmentStatus.Active, "quiz")]
    [InlineData(EnrollmentStatus.Completed, "quiz")]
    public async Task Learner_quiz_reads_require_the_entire_publication_chain(EnrollmentStatus status, string hidden)
    {
        var f = new LearningFlowFixture(status);
        if (hidden == "course") f.Course.Archive();
        if (hidden == "lesson") f.Lesson.Unpublish();
        if (hidden == "quiz") f.Quiz.Unpublish();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new GetQuizHandler(f.Quizzes.Object, f.QuizReadable).HandleAsync(f.Lesson.Id, f.UserId, false));
        f.VerifyNoProgressWrites();
    }

    [Fact]
    public async Task Staff_read_bypass_does_not_grant_learner_mutation_access()
    {
        var f = new LearningFlowFixture(EnrollmentStatus.Completed);
        f.Lesson.Unpublish();
        f.Quiz.Unpublish();
        var organization = new LMS.Application.Features.Organizations.OrganizationContext();
        var org = Guid.NewGuid();
        organization.Initialize(org, f.UserId, OrganizationRole.Admin, false);
        f.Course.AssignOrganization(org);
        var staffReadable = new LMS.Application.Features.Quizzes.Common.EnsureQuizReadable(f.Lessons.Object,
            new LMS.Application.Features.Lessons.Common.EnsureLessonReadable(f.Courses.Object, f.Sections.Object, f.Enrollments.Object, organization));
        await staffReadable.CheckAsync(f.Quiz, f.UserId, true);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.QuizReadable.CheckForMutationAsync(f.Quiz, f.UserId));
        f.Lesson.Publish();
        f.Quiz.Publish();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.QuizReadable.CheckForMutationAsync(f.Quiz, f.UserId));
    }

    [Fact]
    public async Task Anonymous_learner_cannot_read_published_content()
    {
        var f = new LearningFlowFixture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.LessonReadable.CheckAsync(f.Lesson, Guid.Empty, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.QuizReadable.CheckAsync(f.Quiz, Guid.Empty, false));
    }

    [Fact]
    public async Task Course_progress_counts_only_distinct_completed_published_lessons_for_this_user()
    {
        var f = new LearningFlowFixture();
        var second = f.NewLesson();
        var draft = f.NewLesson(false);
        f.SetLessons(f.Lesson, second, draft);
        f.Progress.Complete();
        var duplicate = new LessonProgress(f.UserId, f.Lesson.Id);
        duplicate.Complete();
        var otherUser = new LessonProgress(Guid.NewGuid(), second.Id);
        otherUser.Complete();
        var unpublished = new LessonProgress(f.UserId, draft.Id);
        unpublished.Complete();
        f.SetProgress(f.Progress, duplicate, otherUser, unpublished);

        var result = await new GetCourseProgressHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
            .HandleAsync(f.Course.Id, f.UserId);

        Assert.Equal(2, result.TotalLessons);
        Assert.Equal(1, result.CompletedLessons);
        Assert.Equal(50m, result.ProgressPercentage);
        Assert.False(result.IsCompleted);
        f.VerifyNoProgressWrites();
    }

    [Fact]
    public async Task Course_without_published_lessons_is_not_reported_complete()
    {
        var f = new LearningFlowFixture();
        f.Lesson.Unpublish();
        var result = await new GetCourseProgressHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
            .HandleAsync(f.Course.Id, f.UserId);
        Assert.Equal(0, result.TotalLessons);
        Assert.Equal(0m, result.ProgressPercentage);
        Assert.False(result.IsCompleted);
        f.Progresses.Verify(x => x.GetByUserAndLessonIdsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
