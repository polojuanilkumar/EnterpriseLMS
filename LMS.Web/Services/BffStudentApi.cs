using LMS.Web.Models;
namespace LMS.Web.Services;

public sealed partial class LearnerApi
{
    // Metadata and stored enrollment only: no content/progress gates or invented aggregates.
    public async Task<BffStudentCourses> StudentCourses(LearnerSession session, Guid org, bool catalog, CancellationToken token)
    {
        if (catalog)
        {
            var page = await Catalog(session, org, token);
            return new(org, page.Courses.Select(row => StudentRow(row.Course.Id, row.Course, row.Enrollment, session)).ToArray());
        }
        await LearnerAccess(session, org, token);
        var enrollments = await EnrollmentRecords(session, org, token);
        var courses = await Get<CourseView[]>("api/Courses", session, org, token);
        return new(org, enrollments.Select(row => StudentRow(row.CourseId, courses.SingleOrDefault(c => c.Id == row.CourseId), row, session)).ToArray());
    }
    private static BffStudentCourseRow StudentRow(Guid courseId, CourseView? course, LearnerEnrollment? enrollment, LearnerSession session)
    {
        if (enrollment is not null && enrollment.UserId != Guid.Empty && enrollment.UserId != session.UserId) throw new WebApiException(403);
        return new(courseId, course is null ? null : new(course.Id, course.Title, course.Description, course.CategoryName,
            course.Level, course.DurationInMinutes, course.IsPublished && course.Status == "Published"),
            enrollment is null ? null : new(enrollment.Id, enrollment.State, enrollment.EnrolledAt, enrollment.CompletedAt));
    }
}
