using LMS.Web.Models;
namespace LMS.Web.Services;
public sealed partial class LearnerApi
{
    public Task<AssignmentPage> StudentAssignment(LearnerSession s, Guid course, Guid section, Guid lesson, Guid? assignment, Guid? submission, CancellationToken token)
        => AssignmentPage(s, s.OrganizationId ?? throw new WebApiException(403), course, section, lesson, false, assignment, submission, token);
    public static object StudentAssignmentProjection(AssignmentPage p)
    {
        static object Work(AssignmentSubmissionView s) => new { s.Id, s.AssignmentId, s.Text, s.HasAttachment, s.SubmittedAt, s.InstructionsSnapshot, s.MaximumMarksSnapshot,
            Grade = s.Grade is null ? null : new { s.Grade.Marks, s.Grade.Feedback, s.Grade.GradedAt } };
        static object Definition(AssignmentView a) => new { a.Id, a.LessonId, a.Title, a.Instructions, a.DueAtUtc, a.MaximumMarks, a.IsPublished };
        var parent = p.Context.Parent;
        return new { p.OrganizationId, parent.CourseId, parent.SectionId, parent.LessonId, parent.CoursePublished, parent.LessonPublished,
            EnrollmentStatus = p.Context.EnrollmentStatus, Assignments = p.Assignments.Select(Definition), Assignment = p.Assignment is null ? null : Definition(p.Assignment), Submissions = p.Submissions.Select(Work),
            Submission = p.Submission is null ? null : Work(p.Submission), p.CanSubmit };
    }
    public async Task StudentSubmitAssignment(LearnerSession s, AssignmentPage page, BffAssignmentUpload form, CancellationToken token)
    {
        if (page.Context.EnrollmentStatus != 1) throw new WebApiException(403);
        if (page.Submissions.Length > 0) throw new WebApiException(409);
        if (!page.Context.Parent.CoursePublished || !page.Context.Parent.LessonPublished || page.Assignment?.IsPublished != true) throw new WebApiException(404);
        await SubmitAssignment(s, page, new() { Text = form.Text, File = form.File, Confirmed = form.Confirmed }, token);
    }
    public static object StudentGradeCourse(GradeCourseView c) => new { c.CourseId, c.Title, c.Published, c.EnrollmentStatus, EnrollmentCompletedAt = GradeUtc(c.EnrollmentCompletedAt),
        c.StoredEnrollmentPercentage, c.PublishedLessons, c.CompletedPublishedLessons, c.CurrentPercentage };
    private static DateTime? GradeUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    public static QuizGradeView StudentQuizGrade(QuizGradeView q) => q with { StartedAt = GradeUtc(q.StartedAt), SubmittedAt = GradeUtc(q.SubmittedAt), Deadline = GradeUtc(q.Deadline) };
    public static AssignmentGradebookView StudentAssignmentGrade(AssignmentGradebookView a) => a with { SubmittedAt = GradeUtc(a.SubmittedAt), GradedAt = GradeUtc(a.GradedAt) };
    public static object StudentGradePage<T>(ReportPageView<T> p) => new { p.Page, p.PageSize, p.Total, p.Items };
}
