using System.Text.Json;
using LMS.Application.Features.CourseProgress.GetProgress;
using LMS.Application.Features.Enrollments.EnrollCourse;
using LMS.Application.Features.LessonProgress.GetProgress;
using LMS.Application.Features.LessonProgress.CompleteLesson;
using LMS.Application.Features.LessonProgress.StartLesson;
using LMS.Application.Features.LessonProgress.UpdateProgress;
using LMS.Application.Interfaces.Identity;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Web.Models;
using Moq;
using Xunit;
namespace LMS.Tests;

public sealed class LearnerWorkflowPolicyTests
{
    private static CurrentCourseProgress WebProgress(LMS.Application.Features.CourseProgress.Common.CourseProgressResponse response)
        =>JsonSerializer.Deserialize<CurrentCourseProgress>(JsonSerializer.Serialize(response))!;
    [Theory]
    [InlineData(EnrollmentStatus.Active)] [InlineData(EnrollmentStatus.Completed)] [InlineData(EnrollmentStatus.Cancelled)]
    public async Task Existing_enrollment_operation_reuses_cancelled_record_and_conflicts_for_other_states(EnrollmentStatus status)
    {
        var f=new LearningFlowFixture(status);var identity=new Mock<IIdentityService>();identity.Setup(x=>x.IsInRoleAsync(f.UserId,"Student")).ReturnsAsync(true);
        f.Enrollments.Setup(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var handler=new EnrollCourseHandler(f.Enrollments.Object,f.Courses.Object,identity.Object,f.UnitOfWork.Object);
        var id=f.Enrollment.Id;var enrolled=f.Enrollment.EnrolledAt;
        if(status==EnrollmentStatus.Cancelled)
        {
            Assert.Equal(id,await handler.HandleAsync(new(){CourseId=f.Course.Id},f.UserId));
            Assert.Equal(EnrollmentStatus.Active,f.Enrollment.Status);Assert.Equal(enrolled,f.Enrollment.EnrolledAt);
            f.Enrollments.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Once);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>handler.HandleAsync(new(){CourseId=f.Course.Id},f.UserId));
            Assert.Equal(status,f.Enrollment.Status);f.Enrollments.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Never);
        }
        Assert.Equal(id,f.Enrollment.Id);f.Enrollments.Verify(x=>x.AddAsync(It.IsAny<CourseEnrollment>(),It.IsAny<CancellationToken>()),Times.Never);
        f.UnitOfWork.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Fresh_enrollment_uses_authenticated_owner_and_does_not_start_progress()
    {
        var f=new LearningFlowFixture();var identity=new Mock<IIdentityService>();identity.Setup(x=>x.IsInRoleAsync(f.UserId,"Student")).ReturnsAsync(true);
        f.Enrollments.Setup(x=>x.GetByCourseAndUserAsync(f.Course.Id,f.UserId,It.IsAny<CancellationToken>())).ReturnsAsync((CourseEnrollment?)null);
        f.Enrollments.Setup(x=>x.AddAsync(It.IsAny<CourseEnrollment>(),It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await new EnrollCourseHandler(f.Enrollments.Object,f.Courses.Object,identity.Object,f.UnitOfWork.Object).HandleAsync(new(){CourseId=f.Course.Id},f.UserId);
        f.Enrollments.Verify(x=>x.AddAsync(It.Is<CourseEnrollment>(e=>e.UserId==f.UserId&&e.CourseId==f.Course.Id&&e.Status==EnrollmentStatus.Active),It.IsAny<CancellationToken>()),Times.Once);
        f.VerifyNoProgressWrites();
    }
    [Fact]
    public async Task Explicit_start_then_complete_needs_no_percentage_and_keeps_enrollment_active_until_separate_completion()
    {
        var f=new LearningFlowFixture();
        var started=await new StartLessonHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable).HandleAsync(f.Lesson.Id,f.UserId);
        Assert.False(started.IsCompleted);Assert.Equal(0,started.ProgressPercentage);
        var completed=await new CompleteLessonHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable).HandleAsync(f.Lesson.Id,f.UserId);
        Assert.True(completed.IsCompleted);Assert.Equal(100,completed.ProgressPercentage);Assert.NotNull(completed.CompletedAt);
        var current=await new GetCourseProgressHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable).HandleAsync(f.Course.Id,f.UserId);
        Assert.Equal(1,current.TotalLessons);Assert.Equal(1,current.CompletedLessons);Assert.Equal(100,current.ProgressPercentage);Assert.True(current.IsCompleted);
        Assert.Equal(EnrollmentStatus.Active,f.Enrollment.Status);Assert.Null(f.Enrollment.CompletedAt);
        f.UnitOfWork.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Never);
        f.Quizzes.Verify(x=>x.GetByLessonIdAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Explicit_update_to_100_completes_lesson_but_course_enrollment_requires_separate_confirmation_api()
    {
        var f=new LearningFlowFixture();
        await new UpdateLessonProgressHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable).HandleAsync(f.Lesson.Id,new(){ProgressPercentage=100},f.UserId);
        Assert.True(f.Progress.IsCompleted);Assert.Equal(EnrollmentStatus.Active,f.Enrollment.Status);Assert.Null(f.Enrollment.CompletedAt);
        var handler=new GetCourseProgressHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable);
        var current=WebProgress(await handler.HandleAsync(f.Course.Id,f.UserId));Assert.True(current.CanComplete);Assert.Equal(100,current.ProgressPercentage);
        await f.Completion.HandleAsync(f.Enrollment.Id,f.UserId);
        current=WebProgress(await handler.HandleAsync(f.Course.Id,f.UserId));Assert.False(current.CanComplete);Assert.Equal(2,current.EnrollmentStatus);Assert.NotNull(current.EnrollmentCompletedAt);
        var stamp=current.EnrollmentCompletedAt;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Completion.HandleAsync(f.Enrollment.Id,f.UserId));Assert.Equal(stamp,f.Enrollment.CompletedAt);
        f.Quizzes.Verify(x=>x.GetByLessonIdAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>()),Times.Never);
        f.UnitOfWork.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Once);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Current_eligibility_and_guarded_completion_agree_for_zero_or_incomplete_published_lessons(bool empty)
    {
        var f=new LearningFlowFixture();if(empty)f.SetLessons(f.NewLesson(false));
        var current=WebProgress(await new GetCourseProgressHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable).HandleAsync(f.Course.Id,f.UserId));
        Assert.False(current.CanComplete);Assert.Equal(empty?0:1,current.TotalLessons);Assert.Equal(0,current.ProgressPercentage);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Completion.HandleAsync(f.Enrollment.Id,f.UserId));
        Assert.Equal(EnrollmentStatus.Active,f.Enrollment.Status);Assert.Null(f.Enrollment.CompletedAt);f.UnitOfWork.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Never);
    }
    [Theory]
    [InlineData(EnrollmentStatus.Active)] [InlineData(EnrollmentStatus.Completed)]
    public async Task Repeated_gets_preserve_all_stored_lesson_and_enrollment_fields(EnrollmentStatus status)
    {
        var f=new LearningFlowFixture(status);f.Progress.UpdateProgress(40);
        var lessonBefore=(f.Progress.LastAccessedAt,f.Progress.StartedAt,f.Progress.CompletedAt,f.Progress.ProgressPercentage,f.Progress.IsCompleted);
        var enrollmentBefore=(f.Enrollment.Status,f.Enrollment.CompletedAt,f.Enrollment.ProgressPercentage,f.Enrollment.EnrolledAt);
        var read=new GetLessonProgressHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable);
        var courseRead=new GetCourseProgressHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable);
        for(var i=0;i<2;i++){await read.HandleAsync(f.Lesson.Id,f.UserId);await courseRead.HandleAsync(f.Course.Id,f.UserId);}
        Assert.Equal(lessonBefore,(f.Progress.LastAccessedAt,f.Progress.StartedAt,f.Progress.CompletedAt,f.Progress.ProgressPercentage,f.Progress.IsCompleted));
        Assert.Equal(enrollmentBefore,(f.Enrollment.Status,f.Enrollment.CompletedAt,f.Enrollment.ProgressPercentage,f.Enrollment.EnrolledAt));f.VerifyNoProgressWrites();
        f.UnitOfWork.Verify(x=>x.SaveChangesAsync(It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Historical_completed_enrollment_does_not_assert_current_published_content_is_complete()
    {
        var f=new LearningFlowFixture(EnrollmentStatus.Completed);var historical=f.Enrollment.CompletedAt;
        f.Progress.Complete();f.SetLessons(f.Lesson,f.NewLesson(),f.NewLesson(false));
        var handler=new GetCourseProgressHandler(f.Lessons.Object,f.Progresses.Object,f.LessonReadable);
        var current=WebProgress(await handler.HandleAsync(f.Course.Id,f.UserId));Assert.Equal(50,current.ProgressPercentage);Assert.Equal(2,current.TotalLessons);Assert.Equal(1,current.CompletedLessons);
        Assert.False(current.IsCompleted);Assert.Equal(2,current.EnrollmentStatus);Assert.Equal(historical,current.EnrollmentCompletedAt);
        f.SetLessons();current=WebProgress(await handler.HandleAsync(f.Course.Id,f.UserId));Assert.Equal(0,current.ProgressPercentage);Assert.False(current.CanComplete);Assert.Equal(historical,current.EnrollmentCompletedAt);
        f.VerifyNoProgressWrites();
    }
}
