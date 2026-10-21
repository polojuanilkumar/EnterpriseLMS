using System.Reflection;
using System.Text.Json;
using LMS.Application.Features.Auditing;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using LMS.Infrastructure.Persistence.Migrations;
using LMS.Infrastructure.Repositories;
using LMS.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
namespace LMS.Tests;

public sealed class AuditTests
{
    private static readonly DateTimeOffset Now=new(2026,10,6,12,0,0,TimeSpan.Zero);
    private static AuditEvent Event(Guid org,Guid? actor=null,DateTimeOffset? time=null,Guid? resource=null)
        =>AuditEvent.Create(AuditAction.CoursePublicationChanged,resource??Guid.NewGuid(),org,actor??Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString("N"),time??Now,AuditMetadata.Publication(false,true));
    [Fact] public void Two_organizations_scoped_totals_filters_order_pages_and_foreign_details()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var actor=Guid.NewGuid();var resource=Guid.NewGuid();
        var first=Event(a,actor,Now.AddMinutes(-3),resource);var second=Event(a,actor,Now.AddMinutes(-2));var third=Event(a,null,Now.AddMinutes(-1));var foreign=Event(b,actor);
        var account=AuditEvent.Create(AuditAction.PasswordReset,Guid.NewGuid(),null,null,Guid.NewGuid(),Guid.NewGuid().ToString("N"),Now,AuditMetadata.Empty());
        var source=new[]{first,second,third,foreign,account}.AsQueryable();var filter=new AuditFilter {FromUtc=Now.AddDays(-1),ToUtc=Now.AddDays(1)}.Normalize(Now);
        var rows=AuditQueries.Filter(AuditQueries.Organization(source,a),filter);Assert.Equal(3,rows.Count());
        Assert.Equal(third.Id,AuditQueries.Ordered(rows).Skip(0).Take(1).Single().Id);Assert.Equal(second.Id,AuditQueries.Ordered(rows).Skip(1).Take(1).Single().Id);
        Assert.Empty(AuditQueries.Organization(source,a).Where(x=>x.Id==foreign.Id));Assert.Single(AuditQueries.Organization(source,b));
        Assert.Equal(account.Id,AuditQueries.Platform(source).Single().Id);Assert.Empty(AuditQueries.Platform(source).Where(x=>x.Id==first.Id));
        filter.ActorId=actor;Assert.Equal(2,AuditQueries.Filter(AuditQueries.Organization(source,a),filter).Count());
        filter.ResourceId=resource;Assert.Equal(first.Id,AuditQueries.Filter(AuditQueries.Organization(source,a),filter).Single().Id);
        filter.Outcome="failed";Assert.Empty(AuditQueries.Filter(AuditQueries.Organization(source,a),filter));
        filter.Outcome="succeeded";filter.Action="assignment.graded";Assert.Empty(AuditQueries.Filter(AuditQueries.Organization(source,a),filter));
        filter.Action=first.Action;filter.FromUtc=first.TimestampUtc;filter.ToUtc=second.TimestampUtc;
        Assert.Equal(first.Id,AuditQueries.Filter(AuditQueries.Organization(source,a),filter).Single().Id);
    }
    [Fact] public void Equal_timestamps_use_event_ID_as_a_deterministic_tiebreaker()
    {
        var org=Guid.NewGuid();var rows=new[]{Event(org),Event(org),Event(org)}.AsQueryable();
        Assert.Equal(rows.OrderByDescending(x=>x.Id).Select(x=>x.Id),AuditQueries.Ordered(rows).Select(x=>x.Id));
    }
    [Theory][InlineData(OrganizationRole.Student)][InlineData(OrganizationRole.Instructor)]
    public void Non_admin_members_are_denied_even_with_a_selected_organization(OrganizationRole role)
    {var org=Guid.NewGuid();Assert.Throws<UnauthorizedAccessException>(()=>AuditAccess.Organization(org,org,Guid.NewGuid(),true,false,role));}
    [Fact] public void Admin_and_stored_platform_access_require_explicit_target_and_active_org()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var actor=Guid.NewGuid();
        AuditAccess.Organization(a,a,actor,true,false,OrganizationRole.Admin);AuditAccess.Organization(a,a,actor,true,true,null);
        Assert.Throws<UnauthorizedAccessException>(()=>AuditAccess.Organization(a,b,actor,true,true,null));
        Assert.Throws<UnauthorizedAccessException>(()=>AuditAccess.Organization(null,a,actor,true,true,null));
        Assert.Throws<UnauthorizedAccessException>(()=>AuditAccess.Organization(a,a,actor,false,true,null));
        Assert.Throws<UnauthorizedAccessException>(()=>AuditAccess.Platform(actor,false));AuditAccess.Platform(actor,true);
        Assert.Throws<UnauthorizedAccessException>(()=>AuditAccess.Platform(null,true));
    }
    [Fact] public async Task Repository_rejects_unselected_foreign_scope_before_any_SQL()
    {
        using var db=TenantSqlTests.CreateContext();var selected=TenantSqlTests.Scope(Guid.NewGuid(),OrganizationRole.Admin);var request=new AuditRequestContext();request.Authenticate(selected.UserId);
        var repo=new AuditQueryRepository(db,selected,request,TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repo.Organization(Guid.NewGuid(),new(),default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repo.OrganizationDetail(Guid.NewGuid(),Guid.NewGuid(),default));
    }
    [Theory][InlineData(0,20)][InlineData(10001,20)][InlineData(1,0)][InlineData(1,51)]
    public void Pagination_is_bounded(int page,int size)=>Assert.Throws<ArgumentException>(()=>new AuditFilter {Page=page,PageSize=size}.Normalize(Now));
    [Fact] public void Date_ranges_offsets_action_and_outcome_are_allowlisted()
    {
        Assert.Equal(Now.AddDays(-7),new AuditFilter().Normalize(Now).FromUtc);
        new AuditFilter {FromUtc=Now.AddDays(-31),ToUtc=Now}.Normalize(Now);
        Assert.Throws<ArgumentException>(()=>new AuditFilter {FromUtc=Now.AddDays(-31).AddTicks(-1),ToUtc=Now}.Normalize(Now));
        Assert.Throws<ArgumentException>(()=>new AuditFilter {FromUtc=Now,ToUtc=Now}.Normalize(Now));
        Assert.Throws<ArgumentException>(()=>new AuditFilter {ToUtc=Now.ToOffset(TimeSpan.FromHours(5))}.Normalize(Now));
        Assert.Throws<ArgumentException>(()=>new AuditFilter {Action="<script>"}.Normalize(Now));
        Assert.Throws<ArgumentException>(()=>new AuditFilter {Outcome="200"}.Normalize(Now));
        Assert.Throws<ArgumentException>(()=>new AuditFilter {ResourceId=Guid.Empty}.Normalize(Now));
    }
    [Theory][InlineData("password")][InlineData("jwt")][InlineData("token")][InlineData("tokenHash")][InlineData("securityStamp")]
    [InlineData("email")][InlineData("emailBody")][InlineData("text")][InlineData("feedback")][InlineData("quizAnswer")]
    [InlineData("answerKey")][InlineData("path")][InlineData("exception")][InlineData("body")]
    public void Sensitive_or_arbitrary_metadata_is_rejected(string key)
        =>Assert.Throws<ArgumentException>(()=>AuditMetadata.Validate(JsonSerializer.Serialize(new Dictionary<string,string>{{key,"secret"}})));
    [Theory][InlineData("{\"newRole\":\"SuperAdmin\"}")][InlineData("{\"targetUserId\":\"a@example.test\"}")]
    [InlineData("{\"oldPublished\":\"<script>\"}")][InlineData("{\"newRole\":\"Admin\",\"newRole\":\"Student\"}")]
    public void Metadata_values_types_and_duplicate_keys_are_restricted(string json)=>Assert.Throws<ArgumentException>(()=>AuditMetadata.Validate(json));
    [Fact] public void Metadata_bound_and_action_specific_allowlist_are_enforced()
    {
        Assert.Throws<ArgumentException>(()=>AuditMetadata.Validate(new string(' ',513)));
        Assert.Throws<ArgumentException>(()=>AuditEvent.Create(AuditAction.PasswordReset,Guid.NewGuid(),null,null,Guid.NewGuid(),Guid.NewGuid().ToString("N"),Now,AuditMetadata.Related(Guid.NewGuid())));
        var actor=Guid.NewGuid();var target=Guid.NewGuid();var org=Guid.NewGuid();var resource=Guid.NewGuid();
        var row=AuditEvent.Create(AuditAction.MembershipRoleChanged,resource,org,actor,Guid.NewGuid(),Guid.NewGuid().ToString("N"),Now,AuditMetadata.Roles(OrganizationRole.Student,OrganizationRole.Instructor,target));
        Assert.Equal(actor,row.ActorUserId);Assert.Equal(org,row.OrganizationId);Assert.Equal(resource,row.ResourceId);Assert.Equal("membership.role-changed",row.Action);Assert.Equal("membership",row.ResourceType);
        using var json=JsonDocument.Parse(row.Metadata);Assert.Equal("Student",json.RootElement.GetProperty("oldRole").GetString());Assert.Equal("Instructor",json.RootElement.GetProperty("newRole").GetString());Assert.Equal(target.ToString(),json.RootElement.GetProperty("targetUserId").GetString());
    }
    [Fact] public void Stable_codes_cover_all_instrumented_resources_and_web_filter_actions()
    {
        Assert.Equal(AuditEvent.Actions,AuditDisplay.Actions);Assert.Equal(AuditEvent.Outcomes,AuditDisplay.Outcomes);
        Assert.Equal(18,AuditEvent.Actions.Distinct().Count());
        Assert.Equal("course",AuditEvent.Resource(AuditAction.CourseInstructorChanged));Assert.Equal("assignment-submission",AuditEvent.Resource(AuditAction.AssignmentSubmitted));
        Assert.Equal("assignment-grade",AuditEvent.Resource(AuditAction.AssignmentGraded));Assert.Equal("enrollment",AuditEvent.Resource(AuditAction.EnrollmentCompleted));
        Assert.DoesNotContain(typeof(AuditEvent).GetProperties(),p=>p.Name.Contains("Token")||p.Name.Contains("Password")||p.Name.Contains("Email"));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Audit_updates_and_deletes_are_rejected_before_any_database_access(bool delete)
    {
        using var db=TenantSqlTests.CreateContext();var row=Event(Guid.NewGuid());db.Attach(row);
        if(delete)db.Remove(row);else db.Entry(row).Property(x=>x.Outcome).CurrentValue="unknown";
        Assert.Contains("append-only",Assert.Throws<InvalidOperationException>(()=>db.SaveChanges()).Message);
        Assert.Contains("append-only",(await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync())).Message);
    }
    [Fact] public void Staging_uses_server_actor_and_deduplicates_same_operation_without_saving()
    {
        var org=Guid.NewGuid();var scope=TenantSqlTests.Scope(org,OrganizationRole.Admin);using var db=TenantSqlTests.CreateContext(scope);var resource=Guid.NewGuid();
        db.Stage(AuditAction.InvitationCreated,resource,org,AuditMetadata.InvitationRole(OrganizationRole.Instructor));
        db.Stage(AuditAction.InvitationCreated,resource,org,AuditMetadata.InvitationRole(OrganizationRole.Instructor));
        var entry=Assert.Single(db.ChangeTracker.Entries<AuditEvent>());Assert.Equal(EntityState.Added,entry.State);Assert.Equal(scope.UserId,entry.Entity.ActorUserId);
        Assert.Throws<InvalidOperationException>(()=>db.Stage(AuditAction.InvitationCreated,resource,org,AuditMetadata.InvitationRole(OrganizationRole.Admin)));
        Assert.Throws<InvalidOperationException>(()=>db.SaveChanges()); // No synchronous audit bypass.
    }
    [Fact] public void Delivery_is_a_separate_observation_not_an_invitation_mutation_success()
    {
        var org=Guid.NewGuid();using var db=TenantSqlTests.CreateContext(TenantSqlTests.Scope(org,OrganizationRole.Admin));var id=Guid.NewGuid();
        db.Stage(AuditAction.InvitationCreated,id,org,AuditMetadata.InvitationRole(OrganizationRole.Student));
        var success=db.Observation(AuditAction.InvitationDelivery,id,org,AuditMetadata.Empty(),AuditOutcome.Succeeded);
        var unknown=db.Observation(AuditAction.InvitationDelivery,id,org,AuditMetadata.Empty(),AuditOutcome.Unknown);
        Assert.NotEqual(success.OperationId,unknown.OperationId);Assert.Equal("invitation.delivery",success.Action);Assert.Equal("unknown",unknown.Outcome);
        Assert.Equal(success.CorrelationId,unknown.CorrelationId);Assert.Equal("{}",success.Metadata);
        Assert.Single(db.ChangeTracker.Entries<AuditEvent>()); // Observations aren't staged in the business context.
        Assert.Throws<ArgumentException>(()=>AuditEvent.Create(AuditAction.InvitationCreated,id,org,null,Guid.NewGuid(),Guid.NewGuid().ToString("N"),Now,AuditMetadata.Empty(),AuditOutcome.Unknown));
    }
    [Fact] public void Invitation_reissue_links_internal_IDs_without_emails_tokens_or_hashes()
    {
        var prior=Guid.NewGuid();var current=Guid.NewGuid();var org=Guid.NewGuid();
        var row=AuditEvent.Create(AuditAction.InvitationReissued,current,org,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString("N"),Now,AuditMetadata.Reissue(OrganizationRole.Admin,prior));
        Assert.Equal(current,row.ResourceId);Assert.Contains(prior.ToString(),row.Metadata);Assert.Contains("Admin",row.Metadata);
        Assert.DoesNotContain("email",row.Metadata);Assert.DoesNotContain("token",row.Metadata);Assert.DoesNotContain("hash",row.Metadata);
    }
    [Fact] public void Model_and_additive_migration_preserve_history_and_have_scoped_indexes_no_cascades()
    {
        using var db=TenantSqlTests.CreateContext();var entity=db.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Empty(entity.GetForeignKeys());Assert.Equal(512,entity.FindProperty("Metadata")!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(),i=>i.IsUnique&&i.Properties.Select(x=>x.Name).SequenceEqual(new[]{"OperationId","Action","ResourceType","ResourceId"}));
        Assert.Contains(entity.GetIndexes(),i=>i.Properties.Select(x=>x.Name).SequenceEqual(new[]{"Scope","OrganizationId","TimestampUtc","Id"}));
        var migration=new AddAuditEvents();Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());Assert.All(migration.UpOperations,o=>Assert.True(o is CreateTableOperation or CreateIndexOperation));
        Assert.Contains("THROW 51000",Assert.IsType<SqlOperation>(migration.DownOperations.First()).Sql);
        Assert.Empty(migration.UpOperations.OfType<CreateTableOperation>().Single().ForeignKeys);Assert.False(db.Database.HasPendingModelChanges());
        var sql=db.GetService<IMigrator>().GenerateScript("20261006122454_AddOrganizationInvitations");
        Assert.Contains("CREATE TABLE [AuditEvents]",sql);Assert.DoesNotContain("DELETE FROM",sql);Assert.DoesNotContain("CASCADE",sql);Assert.DoesNotContain("UPDATE [",sql);
    }
    [Fact] public void Real_scoped_queries_translate_to_SQL_with_scope_on_counts_and_details()
    {
        var org=Guid.NewGuid();using var db=TenantSqlTests.CreateContext();var filter=new AuditFilter().Normalize(Now);
        var scope=AuditQueries.Filter(AuditQueries.Organization(db.AuditEvents.AsNoTracking(),org),filter);
        var sql=AuditQueries.Rows(AuditQueries.Ordered(scope).Take(20)).ToQueryString();
        Assert.Contains("[OrganizationId]",sql);Assert.Contains(org.ToString(),sql);Assert.Contains("organization",sql);Assert.Contains("ORDER BY",sql);
        var detail=AuditQueries.Rows(AuditQueries.Organization(db.AuditEvents.AsNoTracking(),org).Where(x=>x.Id==Guid.NewGuid())).ToQueryString();Assert.Contains(org.ToString(),detail);
        var platform=AuditQueries.Platform(db.AuditEvents).ToQueryString();Assert.Contains("[OrganizationId] IS NULL",platform);Assert.Contains("account",platform);
    }
    [Fact] public void API_viewer_has_no_mutations_and_org_scope_is_not_optional()
    {
        var type=typeof(LMS.Api.Controllers.AuditController);Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        foreach(var name in new[]{"Organization","OrganizationDetail","Platform","PlatformDetail"})
        {
            var method=type.GetMethod(name)!;Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());Assert.Null(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.Equal(name.StartsWith("Organization")?"Admin,SuperAdmin":"SuperAdmin",method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
            Assert.Equal(name.StartsWith("Organization"),method.GetCustomAttribute<LMS.Api.Middleware.OrganizationScopeAttribute>() is not null);
        }
    }
    [Theory][InlineData("DELETE FROM [a] FROM [AuditEvents] AS [a]")]
    [InlineData("UPDATE [a] SET [Metadata]=@p0 FROM [AuditEvents] AS [a]")]
    [InlineData("DELETE FROM [dbo].[AuditEvents]")][InlineData("TRUNCATE TABLE [AuditEvents]")]
    public void Bulk_and_raw_audit_mutations_are_denied(string sql)=>Assert.Throws<InvalidOperationException>(()=>AuditCommandGuard.Validate(sql));
    [Fact] public void Business_updates_and_audit_inserts_in_the_same_EF_batch_are_allowed()
        =>AuditCommandGuard.Validate("SET NOCOUNT ON; UPDATE [Courses] SET [IsPublished]=@p0 WHERE [Id]=@p1; INSERT INTO [AuditEvents] ([Id],[Metadata]) VALUES (@p2,@p3);");
}
