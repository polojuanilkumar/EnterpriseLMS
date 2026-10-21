using LMS.Application.Features.LessonProgress.CompleteLesson;
using LMS.Application.Features.LessonProgress.GetProgress;
using LMS.Application.Features.LessonProgress.StartLesson;
using LMS.Application.Features.LessonProgress.UpdateProgress;
using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Features.Lessons.Common;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class LearnerWriteTests
{
    private static Task Write(LearningFlowFixture f, string operation) => operation switch
    {
        "start" => new StartLessonHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
            .HandleAsync(f.Lesson.Id, f.UserId),
        "update" => new UpdateLessonProgressHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
            .HandleAsync(f.Lesson.Id, new UpdateLessonProgressRequest { ProgressPercentage = 40m }, f.UserId),
        "complete" => new CompleteLessonHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
            .HandleAsync(f.Lesson.Id, f.UserId),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    [Theory]
    [InlineData("start")]
    [InlineData("update")]
    [InlineData("complete")]
    public async Task Active_enrollment_can_write_lesson_progress(string operation)
    {
        var f = new LearningFlowFixture();
        await Write(f, operation);
        f.Progresses.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(operation == "complete", f.Progress.IsCompleted);
        Assert.Equal(operation == "update" ? 40m : operation == "complete" ? 100m : 0m,
            f.Progress.ProgressPercentage);
    }

    [Fact]
    public async Task Starting_a_new_lesson_creates_progress_for_authenticated_owner()
    {
        var f = new LearningFlowFixture();
        f.Progresses.Setup(x => x.GetByUserAndLessonAsync(f.UserId, f.Lesson.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LessonProgress?)null);
        f.Progresses.Setup(x => x.AddAsync(It.IsAny<LessonProgress>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await Write(f, "start");
        f.Progresses.Verify(x => x.AddAsync(It.Is<LessonProgress>(p => p.UserId == f.UserId &&
            p.LessonId == f.Lesson.Id && !p.IsCompleted), It.IsAny<CancellationToken>()), Times.Once);
        f.Progresses.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("start", EnrollmentStatus.Completed)]
    [InlineData("update", EnrollmentStatus.Completed)]
    [InlineData("complete", EnrollmentStatus.Completed)]
    [InlineData("start", EnrollmentStatus.Cancelled)]
    [InlineData("update", EnrollmentStatus.Cancelled)]
    [InlineData("complete", EnrollmentStatus.Cancelled)]
    public async Task Nonactive_enrollment_cannot_write_or_touch_progress(string operation, EnrollmentStatus status)
    {
        var f = new LearningFlowFixture(status);
        var accessedAt = f.Progress.LastAccessedAt;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Write(f, operation));
        Assert.Equal(accessedAt, f.Progress.LastAccessedAt);
        Assert.Equal(0m, f.Progress.ProgressPercentage);
        Assert.False(f.Progress.IsCompleted);
        f.VerifyNoProgressWrites();
    }

    [Theory]
    [InlineData("start", "course")]
    [InlineData("update", "course")]
    [InlineData("complete", "course")]
    [InlineData("start", "lesson")]
    [InlineData("update", "lesson")]
    [InlineData("complete", "lesson")]
    public async Task Unpublished_content_blocks_writes(string operation, string hidden)
    {
        var f = new LearningFlowFixture();
        if (hidden == "course") f.Course.Archive();
        else f.Lesson.Unpublish();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Write(f, operation));
        f.VerifyNoProgressWrites();
    }

    [Theory]
    [InlineData("start", false)]
    [InlineData("update", false)]
    [InlineData("complete", false)]
    [InlineData("read", false)]
    [InlineData("start", true)]
    [InlineData("update", true)]
    [InlineData("complete", true)]
    [InlineData("read", true)]
    public async Task Progress_must_belong_to_authenticated_user_and_requested_lesson(string operation, bool wrongLesson)
    {
        var f = new LearningFlowFixture();
        var unrelated = new LessonProgress(wrongLesson ? f.UserId : Guid.NewGuid(),
            wrongLesson ? Guid.NewGuid() : f.Lesson.Id);
        var accessedAt = unrelated.LastAccessedAt;
        f.Progresses.Setup(x => x.GetByUserAndLessonAsync(f.UserId, f.Lesson.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unrelated);
        Func<Task> action = operation == "read"
            ? () => new GetLessonProgressHandler(f.Lessons.Object, f.Progresses.Object, f.LessonReadable)
                .HandleAsync(f.Lesson.Id, f.UserId)
            : () => Write(f, operation);
        if (wrongLesson) await Assert.ThrowsAsync<KeyNotFoundException>(action);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
        Assert.Equal(accessedAt, unrelated.LastAccessedAt);
        Assert.False(unrelated.IsCompleted);
        f.VerifyNoProgressWrites();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admin")]
    [InlineData("other-instructor")]
    [InlineData("student")]
    public async Task Quiz_content_changes_require_owner_instructor_or_admin(string actor)
    {
        var f = new LearningFlowFixture();
        var ownership = new EnsureQuizOwnership(f.Lessons.Object, f.Quizzes.Object,
            new EnsureLessonOwnership(f.Sections.Object, f.Courses.Object));
        var userId = actor == "owner" ? f.Course.InstructorId : f.UserId;
        Func<Task> action = () => ownership.CheckQuizAsync(f.Quiz.Id, userId,
            isAdmin: actor == "admin", isInstructor: actor is "owner" or "other-instructor");
        if (actor is "owner" or "admin") await action();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
    }
}
