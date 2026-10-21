using LMS.Web.Models;
namespace LMS.Web.Services;

// Student viewer reads deliberately avoid progress, quiz and assignment endpoints.
public sealed partial class LearnerApi
{
    public async Task<SectionsPage> StudentSections(LearnerSession session, Guid courseId, CancellationToken token)
    {
        var org = session.OrganizationId ?? throw new WebApiException(403);
        await LearnerAccess(session, org, token);
        var course = await Get<CourseView>($"api/Courses/{courseId}", session, org, token);
        if (course.Id != courseId || !course.IsPublished || course.Status != "Published") throw new WebApiException(404);
        var records = await EnrollmentRecords(session, org, token);
        var enrollment = records.SingleOrDefault(x => x.CourseId == courseId);
        if (enrollment is null || enrollment.Status is not (1 or 2)) throw new WebApiException(403);
        await OwnEnrollment(session, org, courseId, enrollment.Id, token);
        var sections = await Get<SectionView[]>($"api/Courses/{courseId}/sections", session, org, token);
        if (sections.Any(x => x.CourseId != courseId) || sections.Select(x => x.Id).Distinct().Count() != sections.Length) throw new WebApiException(404);
        return new(org, course, sections.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray());
    }
    public async Task<LessonsPage> StudentLessons(LearnerSession session, Guid courseId, Guid sectionId, CancellationToken token)
    {
        var page = await StudentSections(session, courseId, token);
        var section = page.Sections.SingleOrDefault(x => x.Id == sectionId) ?? throw new WebApiException(404);
        var lessons = await Get<LessonView[]>($"api/Sections/{sectionId}/lessons", session, page.OrganizationId, token);
        if (lessons.Any(x => x.SectionId != sectionId) || lessons.Select(x => x.Id).Distinct().Count() != lessons.Length) throw new WebApiException(404);
        return new(page.OrganizationId, page.Course, section, lessons.Where(x => x.IsPublished).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray());
    }
    public async Task<UnitPage> StudentUnit(LearnerSession session, Guid courseId, Guid sectionId, Guid lessonId, CancellationToken token)
    {
        var page = await StudentLessons(session, courseId, sectionId, token);
        if (!page.Lessons.Any(x => x.Id == lessonId)) throw new WebApiException(404);
        var lesson = await Get<LessonView>($"api/Lessons/{lessonId}", session, page.OrganizationId, token);
        if (lesson.Id != lessonId || lesson.SectionId != sectionId || !lesson.IsPublished) throw new WebApiException(404);
        var content = await Get<ContentView>($"api/Lessons/{lessonId}/content-items", session, page.OrganizationId, token);
        if (content.Items.Any(x => x.LessonId != lessonId) || content.Items.Select(x => x.Id).Distinct().Count() != content.Items.Length) throw new WebApiException(404);
        return new(page.OrganizationId, courseId, sectionId, lesson,
            new(content.Items.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray(), content.Items.Length == 0 ? content.Legacy : null));
    }
}
