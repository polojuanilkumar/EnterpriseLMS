using System.Data;
using LMS.Application.Features.Auditing;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace LMS.Infrastructure.Persistence;

public partial class LMSDbContext : IAuditWriter
{
    private readonly AuditRequestContext? _audit;
    private readonly TimeProvider _auditClock;
    private readonly Guid _fallbackOperation=Guid.NewGuid();
    private readonly string _fallbackCorrelation=Guid.NewGuid().ToString("N");
    private bool _auditSaveFailed;
    public DbSet<AuditEvent> AuditEvents=>Set<AuditEvent>();
    private Guid? AuditActor=>_audit?.ActorUserId ?? (CurrentUserId==Guid.Empty?null:CurrentUserId);
    public AuditEvent Observation(AuditAction action,Guid id,Guid? organization,AuditMetadata metadata,AuditOutcome outcome)
        =>AuditEvent.Create(action,id,organization,AuditActor,Guid.NewGuid(),_audit?.CorrelationId??_fallbackCorrelation,_auditClock.GetUtcNow(),metadata,outcome);
    public void Stage(AuditAction action,Guid id,Guid? organization,AuditMetadata metadata)
    {
        if(_auditSaveFailed)throw new InvalidOperationException("The audited operation failed. Reload stored state in a new request.");
        if(AuditActor is null && action is not (AuditAction.PasswordReset or AuditAction.InvitationAccepted or AuditAction.MembershipCreated))
            throw new UnauthorizedAccessException("Verified audit actor required.");
        var audit=AuditEvent.Create(action,id,organization,AuditActor,_audit?.OperationId??_fallbackOperation,_audit?.CorrelationId??_fallbackCorrelation,_auditClock.GetUtcNow(),metadata);
        var existing=AuditEvents.Local.SingleOrDefault(x=>x.OperationId==audit.OperationId&&x.Action==audit.Action&&x.ResourceId==audit.ResourceId);
        if(existing is not null)
        {
            if(existing.Metadata!=audit.Metadata||existing.OrganizationId!=audit.OrganizationId)throw new InvalidOperationException("Separate audited operations require separate operation identifiers.");
            return;
        }
        AuditEvents.Add(audit); // Staged only; callers' business save and transaction own persistence.
    }
    internal void GuardAuditWrites()
    {
        if(_auditSaveFailed)throw new InvalidOperationException("The audited operation failed. Reload stored state in a new request.");
        foreach(var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if(entry.State is EntityState.Modified or EntityState.Deleted)throw new InvalidOperationException("Audit events are append-only.");
            if(entry.State==EntityState.Added)entry.Entity.Validate();
        }
    }
    private static bool AuditedBusiness(object value)=>value is Course or Lesson or Quiz or LessonAssignment or AssignmentSubmission or AssignmentGrade or CourseEnrollment;
    private async Task<int> SaveBusinessAndAudit(bool acceptAllChanges,CancellationToken token)
    {
        GuardAuditWrites();
        var candidates=ChangeTracker.Entries().Where(e=>AuditedBusiness(e.Entity)&&e.State is EntityState.Added or EntityState.Modified).ToArray();
        var audited=candidates.Length>0||ChangeTracker.Entries<AuditEvent>().Any(x=>x.State==EntityState.Added);
        if(audited&&!acceptAllChanges)throw new InvalidOperationException("Audited writes require accepting tracked changes after each save.");
        await using var transaction=audited&&Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(IsolationLevel.Serializable,token):null;
        try
        {
            foreach(var entry in candidates)
            {
                var id=(Guid)entry.Property("Id").CurrentValue!;
                if(entry.State==EntityState.Modified)
                {
                    switch(entry.Entity)
                    {
                        case Course c:
                            var stored=await Courses.AsNoTracking().SingleAsync(x=>x.Id==id,token);
                            if(stored.OrganizationId!=CurrentOrganizationId)throw new UnauthorizedAccessException("Stored audit organization mismatch.");
                            foreach(var transition in AuditTransitions.Course(stored,c,CurrentOrganizationId))Stage(transition);break;
                        case Lesson l:
                            var lesson=await Lessons.AsNoTracking().SingleAsync(x=>x.Id==id,token);
                            Publication(AuditAction.LessonPublicationChanged,id,await LessonOrganization(lesson.Id,token),lesson.IsPublished,l.IsPublished);break;
                        case Quiz q:
                            var quiz=await Quizzes.AsNoTracking().SingleAsync(x=>x.Id==id,token);
                            Publication(AuditAction.QuizPublicationChanged,id,await LessonOrganization(quiz.LessonId,token),quiz.IsPublished,q.IsPublished);break;
                        case LessonAssignment a:
                            var assignment=await LessonAssignments.AsNoTracking().SingleAsync(x=>x.Id==id,token);
                            Publication(AuditAction.AssignmentPublicationChanged,id,await LessonOrganization(assignment.LessonId,token),assignment.IsPublished,a.IsPublished);break;
                        case CourseEnrollment e:
                            var old=await CourseEnrollments.AsNoTracking().SingleAsync(x=>x.Id==id,token);
                            var org=await Courses.Where(x=>x.Id==old.CourseId).Select(x=>x.OrganizationId!.Value).SingleAsync(token);
                            var enrollment=AuditTransitions.Enrollment(id,old.CourseId,org,old.Status,e.Status);
                            if(enrollment is not null)Stage(enrollment);break;
                    }
                }
                else if(entry.Entity is AssignmentSubmission s)
                {
                    if(s.UserId!=AuditActor)throw new UnauthorizedAccessException("Stored submission actor mismatch.");
                    var lessonId=await LessonAssignments.Where(x=>x.Id==s.AssignmentId).Select(x=>x.LessonId).SingleAsync(token);
                    Stage(AuditTransitions.Submission(s,AuditActor!.Value,await LessonOrganization(lessonId,token)));
                }
                else if(entry.Entity is AssignmentGrade g)
                {
                    if(g.GraderId!=AuditActor)throw new UnauthorizedAccessException("Stored grading actor mismatch.");
                    var lessonId=await (from submission in AssignmentSubmissions join a in LessonAssignments on submission.AssignmentId equals a.Id
                        where submission.Id==g.SubmissionId select a.LessonId).SingleAsync(token);
                    Stage(AuditTransitions.Grade(g,AuditActor!.Value,await LessonOrganization(lessonId,token)));
                }
            }
            GuardAuditWrites();
            var result=await base.SaveChangesAsync(acceptAllChanges,token);
            if(transaction is not null)await transaction.CommitAsync(token);
            return result;
        }
        catch {_auditSaveFailed=audited;throw;}
    }
    private async Task<Guid> LessonOrganization(Guid lesson,CancellationToken token)
    {
        var organization=await (from l in Lessons join s in CourseSections on l.SectionId equals s.Id join c in Courses on s.CourseId equals c.Id
            where l.Id==lesson select c.OrganizationId!.Value).SingleAsync(token);
        if(organization!=CurrentOrganizationId)throw new UnauthorizedAccessException("Stored audit organization mismatch.");return organization;
    }
    private void Publication(AuditAction action,Guid id,Guid organization,bool before,bool after)
    {var transition=AuditTransitions.Publication(action,id,organization,before,after);if(transition is not null)Stage(transition);}
    private void Stage(AuditTransition transition)=>Stage(transition.Action,transition.ResourceId,transition.OrganizationId,transition.Metadata);
}
