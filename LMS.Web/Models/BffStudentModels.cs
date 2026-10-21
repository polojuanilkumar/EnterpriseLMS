using System.Text.Json.Serialization;
namespace LMS.Web.Models;

public sealed record BffStudentCourse(Guid Id, string Title, string Description, string? CategoryName,
    string Level, int DurationInMinutes, bool IsAvailable);
public sealed record BffStudentEnrollment(Guid Id, string Status, DateTime EnrolledAt, DateTime? CompletedAt);
public sealed record BffStudentCourseRow(Guid CourseId, BffStudentCourse? Course, BffStudentEnrollment? Enrollment);
public sealed record BffStudentCourses(Guid OrganizationId, BffStudentCourseRow[] Courses);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BffEnrollRequest(bool ConfirmedReenrollment = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BffCancelRequest(bool Confirmed = false);
