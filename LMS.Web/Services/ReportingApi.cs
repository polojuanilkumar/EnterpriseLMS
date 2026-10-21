using LMS.Web.Models;
namespace LMS.Web.Services;
public sealed partial class LearnerApi
{
    public async Task ReportingAccess(LearnerSession session,Guid org,bool staff,CancellationToken token)
    {if(staff)await StaffAccess(session,org,false,token);else await LearnerAccess(session,org,token);}
    private static void Filter(ReportFilterView f,bool learner)
    {if(f.Page<1||f.Page>10000||f.PageSize<1||f.PageSize>50||f.Search?.Length>100||f.EnrollmentStatus is {} state&&(state<1||state>(learner?2:3)))throw new WebApiException(400);}
    private static void PageScope<T>(ReportPageView<T> p,LearnerSession s,Guid org,bool owner,int page,int size)
    {if(p.Items is null)throw new WebApiException(502);if(p.OrganizationId!=org||p.OwnerUserId!=(owner?s.UserId:null)||p.Page!=page||p.PageSize!=size||p.Total<0||p.Items.Length>size)throw new WebApiException(403);}
    public async Task<StaffReportsPage> StaffReports(LearnerSession s,Guid org,ReportFilterView f,CancellationToken token)
    {
        var access=await StaffAccess(s,org,false,token);Filter(f,false);var p=await Get<ReportPageView<ReportCourseView>>("api/Reports/courses"+f.Query(),s,org,token);PageScope(p,s,org,false,f.Page,f.PageSize);
        if(p.Items.Any(c=>c.OrganizationId!=org||!access.IsAdmin&&c.InstructorId!=s.UserId))throw new WebApiException(403);return new(org,f,p);
    }
    public async Task<StaffLearnersReportPage> StaffReportLearners(LearnerSession s,Guid org,Guid course,ReportFilterView f,CancellationToken token)
    {
        var access=await StaffAccess(s,org,false,token);Filter(f,false);var report=await Get<CourseLearnerReportView>($"api/Reports/courses/{course}/learners"+f.Query(),s,org,token);
        if(report.Course.CourseId!=course||report.Course.OrganizationId!=org)throw new WebApiException(404);if(!access.IsAdmin&&report.Course.InstructorId!=s.UserId)throw new WebApiException(403);
        PageScope(report.Learners,s,org,false,f.Page,f.PageSize);if(report.Learners.Items.Any(x=>x.CourseId!=course||x.RecentAssignmentReview is {} link&&link.CourseId!=course))throw new WebApiException(404);return new(org,f,report);
    }
    private static void OwnGradeCourse(GradeCourseView c,LearnerSession s,Guid org,Guid course)
    {if(c.CourseId!=course||c.OrganizationId!=org||c.UserId!=s.UserId||c.EnrollmentStatus is not (1 or 2))throw new WebApiException(403);}
    public async Task<MyGradesPage> MyGrades(LearnerSession s,Guid org,ReportFilterView f,CancellationToken token)
    {
        await LearnerAccess(s,org,token);Filter(f,true);var p=await Get<ReportPageView<GradeCourseView>>("api/Gradebook/courses"+f.Query(),s,org,token);PageScope(p,s,org,true,f.Page,f.PageSize);foreach(var c in p.Items)OwnGradeCourse(c,s,org,c.CourseId);return new(org,f,p);
    }
    public async Task<CourseGradesPage> CourseGrades(LearnerSession s,Guid org,Guid course,int quizPage,int assignmentPage,int pageSize,int? quizStatus,int? assignmentStatus,CancellationToken token)
    {
        await LearnerAccess(s,org,token);
        if(quizPage<1||quizPage>10000||assignmentPage<1||assignmentPage>10000||pageSize<1||pageSize>50||quizStatus is <0 or >3||assignmentStatus is <0 or >2)throw new WebApiException(400);
        var root=$"api/Gradebook/courses/{course}";var c=await Get<GradeCourseView>(root,s,org,token);OwnGradeCourse(c,s,org,course);
        // Fixed requests per page; never per assessment or learner.
        var quizzes=await Get<ReportPageView<QuizGradeView>>(root+$"/quizzes?page={quizPage}&pageSize={pageSize}"+(quizStatus.HasValue?$"&status={quizStatus}":""),s,org,token);
        var assignments=await Get<ReportPageView<AssignmentGradebookView>>(root+$"/assignments?page={assignmentPage}&pageSize={pageSize}"+(assignmentStatus.HasValue?$"&status={assignmentStatus}":""),s,org,token);
        PageScope(quizzes,s,org,true,quizPage,pageSize);PageScope(assignments,s,org,true,assignmentPage,pageSize);
        if(quizzes.Items.Any(x=>x.CourseId!=course)||assignments.Items.Any(x=>x.CourseId!=course))throw new WebApiException(404);
        return new(org,c,quizzes,assignments,quizStatus,assignmentStatus);
    }
}
