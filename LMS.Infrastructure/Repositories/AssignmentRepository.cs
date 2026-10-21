using System.Data;
using LMS.Application.Features.Assignments;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace LMS.Infrastructure.Repositories;

public sealed class AssignmentRepository(LMSDbContext db):IAssignmentRepository
{
    public Task<AssignmentParent?> Parent(Guid lesson,CancellationToken token)
        =>(from l in db.Lessons.AsNoTracking() join s in db.CourseSections.AsNoTracking() on l.SectionId equals s.Id join c in db.Courses.AsNoTracking() on s.CourseId equals c.Id where l.Id==lesson select new AssignmentParent(l.Id,s.Id,c.Id,c.OrganizationId,c.InstructorId,c.IsPublished,l.IsPublished)).SingleOrDefaultAsync(token);
    public async Task<int?> Enrollment(Guid course,Guid user,CancellationToken token)
        =>await db.CourseEnrollments.AsNoTracking().Where(x=>x.CourseId==course&&x.UserId==user).Select(x=>(int?)x.Status).SingleOrDefaultAsync(token);
    public Task<LessonAssignment?> Get(Guid id,CancellationToken token)=>db.LessonAssignments.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token);
    public Task<LessonAssignment[]> List(Guid lesson,CancellationToken token)=>db.LessonAssignments.AsNoTracking().Where(x=>x.LessonId==lesson).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToArrayAsync(token);
    public Task<AssignmentSubmission[]> Submissions(Guid a,Guid? user,CancellationToken token)=>db.AssignmentSubmissions.AsNoTracking().Where(x=>x.AssignmentId==a&&(!user.HasValue||x.UserId==user)).OrderBy(x=>x.SubmittedAt).ToArrayAsync(token);
    internal IQueryable<AssignmentSubmission> SubmissionPageQuery(Guid assignment,int page,int pageSize)=>db.AssignmentSubmissions.AsNoTracking().Where(x=>x.AssignmentId==assignment).OrderByDescending(x=>x.SubmittedAt).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize);
    public Task<bool> HasSubmissions(Guid assignment,CancellationToken token)=>db.AssignmentSubmissions.AnyAsync(x=>x.AssignmentId==assignment,token);
    public async Task<SubmissionSlice> SubmissionPage(Guid assignment,int page,int pageSize,CancellationToken token)
    {
        var query=db.AssignmentSubmissions.AsNoTracking().Where(x=>x.AssignmentId==assignment);
        var total=await query.CountAsync(token);
        var rows=await SubmissionPageQuery(assignment,page,pageSize).ToArrayAsync(token);
        return new(total,rows);
    }
    public Task<AssignmentSubmission?> Submission(Guid id,CancellationToken token)=>db.AssignmentSubmissions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token);
    public Task<AssignmentGrade?> Grade(Guid s,CancellationToken token)=>db.AssignmentGrades.AsNoTracking().SingleOrDefaultAsync(x=>x.SubmissionId==s,token);
    public Task<AssignmentSubmission[]> History(Guid user,CancellationToken token)=>db.AssignmentSubmissions.AsNoTracking().Where(x=>x.UserId==user).OrderByDescending(x=>x.SubmittedAt).ToArrayAsync(token);
    public void Add(object entity)=>db.Add(entity);
    public void Update(LessonAssignment a){db.Update(a);db.Entry(a).Property(x=>x.LessonId).IsModified=false;}
    public async Task Save(CancellationToken token)=>await db.SaveChangesAsync(token);
    public static bool IsNamedUnique(int number,string message,string name)=>number is 2601 or 2627&&(message.Contains("'"+name+"'",StringComparison.Ordinal)||message.Contains("\""+name+"\"",StringComparison.Ordinal)||message.Contains("["+name+"]",StringComparison.Ordinal));
    internal IQueryable<LessonAssignment> AssignmentLock(Guid id)=>db.LessonAssignments.FromSqlInterpolated($"SELECT * FROM [LessonAssignments] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").AsNoTracking();
    internal IQueryable<Lesson> LessonLock(Guid id)=>db.Lessons.FromSqlInterpolated($"SELECT l.* FROM [Lessons] l WITH (HOLDLOCK) JOIN [LessonAssignments] a ON a.[LessonId]=l.[Id] WHERE a.[Id]={id}").AsNoTracking();
    internal IQueryable<Course> CourseLock(Guid id)=>db.Courses.FromSqlInterpolated($"SELECT c.* FROM [Courses] c WITH (HOLDLOCK) JOIN [CourseSections] s WITH (HOLDLOCK) ON s.[CourseId]=c.[Id] JOIN [Lessons] l ON l.[SectionId]=s.[Id] JOIN [LessonAssignments] a ON a.[LessonId]=l.[Id] WHERE a.[Id]={id}").AsNoTracking();
    public async Task<T> Locked<T>(Guid assignment,Func<CancellationToken,Task<T>> action,Action? beforeCommit,CancellationToken token)
    {
        if(!await db.LessonAssignments.AnyAsync(x=>x.Id==assignment,token))throw new KeyNotFoundException("Assignment not found.");
        try
        {
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
            // Organization-filtered UPDATE lock serializes assignment edits/publication/submits/grading.
            await AssignmentLock(assignment).ToListAsync(token);
            // Keep publication and enrollment reads stable until commit; SQL updates must wait.
            await LessonLock(assignment).ToListAsync(token);
            await CourseLock(assignment).ToListAsync(token);
            var result=await action(token);
            beforeCommit?.Invoke(); // Preserve published files if commit acknowledgement becomes uncertain.
            await transaction.CommitAsync(token);return result;
        }
        catch(Exception ex) when(IsConflict(ex)) {throw new InvalidOperationException("A final submission or grade already exists, or a concurrent operation conflicted. Refresh stored state; no automatic retry.",ex);}
    }
    private static bool IsConflict(Exception exception)
    {
        for(Exception? ex=exception;ex is not null;ex=ex.InnerException)
            if(ex is SqlException sql&&sql.Errors.Cast<SqlError>().Any(e=>e.Number==1205||IsNamedUnique(e.Number,e.Message,"IX_AssignmentSubmissions_AssignmentId_UserId")||IsNamedUnique(e.Number,e.Message,"IX_AssignmentGrades_SubmissionId")))return true;
        return false;
    }
}
