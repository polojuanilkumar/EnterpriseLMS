using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using LMS.Infrastructure.Persistence.Migrations;
using LMS.Infrastructure.Repositories;
using LMS.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
namespace LMS.Tests;

public sealed class AssignmentPersistenceTests
{
    [Fact] public void New_model_has_unique_final_submission_grade_private_attachment_and_restrictive_FKs()
    {
        using var db=TenantSqlTests.CreateContext();
        var submission=db.Model.FindEntityType(typeof(AssignmentSubmission))!;var grade=db.Model.FindEntityType(typeof(AssignmentGrade))!;
        Assert.Contains(submission.GetIndexes(),i=>i.IsUnique&&i.GetDatabaseName()=="IX_AssignmentSubmissions_AssignmentId_UserId"&&i.Properties.Select(p=>p.Name).SequenceEqual(new[]{"AssignmentId","UserId"}));
        Assert.Contains(submission.GetIndexes(),i=>i.IsUnique&&i.GetFilter()=="[AttachmentId] IS NOT NULL");Assert.Contains(grade.GetIndexes(),i=>i.IsUnique&&i.GetDatabaseName()=="IX_AssignmentGrades_SubmissionId");
        foreach(var type in new[]{typeof(LessonAssignment),typeof(AssignmentSubmission),typeof(AssignmentGrade)})Assert.All(db.Model.FindEntityType(type)!.GetForeignKeys(),f=>Assert.Equal(DeleteBehavior.Restrict,f.DeleteBehavior));
        Assert.Equal(18,submission.FindProperty("MaximumMarksSnapshot")!.GetPrecision());Assert.Equal(2,grade.FindProperty("Marks")!.GetScale());
    }
    [Fact] public void Additive_migration_Up_only_creates_new_tables_indexes_and_checks_Down_only_drops_new_tables()
    {
        var migration=new AddLessonAssignments();var tables=migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[]{"LessonAssignments","AssignmentSubmissions","AssignmentGrades"},tables.Select(x=>x.Name));
        Assert.All(migration.UpOperations,o=>Assert.True(o is CreateTableOperation or CreateIndexOperation));
        Assert.All(tables.SelectMany(t=>t.ForeignKeys),fk=>Assert.Equal(ReferentialAction.Restrict,fk.OnDelete));
        Assert.Contains(tables.Single(x=>x.Name=="AssignmentSubmissions").CheckConstraints,c=>c.Name=="CK_AssignmentSubmissions_Attachment"&&c.Sql.Contains("[AttachmentSize] IS NOT NULL")&&c.Sql.Contains("26214400"));
        var guard=Assert.IsType<SqlOperation>(migration.DownOperations.First());Assert.Contains("THROW 51000",guard.Sql);Assert.Contains("[AssignmentSubmissions]",guard.Sql);Assert.Contains("[AssignmentGrades]",guard.Sql);
        Assert.Equal(new[]{"AssignmentGrades","AssignmentSubmissions","LessonAssignments"},migration.DownOperations.Skip(1).Cast<DropTableOperation>().Select(x=>x.Name));
    }
    [Fact] public void Migration_SQL_generation_and_pending_model_check_are_offline()
    {
        using var db=TenantSqlTests.CreateContext();Assert.False(db.Database.HasPendingModelChanges());
        var script=db.GetService<IMigrator>().GenerateScript("20260929175839_AddPdfPresentations","20261004061629_AddLessonAssignments");
        Assert.Contains("CREATE TABLE [AssignmentSubmissions]",script);Assert.Contains("CREATE UNIQUE INDEX [IX_AssignmentSubmissions_AssignmentId_UserId]",script);Assert.Contains("CREATE UNIQUE INDEX [IX_AssignmentGrades_SubmissionId]",script);
        Assert.DoesNotContain("ALTER TABLE [CourseEnrollments]",script);Assert.DoesNotContain("DELETE FROM",script);Assert.DoesNotContain("CASCADE",script);
        var down=db.GetService<IMigrator>().GenerateScript("20261004061629_AddLessonAssignments","20260929175839_AddPdfPresentations");Assert.Contains("THROW 51000",down);Assert.DoesNotContain("DROP TABLE [Lessons]",down);
    }
    [Fact] public void Actual_lock_queries_generate_tenant_filtered_parameterized_SQL_without_execution()
    {
        var org=Guid.NewGuid();var assignment=Guid.NewGuid();using var db=TenantSqlTests.CreateContext(TenantSqlTests.Scope(org));var repo=new AssignmentRepository(db);
        var assignmentSql=repo.AssignmentLock(assignment).ToQueryString();Assert.Contains("(UPDLOCK,HOLDLOCK)",assignmentSql);
        foreach(var sql in new[]{assignmentSql,repo.LessonLock(assignment).ToQueryString(),repo.CourseLock(assignment).ToQueryString()})
        {Assert.Contains("HOLDLOCK",sql);Assert.Contains(org.ToString(),sql);Assert.Contains(assignment.ToString(),sql);Assert.Contains("[OrganizationId]",sql);Assert.Contains("@p0",sql);}
        // Translation only: no SQL Server locking or transaction has been exercised.
    }
    [Theory][InlineData("assignment")][InlineData("submission")][InlineData("grade")]
    public void Assignment_queries_scope_through_stored_lesson_course_organization(string type)
    {
        var org=Guid.NewGuid();using var db=TenantSqlTests.CreateContext(TenantSqlTests.Scope(org,OrganizationRole.Admin));
        var sql=type switch {"assignment"=>db.LessonAssignments.ToQueryString(),"submission"=>db.AssignmentSubmissions.ToQueryString(),_=>db.AssignmentGrades.ToQueryString()};
        Assert.Contains(org.ToString(),sql);Assert.Contains("[Lessons]",sql);Assert.Contains("[Courses]",sql);Assert.Contains("[OrganizationId] IS NOT NULL",sql);
        using var none=TenantSqlTests.CreateContext();var noScope=none.AssignmentSubmissions.ToQueryString();Assert.Contains(Guid.Empty.ToString(),noScope);
    }
    [Theory][InlineData(2601,"Cannot insert duplicate key in unique index 'IX_AssignmentSubmissions_AssignmentId_UserId'.",true)][InlineData(2627,"Violation 'IX_AssignmentSubmissions_AssignmentId_UserId'.",true)]
    [InlineData(2601,"unique index 'IX_AssignmentSubmissions_AttachmentId'",false)][InlineData(2627,"index 'IX_AssignmentSubmissions_AssignmentId_UserId_Other'",false)][InlineData(515,"index 'IX_AssignmentSubmissions_AssignmentId_UserId'",false)]
    public void Duplicate_SQL_translation_targets_only_exact_submission_constraint(int number,string message,bool expected)
        =>Assert.Equal(expected,AssignmentRepository.IsNamedUnique(number,message,"IX_AssignmentSubmissions_AssignmentId_UserId"));
    [Theory][InlineData("submission",true)][InlineData("submission",false)][InlineData("grade",true)][InlineData("grade",false)][InlineData("assignment",true)]
    public async Task Submitted_work_grade_changes_deletes_and_assignment_deletion_are_rejected_before_SQL(string type,bool delete)
    {
        using var db=TenantSqlTests.CreateContext(TenantSqlTests.Scope(Guid.NewGuid(),OrganizationRole.Admin));var a=new LessonAssignment(Guid.NewGuid(),"Title","Work",null,10,DateTime.UtcNow);var s=new AssignmentSubmission(a,Guid.NewGuid(),"Work",null,null,null,DateTime.UtcNow);
        object entity=type switch {"submission"=>s,"grade"=>new AssignmentGrade(s.Id,5,"Feedback",Guid.NewGuid(),DateTime.UtcNow),_=>a};
        db.Attach(entity);db.Entry(entity).State=delete?EntityState.Deleted:EntityState.Modified;
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());Assert.Contains(type=="assignment"?"deletion":"immutable",error.Message);
    }
    [Fact] public async Task Offline_factory_refuses_database_connections()
    {
        await using var db=new LMSDbContextFactory().CreateDbContext(["--offline"]);var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>db.Database.OpenConnectionAsync());Assert.Contains("Offline tooling",error.Message);
    }
    [Fact] public async Task Separate_private_store_cleans_abandoned_uploads_and_retains_generated_committed_keys()
    {
        var root=Path.Combine(Path.GetTempPath(),"lms-assignments-"+Guid.NewGuid().ToString("N"));var web=Path.Combine(root,"wwwroot");var privateRoot=Path.Combine(root,"private");
        try
        {
            Assert.Throws<ArgumentException>(()=>new AssignmentStorage(Path.Combine(web,"uploads"),web));
            var store=new AssignmentStorage(privateRoot,web);using var first=AssignmentPolicyTests.Pdf();
            await using(var staged=await store.StageAsync(Guid.NewGuid(),first,26214400,default))await staged.PublishAsync(default);
            Assert.Empty(Directory.GetFiles(privateRoot));
            var key=Guid.NewGuid();using var second=AssignmentPolicyTests.Pdf();await using(var staged=await store.StageAsync(key,second,26214400,default)){await staged.PublishAsync(default);staged.Retain();}
            Assert.Equal(key.ToString("N")+".media",Path.GetFileName(Assert.Single(Directory.GetFiles(privateRoot))));
            await using var downloaded=await store.OpenReadAsync(key,default);Assert.True(downloaded.Length>0);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
