using System.ComponentModel.DataAnnotations;
namespace LMS.Web.Models;

public sealed record LearnerEnrollment(Guid Id, Guid CourseId, int Status, DateTime EnrolledAt, DateTime? CompletedAt, decimal ProgressPercentage, Guid UserId = default)
{
    public string State => Status switch {1=>"Active",2=>"Completed",3=>"Cancelled",_=>"Unknown"};
}
public sealed record CurrentCourseProgress(Guid CourseId, Guid UserId, int TotalLessons, int CompletedLessons, decimal ProgressPercentage, bool IsCompleted, int EnrollmentStatus, DateTime? EnrollmentCompletedAt)
{
    public bool CanComplete => EnrollmentStatus==1 && TotalLessons>0 && CompletedLessons==TotalLessons && IsCompleted;
}
public sealed record MyCourseCounts(Guid CourseId, int TotalLessons, int CompletedLessons, decimal ProgressPercentage, bool IsCompleted);
public sealed record CatalogRow(CourseView Course, LearnerEnrollment? Enrollment);
public sealed record CatalogPage(Guid OrganizationId, CatalogRow[] Courses, string? Notice = null);
public sealed record EnrolledCourseRow(CourseView? Course, LearnerEnrollment Enrollment, MyCourseCounts? Progress);
public sealed record MyCoursesPage(Guid OrganizationId, EnrolledCourseRow[] Courses, string? Notice = null);
public sealed record LessonProgressView(Guid Id, Guid UserId, Guid LessonId, bool IsCompleted, DateTime? CompletedAt, DateTime StartedAt, decimal ProgressPercentage, DateTime LastAccessedAt);
public sealed class LessonProgressForm
{
    [Range(typeof(decimal),"0","100")] public decimal ProgressPercentage { get; set; }
    public bool Confirmed { get; set; }
}
public sealed record LessonProgressPanel(CurrentCourseProgress Course, LessonProgressView? Lesson, LessonProgressForm Form);
public sealed record CourseLearningPanel(CurrentCourseProgress Progress, LearnerEnrollment Enrollment);
public sealed record LearnerRecoveryPage(Guid OrganizationId, string Notice, string ReturnPath);
