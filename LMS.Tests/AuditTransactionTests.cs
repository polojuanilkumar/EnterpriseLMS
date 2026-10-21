using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using LMS.Application.Features.Auditing;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using LMS.Infrastructure.Repositories;
using Xunit;
namespace LMS.Tests;

// EF save/transaction pipeline tests with inert ADO objects and suppressed SQL.
// They exercise no database and cannot establish actual SQL Server rollback or locking.
public sealed class AuditTransactionTests
{
    private sealed class Connection : DbConnection
    {
        public readonly List<Transaction> Transactions=[];
        public bool FailCommit;
        [AllowNull] public override string ConnectionString {get;set;}="";
        public override string Database=>"NoDatabase";
        public override string DataSource=>"NoServer";
        public override string ServerVersion=>"16.0";
        public override ConnectionState State=>ConnectionState.Open;
        public override void Open()=>throw new InvalidOperationException("No connections permitted.");
        public override void Close(){}
        public override void ChangeDatabase(string name)=>throw new NotSupportedException();
        protected override DbCommand CreateDbCommand()=>throw new InvalidOperationException("No SQL execution permitted.");
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)
        {var transaction=new Transaction(this,level,FailCommit);Transactions.Add(transaction);return transaction;}
    }
    private sealed class Transaction(Connection connection,IsolationLevel level,bool failCommit) : DbTransaction
    {
        public bool Committed;public bool RolledBack;
        public override IsolationLevel IsolationLevel=>level;
        protected override DbConnection DbConnection=>connection;
        public override void Commit(){if(failCommit)throw new InvalidOperationException("Unknown commit acknowledgement.");Committed=true;}
        public override void Rollback()=>RolledBack=true;
        protected override void Dispose(bool disposing){if(!Committed)RolledBack=true;base.Dispose(disposing);}
    }
    private sealed class SaveProbe : SaveChangesInterceptor
    {
        public bool Fail;public int Saves;public int Audits;public int Organizations;public bool InTransaction;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {
            var db=(LMSDbContext)data.Context!;Saves++;
            Audits=db.ChangeTracker.Entries<AuditEvent>().Count(x=>x.State==EntityState.Added);
            Organizations=db.ChangeTracker.Entries<Organization>().Count(x=>x.State==EntityState.Added);
            InTransaction=db.Database.CurrentTransaction is not null;
            if(Fail)throw new InvalidOperationException("Injected audit persistence failure.");
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(Audits+Organizations));
        }
    }
    private static LMSDbContext Context(Connection connection,SaveProbe probe)
    {
        var request=new AuditRequestContext();request.Authenticate(Guid.NewGuid());
        return new(new DbContextOptionsBuilder<LMSDbContext>().UseSqlServer(connection).AddInterceptors(probe).Options,audit:request);
    }
    private static Guid Stage(LMSDbContext db)
    {
        var organization=new Organization("AUDIT","Name");db.Organizations.Add(organization);
        db.Stage(AuditAction.OrganizationCreated,organization.Id,organization.Id,AuditMetadata.Empty());return organization.Id;
    }
    [Fact] public async Task Business_and_success_event_share_one_save_and_one_owned_transaction()
    {
        var connection=new Connection();var probe=new SaveProbe();await using var db=Context(connection,probe);Stage(db);
        await db.SaveChangesAsync();Assert.Equal(1,probe.Saves);Assert.Equal(1,probe.Audits);Assert.Equal(1,probe.Organizations);Assert.True(probe.InTransaction);
        Assert.True(Assert.Single(connection.Transactions).Committed);
    }
    [Fact] public async Task Audit_persistence_failure_prevents_commit_and_poisoned_context_cannot_replay()
    {
        var connection=new Connection();var probe=new SaveProbe {Fail=true};await using var db=Context(connection,probe);var id=Stage(db);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
        var transaction=Assert.Single(connection.Transactions);Assert.False(transaction.Committed);Assert.True(transaction.RolledBack);Assert.True(probe.InTransaction);Assert.Equal(1,probe.Organizations);Assert.Equal(1,probe.Audits);
        probe.Fail=false;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(()=>db.Stage(AuditAction.OrganizationCreated,id,id,AuditMetadata.Empty()));Assert.Equal(1,probe.Saves);
    }
    [Fact] public async Task Commit_acknowledgement_failure_is_propagated_and_never_reports_business_success()
    {
        var connection=new Connection {FailCommit=true};var probe=new SaveProbe();await using var db=Context(connection,probe);Stage(db);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());Assert.False(Assert.Single(connection.Transactions).Committed);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());Assert.Equal(1,probe.Saves);
    }
    [Fact] public async Task Explicit_multi_save_transaction_owns_commit_and_identical_stage_is_not_duplicated()
    {
        var connection=new Connection();var probe=new SaveProbe();await using var db=Context(connection,probe);
        await using var outer=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);var id=Stage(db);
        await db.SaveChangesAsync();Assert.False(connection.Transactions.Single().Committed);
        // A suppressed SQL save cannot accept entries itself; emulate EF's normal successful acceptance.
        db.ChangeTracker.AcceptAllChanges();
        db.Stage(AuditAction.OrganizationCreated,id,id,AuditMetadata.Empty());
        await db.SaveChangesAsync();Assert.Equal(0,probe.Audits);Assert.Single(db.AuditEvents.Local);
        await outer.RollbackAsync();Assert.True(connection.Transactions.Single().RolledBack);Assert.False(connection.Transactions.Single().Committed);
    }
    [Fact] public async Task Suppressing_accept_all_changes_is_rejected_before_save()
    {
        var connection=new Connection();var probe=new SaveProbe();await using var db=Context(connection,probe);Stage(db);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync(false));Assert.Equal(0,probe.Saves);Assert.Empty(connection.Transactions);
    }
    [Fact] public async Task Delivery_observation_uses_a_clean_context_and_cannot_commit_pending_business_changes()
    {
        var connection=new Connection();var probe=new SaveProbe();await using var business=Context(new Connection(),new SaveProbe());var org=Stage(business);
        var observation=business.Observation(AuditAction.InvitationDelivery,Guid.NewGuid(),org,AuditMetadata.Empty(),AuditOutcome.Unknown);
        await using var services=new ServiceCollection().AddScoped<LMSDbContext>(_=>Context(connection,probe)).BuildServiceProvider();
        var writer=new AuditObservationWriter(services.GetRequiredService<IServiceScopeFactory>());
        await writer.AppendDelivery(observation,default);
        Assert.Equal(0,probe.Organizations);Assert.Equal(1,probe.Audits);Assert.True(probe.InTransaction);Assert.True(connection.Transactions.Single().Committed);
        Assert.Single(business.ChangeTracker.Entries<Organization>());Assert.Single(business.ChangeTracker.Entries<AuditEvent>());
        var invalid=AuditEvent.Create(AuditAction.PasswordReset,Guid.NewGuid(),null,null,Guid.NewGuid(),Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,AuditMetadata.Empty());
        await Assert.ThrowsAsync<ArgumentException>(()=>writer.AppendDelivery(invalid,default));Assert.Equal(1,probe.Saves);
    }
    [Theory][InlineData(AuditAction.CoursePublicationChanged)][InlineData(AuditAction.LessonPublicationChanged)]
    [InlineData(AuditAction.QuizPublicationChanged)][InlineData(AuditAction.AssignmentPublicationChanged)]
    public void Publication_transition_has_only_verified_old_new_boolean_states_and_skips_noop(AuditAction action)
    {
        var id=Guid.NewGuid();var org=Guid.NewGuid();var change=AuditTransitions.Publication(action,id,org,false,true)!;
        Assert.Equal(action,change.Action);Assert.Equal(id,change.ResourceId);Assert.Equal(org,change.OrganizationId);
        var audit=AuditEvent.Create(action,id,org,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,change.Metadata);
        Assert.Contains("\"oldPublished\":\"false\"",audit.Metadata);Assert.Contains("\"newPublished\":\"true\"",audit.Metadata);
        Assert.Null(AuditTransitions.Publication(action,id,org,true,true));
    }
    [Fact] public void Instructor_change_and_course_publication_are_distinct_changes_with_stored_identity()
    {
        var org=Guid.NewGuid();var oldId=Guid.NewGuid();var newId=Guid.NewGuid();
        var course=new Course("private title","C","private description",oldId,CourseLevel.Beginner);course.AssignOrganization(org);
        using var db=TenantSqlTests.CreateContext();db.Attach(course);
        var stored=(Course)db.Entry(course).CurrentValues.ToObject();
        course.AssignInstructor(newId);course.Publish();var changes=AuditTransitions.Course(stored,course,org).ToArray();
        Assert.Equal(2,changes.Length);Assert.Equal(AuditAction.CourseInstructorChanged,changes[0].Action);
        var eventRow=AuditEvent.Create(changes[0].Action,changes[0].ResourceId,org,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,changes[0].Metadata);
        Assert.Contains(oldId.ToString(),eventRow.Metadata);Assert.Contains(newId.ToString(),eventRow.Metadata);Assert.DoesNotContain("private",eventRow.Metadata);
        Assert.Equal(AuditAction.CoursePublicationChanged,changes[1].Action);
        Assert.Throws<UnauthorizedAccessException>(()=>AuditTransitions.Course(stored,course,Guid.NewGuid()).ToArray());
    }
    [Fact] public void Submission_grade_and_enrollment_metadata_excludes_all_learning_content()
    {
        var org=Guid.NewGuid();var user=Guid.NewGuid();var grader=Guid.NewGuid();
        var assignment=new LessonAssignment(Guid.NewGuid(),"private title","private instructions",null,10,DateTime.UtcNow);
        var submission=new AssignmentSubmission(assignment,user,"private submitted text",Guid.NewGuid(),"private-file.pdf",12,DateTime.UtcNow);
        var grade=new AssignmentGrade(submission.Id,7,"private feedback",grader,DateTime.UtcNow);
        var submitted=AuditTransitions.Submission(submission,user,org);var graded=AuditTransitions.Grade(grade,grader,org);
        foreach(var transition in new[]{submitted,graded})
        {
            var row=AuditEvent.Create(transition.Action,transition.ResourceId,org,user,Guid.NewGuid(),Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,transition.Metadata);
            Assert.DoesNotContain("private",row.Metadata);Assert.DoesNotContain("marks",row.Metadata);Assert.DoesNotContain("filename",row.Metadata);
        }
        Assert.Equal(submission.Id,submitted.ResourceId);Assert.Equal(grade.Id,graded.ResourceId);
        Assert.Throws<UnauthorizedAccessException>(()=>AuditTransitions.Submission(submission,Guid.NewGuid(),org));Assert.Throws<UnauthorizedAccessException>(()=>AuditTransitions.Grade(grade,user,org));
        var enrollment=Guid.NewGuid();var course=Guid.NewGuid();
        Assert.Equal(AuditAction.EnrollmentCancelled,AuditTransitions.Enrollment(enrollment,course,org,EnrollmentStatus.Active,EnrollmentStatus.Cancelled)!.Action);
        Assert.Equal(AuditAction.EnrollmentCompleted,AuditTransitions.Enrollment(enrollment,course,org,EnrollmentStatus.Active,EnrollmentStatus.Completed)!.Action);
        Assert.Null(AuditTransitions.Enrollment(enrollment,course,org,EnrollmentStatus.Completed,EnrollmentStatus.Completed));
    }
}
