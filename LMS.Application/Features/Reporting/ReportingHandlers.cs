using LMS.Application.Features.Organizations;
using LMS.Domain.Enums;
namespace LMS.Application.Features.Reporting;
public sealed class ReportingHandlers(IReportingRepository repo,OrganizationContext org,TimeProvider clock)
{
    private void Access(bool staff)
    {
        if(!org.OrganizationId.HasValue||org.UserId==Guid.Empty)throw new UnauthorizedAccessException("Select a verified organization.");
        if(staff?!org.CanManageContent:org.IsSuperAdmin||org.Role!=OrganizationRole.Student)throw new UnauthorizedAccessException("Verified organization role is required.");
    }
    private void Page<T>(ReportPage<T> p,int page,int size,bool owner)
    {if(p.OrganizationId!=org.OrganizationId||p.OwnerUserId!=(owner?org.UserId:null)||p.Page!=page||p.PageSize!=size||p.Total<0||p.Items.Length>size)throw new UnauthorizedAccessException("Report scope mismatch.");}
    public async Task<ReportPage<CourseReportRow>> Courses(ReportFilter filter,CancellationToken token)
    {
        Access(true);filter.Validate();var p=await repo.Courses(filter,token);Page(p,filter.Page,filter.PageSize,false);
        if(p.Items.Any(c=>c.OrganizationId!=org.OrganizationId||!org.IsSuperAdmin&&org.Role==OrganizationRole.Instructor&&c.InstructorId!=org.UserId))throw new UnauthorizedAccessException("Course scope mismatch.");return p;
    }
    public async Task<CourseLearnerReport> Learners(Guid course,ReportFilter filter,CancellationToken token)
    {
        Access(true);filter.Validate();var c=await repo.StaffCourse(course,token)??throw new KeyNotFoundException("Reporting course not found or not assigned.");
        if(c.CourseId!=course||c.OrganizationId!=org.OrganizationId)throw new KeyNotFoundException("Reporting course not found.");
        if(!org.IsSuperAdmin&&org.Role==OrganizationRole.Instructor&&c.InstructorId!=org.UserId)throw new UnauthorizedAccessException("Assigned course ownership is required.");
        var p=await repo.Learners(course,filter,token);Page(p,filter.Page,filter.PageSize,false);if(p.Items.Any(x=>x.CourseId!=course||x.RecentAssignmentReview is {} link&&link.CourseId!=course))throw new KeyNotFoundException("Stored course mismatch.");
        return new(c,p with {Items=p.Items.Select(x=>x with {EnrolledAt=Utc(x.EnrolledAt),EnrollmentCompletedAt=Utc(x.EnrollmentCompletedAt)}).ToArray()});
    }
    public async Task<ReportPage<GradeCourseRow>> GradeCourses(ReportFilter filter,CancellationToken token)
    {Access(false);filter.Validate(true);var p=await repo.GradeCourses(filter,token);Page(p,filter.Page,filter.PageSize,true);foreach(var c in p.Items)Own(c,c.CourseId);return p with {Items=p.Items.Select(Normalize).ToArray()};}
    private void Own(GradeCourseRow c,Guid id)
    {if(c.CourseId!=id||c.OrganizationId!=org.OrganizationId||c.UserId!=org.UserId||c.EnrollmentStatus is not (1 or 2))throw new UnauthorizedAccessException("Only own Active or Completed enrollment grades are available.");}
    public async Task<GradeCourseRow> GradeCourse(Guid id,CancellationToken token)
    {Access(false);var c=await repo.GradeCourse(id,token)??throw new KeyNotFoundException("Gradebook course not found or enrollment unavailable.");Own(c,id);return Normalize(c);}
    public async Task<ReportPage<QuizGradeRow>> Quizzes(Guid course,AssessmentFilter filter,CancellationToken token)
    {
        Access(false);filter.Validate();await GradeCourse(course,token);var p=await repo.Quizzes(course,filter,clock.GetUtcNow().UtcDateTime,token);Page(p,filter.Page,filter.PageSize,true);
        if(p.Items.Any(x=>x.CourseId!=course))throw new KeyNotFoundException("Stored course mismatch.");
        return p with {Items=p.Items.Select(x=>x with {StartedAt=Utc(x.StartedAt),SubmittedAt=Utc(x.SubmittedAt),Deadline=Utc(x.Deadline)}).ToArray()};
    }
    public async Task<ReportPage<AssignmentGradeRow>> Assignments(Guid course,AssessmentFilter filter,CancellationToken token)
    {
        Access(false);filter.Validate();if(filter.Status==3)throw new ArgumentException("Assignment state must be 0 (not submitted), 1 (ungraded), or 2 (graded).");
        await GradeCourse(course,token);var p=await repo.Assignments(course,filter,token);Page(p,filter.Page,filter.PageSize,true);if(p.Items.Any(x=>x.CourseId!=course))throw new KeyNotFoundException("Stored course mismatch.");
        return p with {Items=p.Items.Select(x=>x with {SubmittedAt=Utc(x.SubmittedAt),GradedAt=Utc(x.GradedAt)}).ToArray()};
    }
    private static DateTime Utc(DateTime d)=>DateTime.SpecifyKind(d,DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d)=>d.HasValue?Utc(d.Value):null;
    private static GradeCourseRow Normalize(GradeCourseRow c)=>c with {EnrollmentCompletedAt=Utc(c.EnrollmentCompletedAt)};
}
