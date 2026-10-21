using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class CourseCompletionTests
{
    [Fact]
    public async Task All_published_lessons_complete_allows_completion_and_repeat_is_a_conflict()
    {
        var f = new LearningFlowFixture();
        var second = f.NewLesson();
        var draft = f.NewLesson(false);
        f.SetLessons(f.Lesson, second, draft);
        f.Progress.Complete();
        var secondProgress = new LessonProgress(f.UserId, second.Id);
        secondProgress.Complete();
        f.SetProgress(f.Progress, secondProgress);

        await f.Completion.HandleAsync(f.Enrollment.Id, f.UserId);

        Assert.Equal(EnrollmentStatus.Completed, f.Enrollment.Status);
        Assert.Equal(100m, f.Enrollment.ProgressPercentage);
        Assert.NotNull(f.Enrollment.CompletedAt);
        var completedAt = f.Enrollment.CompletedAt;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Completion.HandleAsync(f.Enrollment.Id, f.UserId));
        Assert.Equal("Only active enrollments can be completed.", error.Message);
        Assert.Equal(completedAt, f.Enrollment.CompletedAt);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        f.Progresses.Verify(x => x.GetByUserAndLessonIdsAsync(f.UserId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2 && ids.Contains(f.Lesson.Id) && ids.Contains(second.Id)),
            It.IsAny<CancellationToken>()), Times.Once);
        f.Enrollments.Verify(x => x.ExecuteEnrollmentTransactionAsync(f.Enrollment.Id,
            It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("empty-course")]
    [InlineData("draft-only")]
    [InlineData("missing-progress")]
    [InlineData("partial-progress")]
    [InlineData("other-user")]
    [InlineData("other-lesson")]
    [InlineData("one-of-two")]
    public async Task Incomplete_prerequisites_leave_enrollment_active_and_unsaved(string scenario)
    {
        var f = new LearningFlowFixture();
        switch (scenario)
        {
            case "empty-course": f.SetLessons(); break;
            case "draft-only": f.Lesson.Unpublish(); break;
            case "missing-progress": f.SetProgress(); break;
            case "partial-progress": f.Progress.UpdateProgress(99m); break;
            case "other-user":
            case "other-lesson":
                var unrelated = new LessonProgress(scenario == "other-user" ? Guid.NewGuid() : f.UserId,
                    scenario == "other-lesson" ? Guid.NewGuid() : f.Lesson.Id);
                unrelated.Complete();
                f.SetProgress(unrelated);
                break;
            case "one-of-two":
                f.Progress.Complete();
                f.SetLessons(f.Lesson, f.NewLesson());
                break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Completion.HandleAsync(f.Enrollment.Id, f.UserId));
        Assert.Equal(EnrollmentStatus.Active, f.Enrollment.Status);
        Assert.Null(f.Enrollment.CompletedAt);
        Assert.Equal(0m, f.Enrollment.ProgressPercentage);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_authenticated_enrollment_owner_can_complete(bool anonymous)
    {
        var f = new LearningFlowFixture();
        f.Progress.Complete();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            f.Completion.HandleAsync(f.Enrollment.Id, anonymous ? Guid.Empty : Guid.NewGuid()));
        Assert.Equal(EnrollmentStatus.Active, f.Enrollment.Status);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("enrollment")]
    [InlineData("course")]
    [InlineData("unpublished-course")]
    public async Task Missing_or_unpublished_resources_cannot_be_completed(string missing)
    {
        var f = new LearningFlowFixture();
        f.Progress.Complete();
        if (missing == "enrollment")
            f.Enrollments.Setup(x => x.GetByIdAsync(f.Enrollment.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((CourseEnrollment?)null);
        else if (missing == "course")
            f.Courses.Setup(x => x.GetByIdAsync(f.Course.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Course?)null);
        else f.Course.Archive();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Completion.HandleAsync(f.Enrollment.Id, f.UserId));
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Cancelled)]
    [InlineData(EnrollmentStatus.Completed)]
    public async Task Nonactive_enrollments_cannot_complete(EnrollmentStatus status)
    {
        var f = new LearningFlowFixture(status);
        f.Progress.Complete();
        var completedAt = f.Enrollment.CompletedAt;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Completion.HandleAsync(f.Enrollment.Id, f.UserId));
        Assert.Equal(status, f.Enrollment.Status);
        Assert.Equal(completedAt, f.Enrollment.CompletedAt);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
