using LMS.Application.Features.Organizations;
using LMS.Application.Features.Reporting;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace LMS.Infrastructure.Repositories;
public sealed class ReportingRepository(LMSDbContext db,OrganizationContext org):IReportingRepository
{
    private ReportingQueries Queries=>new(ReportingSources.From(db),org);
    private async Task<ReportPage<T>> Page<T>(IQueryable<T> filtered,IQueryable<T> ordered,int page,int size,bool owner,CancellationToken token)
    {var total=await filtered.CountAsync(token);var rows=await ordered.Skip((page-1)*size).Take(size).ToArrayAsync(token);return new(org.OrganizationId!.Value,owner?org.UserId:null,page,size,total,rows);}
    public Task<ReportPage<CourseReportRow>> Courses(ReportFilter filter,CancellationToken token)
    {var q=Queries.Summary(filter);return Page(q,q.OrderBy(x=>x.Title).ThenBy(x=>x.CourseId),filter.Page,filter.PageSize,false,token);}
    public Task<CourseReference?> StaffCourse(Guid id,CancellationToken token)=>Queries.StaffCourse(id).SingleOrDefaultAsync(token);
    public Task<ReportPage<LearnerReportRow>> Learners(Guid course,ReportFilter filter,CancellationToken token)
    {var q=Queries.Learners(course,filter);return Page(q,q.OrderBy(x=>x.LastName).ThenBy(x=>x.FirstName).ThenBy(x=>x.UserId),filter.Page,filter.PageSize,false,token);}
    public Task<ReportPage<GradeCourseRow>> GradeCourses(ReportFilter filter,CancellationToken token)
    {var q=Queries.GradeCourses(filter);return Page(q,q.OrderBy(x=>x.Title).ThenBy(x=>x.CourseId),filter.Page,filter.PageSize,true,token);}
    public Task<GradeCourseRow?> GradeCourse(Guid course,CancellationToken token)=>Queries.GradeCourses(new()).Where(c=>c.CourseId==course).SingleOrDefaultAsync(token);
    public Task<ReportPage<QuizGradeRow>> Quizzes(Guid course,AssessmentFilter filter,DateTime now,CancellationToken token)
    {var q=Queries.Quizzes(course,filter,now);return Page(q,q.OrderBy(x=>x.Title).ThenBy(x=>x.QuizId).ThenBy(x=>x.AttemptNumber).ThenBy(x=>x.AttemptId),filter.Page,filter.PageSize,true,token);}
    public Task<ReportPage<AssignmentGradeRow>> Assignments(Guid course,AssessmentFilter filter,CancellationToken token)
    {var q=Queries.Assignments(course,filter);return Page(q,q.OrderBy(x=>x.Title).ThenBy(x=>x.AssignmentId),filter.Page,filter.PageSize,true,token);}
}
