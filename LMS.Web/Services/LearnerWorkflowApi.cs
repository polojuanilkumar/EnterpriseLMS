using LMS.Web.Models;
namespace LMS.Web.Services;

public sealed partial class LearnerApi
{
    public async Task LearnerAccess(LearnerSession session,Guid org,CancellationToken token)
    {
        if(session.OrganizationId!=org)throw new WebApiException(403);
        var access=await Access(session,org,token);
        if(access.Role!="Student"||access.IsSuperAdmin)throw new WebApiException(403);
    }
    private async Task<LearnerEnrollment[]> EnrollmentRecords(LearnerSession session,Guid org,CancellationToken token)
    {
        var rows=await Get<LearnerEnrollment[]>("api/Enrollments/me",session,org,token);
        if(rows.Any(x=>x.Id==Guid.Empty||x.CourseId==Guid.Empty||x.Status is <1 or >3)
            ||rows.Select(x=>x.Id).Distinct().Count()!=rows.Length||rows.Select(x=>x.CourseId).Distinct().Count()!=rows.Length)throw new WebApiException(502);
        return rows;
    }
    public async Task<CatalogPage> Catalog(LearnerSession session,Guid org,CancellationToken token)
    {
        await LearnerAccess(session,org,token);
        // Catalog discovery uses metadata only, never progress, sections, lessons or content gates.
        var courses=await Get<CourseView[]>("api/Courses",session,org,token);
        var enrollments=await EnrollmentRecords(session,org,token);
        return new(org,courses.Where(x=>x.IsPublished).Select(x=>new CatalogRow(x,enrollments.SingleOrDefault(e=>e.CourseId==x.Id))).ToArray());
    }
    public async Task<MyCoursesPage> MyCourses(LearnerSession session,Guid org,CancellationToken token)
    {
        await LearnerAccess(session,org,token);
        var enrollments=await EnrollmentRecords(session,org,token);
        var courses=await Get<CourseView[]>("api/Courses",session,org,token);
        var progress=await Get<MyCourseCounts[]>("api/Enrollments/my-courses/progress",session,org,token);
        if(progress.Any(x=>!enrollments.Any(e=>e.CourseId==x.CourseId))||progress.Select(x=>x.CourseId).Distinct().Count()!=progress.Length)throw new WebApiException(502);
        return new(org,enrollments.Select(e=>new EnrolledCourseRow(courses.SingleOrDefault(c=>c.Id==e.CourseId),e,progress.SingleOrDefault(p=>p.CourseId==e.CourseId))).ToArray());
    }
    public async Task<CurrentCourseProgress> CourseProgress(LearnerSession session,Guid org,Guid course,CancellationToken token)
    {
        if(session.OrganizationId!=org)throw new WebApiException(403);
        var progress=await Get<CurrentCourseProgress>($"api/Courses/{course}/progress",session,org,token);
        if(progress.CourseId!=course||progress.UserId!=session.UserId||progress.EnrollmentStatus is not (1 or 2))throw new WebApiException(403);
        return progress;
    }
    public async Task<LearnerEnrollment> OwnEnrollment(LearnerSession session,Guid org,Guid course,Guid enrollment,CancellationToken token)
    {
        await LearnerAccess(session,org,token);
        var rows=await EnrollmentRecords(session,org,token);
        if(!rows.Any(x=>x.Id==enrollment&&x.CourseId==course))throw new WebApiException(404);
        var stored=await Get<LearnerEnrollment>($"api/Enrollments/{enrollment}",session,org,token);
        if(stored.UserId!=session.UserId)throw new WebApiException(403);
        if(stored.Id!=enrollment||stored.CourseId!=course)throw new WebApiException(404);
        return stored;
    }
    public async Task<CourseLearningPanel> CourseLearning(LearnerSession session,Guid org,Guid course,CancellationToken token)
    {
        await LearnerAccess(session,org,token);
        var progress=await CourseProgress(session,org,course,token);
        var records=await EnrollmentRecords(session,org,token);
        var enrollment=records.SingleOrDefault(x=>x.CourseId==course)??throw new WebApiException(404);
        return new(progress,await OwnEnrollment(session,org,course,enrollment.Id,token));
    }
    public async Task Enroll(LearnerSession session,Guid org,Guid course,bool confirmedReenrollment,CancellationToken token)
    {
        var catalog=await Catalog(session,org,token);
        var row=catalog.Courses.SingleOrDefault(x=>x.Course.Id==course)??throw new WebApiException(404);
        if(row.Enrollment?.Status==3&&!confirmedReenrollment)throw new AuthoringValidationException("Confirm re-enrollment of the existing Cancelled record.");
        // Active/Completed duplicates still reach the existing API's conflict guard, including races.
        await AuthoringChange(HttpMethod.Post,$"api/Courses/{course}/enroll",new {},session,org,token);
    }
    public async Task ChangeEnrollment(LearnerSession session,Guid org,Guid course,Guid enrollment,string action,bool confirmed,CancellationToken token)
    {
        var stored=await OwnEnrollment(session,org,course,enrollment,token);
        if(action is not ("cancel" or "complete"))throw new WebApiException(400);
        if(!confirmed)throw new AuthoringValidationException("Explicit confirmation is required.");
        if(action=="complete")await CourseProgress(session,org,course,token);
        // The API rechecks Active status, publication and completion prerequisites at the mutation boundary.
        await AuthoringChange(HttpMethod.Patch,$"api/Enrollments/{stored.Id}/{action}",new {},session,org,token);
    }
    public async Task<LessonProgressPanel> LessonState(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,CancellationToken token)
    {
        await LearnerAccess(session,org,token);
        var parent=await Lessons(session,org,course,section,token);
        if(!parent.Lessons.Any(x=>x.Id==lesson))throw new WebApiException(404);
        var courseProgress=await CourseProgress(session,org,course,token);
        LessonProgressView? progress=null;
        try {progress=await Get<LessonProgressView>($"api/Lessons/{lesson}/progress",session,org,token);}
        catch(WebApiException ex) when(ex.Status==404) { }
        if(progress is not null)
        {
            if(progress.UserId!=session.UserId)throw new WebApiException(403);
            if(progress.LessonId!=lesson)throw new WebApiException(404);
        }
        return new(courseProgress,progress,new(){ProgressPercentage=progress?.ProgressPercentage??0});
    }
    public async Task ChangeLessonProgress(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,string action,LessonProgressForm form,CancellationToken token)
    {
        await LessonState(session,org,course,section,lesson,token);
        var (method,suffix)=action switch {"start"=>(HttpMethod.Post,"start"),"update"=>(HttpMethod.Patch,"progress"),"complete"=>(HttpMethod.Patch,"complete"),_=>throw new WebApiException(400)};
        await AuthoringChange(method,$"api/Lessons/{lesson}/{suffix}",action=="update"?new {form.ProgressPercentage}:new {},session,org,token);
    }
}
