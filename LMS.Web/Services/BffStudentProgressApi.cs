using LMS.Web.Models;
namespace LMS.Web.Services;

public sealed partial class LearnerApi
{
    public async Task<BffStudentProgress> StudentProgress(LearnerSession session, Guid course, Guid? section, Guid? lesson, CancellationToken token)
    {
        var org = session.OrganizationId ?? throw new WebApiException(403);
        if (section.HasValue != lesson.HasValue) throw new WebApiException(404);
        if (lesson.HasValue)
        {
            var parent = await StudentLessons(session, course, section!.Value, token);
            if (!parent.Lessons.Any(x => x.Id == lesson)) throw new WebApiException(404);
            var detail = await Get<LessonView>($"api/Lessons/{lesson}", session, org, token);
            if (detail.Id != lesson || detail.SectionId != section || !detail.IsPublished) throw new WebApiException(404);
        }
        else await StudentSections(session, course, token);
        var learning = await CourseLearning(session, org, course, token);
        if (learning.Enrollment.Status is not (1 or 2)) throw new WebApiException(403);
        LessonProgressView? stored = null;
        if (lesson.HasValue)
        {
            try { stored = await Get<LessonProgressView>($"api/Lessons/{lesson}/progress", session, org, token); }
            catch (WebApiException ex) when (ex.Status == 404) { } // Existing API's not-started representation.
            if (stored is not null)
            {
                if (stored.UserId != session.UserId) throw new WebApiException(403);
                if (stored.LessonId != lesson) throw new WebApiException(404);
            }
        }
        var current = learning.Progress;
        return new(org, course, section, lesson,
            new(current.TotalLessons, current.CompletedLessons, current.ProgressPercentage, current.IsCompleted),
            new(learning.Enrollment.Id, learning.Enrollment.State, learning.Enrollment.EnrolledAt, learning.Enrollment.CompletedAt),
            stored is null ? null : new(stored.IsCompleted, stored.ProgressPercentage, stored.StartedAt, stored.LastAccessedAt, stored.CompletedAt));
    }
    public async Task StudentChangeLesson(LearnerSession session, Guid course, Guid section, Guid lesson, string action, decimal percentage, CancellationToken token)
    {
        var stored = await StudentProgress(session, course, section, lesson, token);
        if (stored.Enrollment.Status != "Active") throw new WebApiException(409);
        await ChangeLessonProgress(session, stored.OrganizationId, course, section, lesson, action, new() { ProgressPercentage = percentage }, token);
    }
    public async Task StudentCompleteEnrollment(LearnerSession session, Guid course, Guid enrollment, bool confirmed, CancellationToken token)
    {
        var stored = await StudentProgress(session, course, null, null, token);
        if (stored.Enrollment.Id != enrollment) throw new WebApiException(404);
        if (!confirmed) throw new WebApiException(400);
        if (stored.Enrollment.Status != "Active") throw new WebApiException(409);
        // Eligibility is advisory. The guarded API rechecks all current prerequisites transactionally.
        await ChangeEnrollment(session, stored.OrganizationId, course, enrollment, "complete", true, token);
    }
}
