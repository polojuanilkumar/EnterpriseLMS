using LMS.Application.Features.Enrollments.CancelEnrollment;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class EnrollmentCancellationTests
{
    private static CancelEnrollmentHandler Handler(LearningFlowFixture f)
        => new(f.Enrollments.Object, f.UnitOfWork.Object);

    [Fact]
    public async Task Owner_can_cancel_active_enrollment_and_repeat_is_rejected()
    {
        var f = new LearningFlowFixture();
        f.Enrollment.UpdateProgress(40m);
        await Handler(f).HandleAsync(f.Enrollment.Id, f.UserId);
        Assert.Equal(EnrollmentStatus.Cancelled, f.Enrollment.Status);
        Assert.Equal(40m, f.Enrollment.ProgressPercentage);
        Assert.Null(f.Enrollment.CompletedAt);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(f).HandleAsync(f.Enrollment.Id, f.UserId));
        Assert.Equal("Only active enrollments can be cancelled.", error.Message);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Completed)]
    [InlineData(EnrollmentStatus.Cancelled)]
    public async Task Cancellation_rejects_nonactive_enrollment_without_saving_or_changing_fields(EnrollmentStatus status)
    {
        var f = new LearningFlowFixture(status);
        var completedAt = f.Enrollment.CompletedAt;
        var progress = f.Enrollment.ProgressPercentage;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(f).HandleAsync(f.Enrollment.Id, f.UserId));
        Assert.Equal(status, f.Enrollment.Status);
        Assert.Equal(completedAt, f.Enrollment.CompletedAt);
        Assert.Equal(progress, f.Enrollment.ProgressPercentage);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nonowner_or_empty_user_cannot_cancel(bool emptyUser)
    {
        var f = new LearningFlowFixture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Handler(f).HandleAsync(f.Enrollment.Id, emptyUser ? Guid.Empty : Guid.NewGuid()));
        Assert.Equal(EnrollmentStatus.Active, f.Enrollment.Status);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_enrollment_returns_not_found_without_saving()
    {
        var f = new LearningFlowFixture();
        f.Enrollments.Setup(x => x.GetByIdAsync(f.Enrollment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CourseEnrollment?)null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Handler(f).HandleAsync(f.Enrollment.Id, f.UserId));
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_handlers_load_and_save_only_inside_the_shared_transaction_callback(bool complete)
    {
        var f = new LearningFlowFixture();
        f.Progress.Complete();
        using var source = new CancellationTokenSource();
        var token = source.Token;
        var insideTransaction = false;
        f.Enrollments.Setup(x => x.ExecuteEnrollmentTransactionAsync(f.Enrollment.Id,
                It.IsAny<Func<CancellationToken, Task>>(), token))
            .Returns(async (Guid _, Func<CancellationToken, Task> action, CancellationToken ct) =>
            {
                insideTransaction = true;
                try { await action(ct); }
                finally { insideTransaction = false; }
            });
        f.Enrollments.Setup(x => x.GetByIdAsync(f.Enrollment.Id, token))
            .Callback(() => Assert.True(insideTransaction))
            .ReturnsAsync(f.Enrollment);
        f.UnitOfWork.Setup(x => x.SaveChangesAsync(token))
            .Callback(() => Assert.True(insideTransaction))
            .ReturnsAsync(1);

        if (complete) await f.Completion.HandleAsync(f.Enrollment.Id, f.UserId, token);
        else await Handler(f).HandleAsync(f.Enrollment.Id, f.UserId, token);

        Assert.False(insideTransaction);
        f.Enrollments.Verify(x => x.ExecuteEnrollmentTransactionAsync(f.Enrollment.Id,
            It.IsAny<Func<CancellationToken, Task>>(), token), Times.Once);
        f.Enrollments.Verify(x => x.GetByIdAsync(f.Enrollment.Id, token), Times.Once);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(token), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_transition_wins_and_second_is_rejected_in_either_serial_order(bool completeFirst)
    {
        var f = new LearningFlowFixture();
        f.Progress.Complete();
        if (completeFirst) await f.Completion.HandleAsync(f.Enrollment.Id, f.UserId);
        else await Handler(f).HandleAsync(f.Enrollment.Id, f.UserId);
        var completedAt = f.Enrollment.CompletedAt;
        var progress = f.Enrollment.ProgressPercentage;

        if (completeFirst)
            await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(f).HandleAsync(f.Enrollment.Id, f.UserId));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Completion.HandleAsync(f.Enrollment.Id, f.UserId));

        Assert.Equal(completeFirst ? EnrollmentStatus.Completed : EnrollmentStatus.Cancelled, f.Enrollment.Status);
        Assert.Equal(completedAt, f.Enrollment.CompletedAt);
        Assert.Equal(progress, f.Enrollment.ProgressPercentage);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancellation_checks_status_after_entering_the_transaction()
    {
        var f = new LearningFlowFixture();
        DateTime? completedAt = null;
        // Model a completion committed before the cancellation callback begins.
        // This verifies handler ordering, not SQL lock behavior.
        f.Enrollments.Setup(x => x.ExecuteEnrollmentTransactionAsync(f.Enrollment.Id,
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Func<CancellationToken, Task> action, CancellationToken token) =>
            {
                f.Enrollment.Complete();
                completedAt = f.Enrollment.CompletedAt;
                return action(token);
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(f).HandleAsync(f.Enrollment.Id, f.UserId));
        Assert.Equal(EnrollmentStatus.Completed, f.Enrollment.Status);
        Assert.NotNull(completedAt);
        Assert.Equal(completedAt, f.Enrollment.CompletedAt);
        Assert.Equal(100m, f.Enrollment.ProgressPercentage);
        f.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Active)]
    [InlineData(EnrollmentStatus.Completed)]
    [InlineData(EnrollmentStatus.Cancelled)]
    public void Domain_cancel_allows_only_active_and_preserves_other_fields(EnrollmentStatus status)
    {
        var f = new LearningFlowFixture(status);
        var completedAt = f.Enrollment.CompletedAt;
        var progress = f.Enrollment.ProgressPercentage;
        if (status == EnrollmentStatus.Active)
        {
            f.Enrollment.Cancel();
            Assert.Equal(EnrollmentStatus.Cancelled, f.Enrollment.Status);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => f.Enrollment.Cancel());
            Assert.Equal(status, f.Enrollment.Status);
        }
        Assert.Equal(completedAt, f.Enrollment.CompletedAt);
        Assert.Equal(progress, f.Enrollment.ProgressPercentage);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Active)]
    [InlineData(EnrollmentStatus.Completed)]
    [InlineData(EnrollmentStatus.Cancelled)]
    public void Reactivation_allows_only_cancelled_and_cannot_erase_completed_timestamp(EnrollmentStatus status)
    {
        var f = new LearningFlowFixture(status);
        var completedAt = f.Enrollment.CompletedAt;
        if (status == EnrollmentStatus.Cancelled)
        {
            f.Enrollment.Reactivate();
            Assert.Equal(EnrollmentStatus.Active, f.Enrollment.Status);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => f.Enrollment.Reactivate());
            Assert.Equal(status, f.Enrollment.Status);
        }
        Assert.Equal(completedAt, f.Enrollment.CompletedAt);
    }
}
