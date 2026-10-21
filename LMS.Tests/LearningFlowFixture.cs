using LMS.Application.Features.Enrollments.CompleteEnrollment;
using LMS.Application.Features.Lessons.Common;
using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.LessonProgresses;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using LMS.Application.Interfaces.Quizzes;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;

namespace LMS.Tests;

internal sealed class LearningFlowFixture
{
    public Guid UserId { get; } = Guid.NewGuid();
    public Course Course { get; } = new("Course", "TEST", "Description", Guid.NewGuid(), CourseLevel.Beginner);
    public CourseSection Section { get; }
    public Lesson Lesson { get; }
    public Quiz Quiz { get; }
    public CourseEnrollment Enrollment { get; }
    public LessonProgress Progress { get; }
    public Mock<ICourseRepository> Courses { get; } = new(MockBehavior.Strict);
    public Mock<ICourseSectionRepository> Sections { get; } = new(MockBehavior.Strict);
    public Mock<ICourseEnrollmentRepository> Enrollments { get; } = new(MockBehavior.Strict);
    public Mock<ILessonRepository> Lessons { get; } = new(MockBehavior.Strict);
    public Mock<ILessonProgressRepository> Progresses { get; } = new(MockBehavior.Strict);
    public Mock<IQuizRepository> Quizzes { get; } = new(MockBehavior.Strict);
    public Mock<IUnitOfWork> UnitOfWork { get; } = new(MockBehavior.Strict);
    public EnsureLessonReadable LessonReadable => new(Courses.Object, Sections.Object, Enrollments.Object);
    public EnsureQuizReadable QuizReadable => new(Lessons.Object, LessonReadable);
    public CompleteEnrollmentHandler Completion => new(Enrollments.Object, UnitOfWork.Object,
        Courses.Object, Lessons.Object, Progresses.Object);

    public LearningFlowFixture(EnrollmentStatus status = EnrollmentStatus.Active)
    {
        Course.Publish();
        Section = new(Course.Id, "Section", null, 1);
        Lesson = NewLesson();
        Quiz = new(Lesson.Id, "Quiz", null, 70m, 10, 2);
        Quiz.Publish();
        Enrollment = new(Course.Id, UserId);
        if (status == EnrollmentStatus.Completed) Enrollment.Complete();
        if (status == EnrollmentStatus.Cancelled) Enrollment.Cancel();
        Progress = new(UserId, Lesson.Id);

        Courses.Setup(x => x.GetByIdAsync(Course.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Course);
        Sections.Setup(x => x.GetByIdAsync(Section.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Section);
        Lessons.Setup(x => x.GetByIdAsync(Lesson.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Lesson);
        SetLessons(Lesson);
        Quizzes.Setup(x => x.GetByLessonIdAsync(Lesson.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Quiz);
        Quizzes.Setup(x => x.GetByIdAsync(Quiz.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Quiz);
        Enrollments.Setup(x => x.GetByCourseAndUserAsync(Course.Id, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enrollment);
        Enrollments.Setup(x => x.GetByIdAsync(Enrollment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Enrollment);
        // Executes application logic only; this mock makes no claim about SQL locking or rollback.
        Enrollments.Setup(x => x.ExecuteEnrollmentTransactionAsync(Enrollment.Id,
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Func<CancellationToken, Task> action, CancellationToken token) => action(token));
        Progresses.Setup(x => x.GetByUserAndLessonAsync(UserId, Lesson.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Progress);
        SetProgress(Progress);
        Progresses.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    public Lesson NewLesson(bool published = true)
    {
        var lesson = new Lesson(Section.Id, "Lesson", null, LessonType.Text, "Content", null, 5, 1);
        if (published) lesson.Publish();
        return lesson;
    }

    public void SetLessons(params Lesson[] lessons) => Lessons
        .Setup(x => x.GetByCourseIdAsync(Course.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lessons);

    public void SetProgress(params LessonProgress[] progress) => Progresses
        .Setup(x => x.GetByUserAndLessonIdsAsync(UserId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(progress);

    public void VerifyNoProgressWrites()
    {
        Progresses.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Progresses.Verify(x => x.AddAsync(It.IsAny<LessonProgress>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
