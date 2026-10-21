using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace LMS.Web.Models;

public sealed record BffCurrentProgress(int TotalLessons, int CompletedLessons, decimal ProgressPercentage, bool IsCompleted);
public sealed record BffStoredLessonProgress(bool IsCompleted, decimal ProgressPercentage, DateTime StartedAt, DateTime LastAccessedAt, DateTime? CompletedAt);
public sealed record BffStudentProgress(Guid OrganizationId, Guid CourseId, Guid? SectionId, Guid? LessonId,
    BffCurrentProgress Current, BffStudentEnrollment Enrollment, BffStoredLessonProgress? Lesson);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BffProgressAction;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BffProgressUpdate([Required, Range(typeof(decimal), "0", "100")] decimal? ProgressPercentage);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BffCompleteEnrollment(bool Confirmed = false);
