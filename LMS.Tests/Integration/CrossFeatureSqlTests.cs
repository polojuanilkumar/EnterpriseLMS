using System.Data.Common;
using LMS.Application.Features.Assignments;
using LMS.Application.Features.Auditing;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Identity;
using LMS.Infrastructure.Persistence;
using LMS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LMS.Tests.Integration;

[Collection("Disposable SQL"), Trait("Category", "SqlServerIntegration")]
public sealed partial class CrossFeatureSqlTests(DisposableSqlServer sql)
{
    private sealed record Graph(Organization Org, Guid Admin, Guid Instructor, Guid Student, Course Course, Lesson Lesson);
    private async Task<Graph> Seed()
    {
        var org = new Organization("T" + Guid.NewGuid().ToString("N"), "Synthetic organization");
        var admin = Guid.NewGuid(); var instructor = Guid.NewGuid(); var student = Guid.NewGuid();
        await using (var db = sql.Context())
        {
            db.Organizations.Add(org);
            foreach (var (id, role) in new[] { (admin, OrganizationRole.Admin), (instructor, OrganizationRole.Instructor), (student, OrganizationRole.Student) })
            {
                var email = id.ToString("N") + "@example.invalid";
                db.Users.Add(new ApplicationUser { Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("N") });
                db.OrganizationMemberships.Add(new OrganizationMembership(org.Id, id, role));
            }
            await db.SaveChangesAsync(); // Synthetic fixture setup, not a production operation.
        }
        var course = new Course("Synthetic course", "C" + Guid.NewGuid().ToString("N"), "Synthetic", instructor, CourseLevel.Beginner);
        course.AssignOrganization(org.Id); course.Publish();
        var section = new CourseSection(course.Id, "Section", null, 1);
        var lesson = new Lesson(section.Id, "Lesson", null, LessonType.Text, "Synthetic", null, 1, 1);
        lesson.Publish();
        await using (var db = sql.Context(org.Id, admin))
        {
            db.Courses.Add(course); await db.SaveChangesAsync();
            db.CourseSections.Add(section); await db.SaveChangesAsync();
            db.Lessons.Add(lesson); await db.SaveChangesAsync();
        }
        return new(org, admin, instructor, student, course, lesson);
    }
    private static OrganizationContext Scope(Graph g, Guid? actor = null, OrganizationRole role = OrganizationRole.Admin)
    { var scope = new OrganizationContext(); scope.Initialize(g.Org.Id, actor ?? g.Admin, role, false); return scope; }

    [SqlIntegrationFact]
    public async Task Two_tenant_lists_counts_nested_details_and_attached_mutations_are_isolated()
    {
        var a = await Seed(); var b = await Seed();
        await using var db = sql.Context(a.Org.Id, a.Admin);
        Assert.Equal(a.Course.Id, Assert.Single(await db.Courses.AsNoTracking().ToArrayAsync()).Id);
        Assert.Equal(1, await db.Lessons.CountAsync());
        Assert.Null(await db.Courses.SingleOrDefaultAsync(x => x.Id == b.Course.Id));
        Assert.Null(await db.Lessons.SingleOrDefaultAsync(x => x.Id == b.Lesson.Id));
        Assert.Null(await new AssignmentRepository(db).Parent(b.Lesson.Id, default));
        db.Attach(b.Course); b.Course.Archive();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        await using var verify = sql.Context(b.Org.Id, b.Admin);
        Assert.True((await verify.Courses.SingleAsync()).IsPublished);
        Assert.Empty(await verify.AuditEvents.Where(x => x.OrganizationId == b.Org.Id).ToArrayAsync());
    }

    [SqlIntegrationFact]
    public async Task Audit_totals_filters_pages_foreign_ids_and_stored_authority_are_enforced()
    {
        var a = await Seed(); var b = await Seed();
        foreach (var g in new[] { a, b })
        {
            await using var write = sql.Context(g.Org.Id, g.Admin);
            var course = await write.Courses.SingleAsync(); course.Archive(); await write.SaveChangesAsync();
        }
        await using var db = sql.Context(a.Org.Id, a.Admin);
        var request = new AuditRequestContext(); request.Authenticate(a.Admin);
        var repo = new AuditQueryRepository(db, Scope(a), request, TimeProvider.System);
        var page = await repo.Organization(a.Org.Id, new AuditFilter { PageSize = 1, ActorId = a.Admin }, default);
        Assert.Equal(1, page.Total); Assert.Equal(a.Course.Id, Assert.Single(page.Items).ResourceId);
        Assert.Equal(1, (await repo.Organization(a.Org.Id, new AuditFilter { Action = "course.publication-changed", Outcome = "succeeded", ResourceId = a.Course.Id }, default)).Total);
        Assert.Equal(0, (await repo.Organization(a.Org.Id, new AuditFilter { ActorId = b.Admin }, default)).Total);
        Assert.Empty((await repo.Organization(a.Org.Id, new AuditFilter { Page = 2, PageSize = 1 }, default)).Items);
        var foreign = await db.AuditEvents.SingleAsync(x => x.OrganizationId == b.Org.Id);
        Assert.Null(await repo.OrganizationDetail(a.Org.Id, foreign.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.Organization(b.Org.Id, new(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.Platform(new(), default));
        foreach (var (actor, role) in new[] { (a.Instructor, OrganizationRole.Instructor), (a.Student, OrganizationRole.Student) })
        {
            var denied = new AuditRequestContext(); denied.Authenticate(actor);
            var restricted = new AuditQueryRepository(db, Scope(a, actor, role), denied, TimeProvider.System);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restricted.Organization(a.Org.Id, new(), default));
        }
        // A server-context claim alone does not establish stored global authority.
        var spoof = new OrganizationContext(); spoof.Initialize(a.Org.Id, a.Student, null, true);
        var spoofActor = new AuditRequestContext(); spoofActor.Authenticate(a.Student);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new AuditQueryRepository(db, spoof, spoofActor, TimeProvider.System).Organization(a.Org.Id, new(), default));
    }

    [SqlIntegrationFact]
    public async Task Concurrent_duplicate_enrollment_has_one_row_and_expected_conflict()
    {
        var g = await Seed(); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var arrived = 0;
        async Task<string> Enroll()
        {
            await using var db = sql.Context(g.Org.Id, g.Student, OrganizationRole.Student);
            Assert.False(await db.CourseEnrollments.AnyAsync(x => x.CourseId == g.Course.Id && x.UserId == g.Student));
            if (Interlocked.Increment(ref arrived) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            db.CourseEnrollments.Add(new CourseEnrollment(g.Course.Id, g.Student));
            try { await new UnitOfWork(db).SaveChangesAsync(); return "created"; }
            catch (InvalidOperationException ex) when (ex.Message == "Student is already enrolled in this course.") { return "conflict"; }
        }
        var results = await Task.WhenAll(Enroll(), Enroll()).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(new[] { "conflict", "created" }, results.Order().ToArray());
        await using var verify = sql.Context(g.Org.Id, g.Admin);
        Assert.Equal(1, await verify.CourseEnrollments.CountAsync());
    }

    [SqlIntegrationFact]
    public async Task Completion_cancel_race_serializes_one_transition_and_one_audit()
    {
        var g = await Seed(); var enrollment = new CourseEnrollment(g.Course.Id, g.Student);
        await using (var db = sql.Context(g.Org.Id, g.Student, OrganizationRole.Student)) { db.Add(enrollment); await db.SaveChangesAsync(); }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Transition(bool complete)
        {
            await using var db = sql.Context(g.Org.Id, g.Student, OrganizationRole.Student);
            await start.Task;
            try
            {
                await new CourseEnrollmentRepository(db).ExecuteEnrollmentTransactionAsync(enrollment.Id, async ct =>
                {
                    var stored = await db.CourseEnrollments.SingleAsync(x => x.Id == enrollment.Id, ct);
                    if (stored.Status != EnrollmentStatus.Active) throw new InvalidOperationException("Terminal state.");
                    if (complete) stored.Complete(); else stored.Cancel();
                    await db.SaveChangesAsync(ct);
                }); return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        var completion = Transition(true); var cancellation = Transition(false); start.SetResult();
        Assert.Equal(1, (await Task.WhenAll(completion, cancellation).WaitAsync(TimeSpan.FromSeconds(30))).Count(x => x));
        await using var verify = sql.Context(g.Org.Id, g.Admin);
        var stored = await verify.CourseEnrollments.SingleAsync();
        Assert.Equal(enrollment.EnrolledAt, stored.EnrolledAt);
        Assert.Equal(stored.Status == EnrollmentStatus.Completed, stored.CompletedAt.HasValue);
        Assert.Equal(1, await verify.AuditEvents.CountAsync(x => x.ResourceId == enrollment.Id));
    }

    private sealed class FailAudit : DbCommandInterceptor
    {
        public bool Triggered;
        public bool BusinessCommandCompleted;
        private void Check(DbCommand command)
        { if (command.CommandText.Contains("INSERT INTO [AuditEvents]", StringComparison.Ordinal)) { Triggered = true; throw new InvalidOperationException("Synthetic audit failure."); } }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        { if (command.CommandText.Contains("UPDATE [Courses]", StringComparison.Ordinal)) BusinessCommandCompleted = true; return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken token = default)
        { if (command.CommandText.Contains("UPDATE [Courses]", StringComparison.Ordinal)) BusinessCommandCompleted = true; return ValueTask.FromResult(result); }
    }
    [SqlIntegrationFact]
    public async Task Audit_failure_rolls_back_business_and_outer_rollback_removes_success()
    {
        var g = await Seed();
        var failure = new FailAudit();
        await using (var failing = sql.Context(g.Org.Id, g.Admin, interceptors: failure))
        {
            await using var tx = await failing.Database.BeginTransactionAsync();
            // Force an earlier SQL business write, independent of EF's insert/update batch ordering.
            await failing.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Courses] SET [Description] = {"Synthetic uncommitted change"} WHERE [Id] = {g.Course.Id}");
            (await failing.Courses.SingleAsync()).Archive(); await Assert.ThrowsAnyAsync<Exception>(() => failing.SaveChangesAsync());
        }
        Assert.True(failure.Triggered); Assert.True(failure.BusinessCommandCompleted);
        await using (var outer = sql.Context(g.Org.Id, g.Admin))
        {
            await using var tx = await outer.Database.BeginTransactionAsync();
            (await outer.Courses.SingleAsync()).Archive(); await outer.SaveChangesAsync();
            Assert.Equal(1, await outer.AuditEvents.CountAsync(x => x.OrganizationId == g.Org.Id));
            await tx.RollbackAsync();
        }
        await using var verify = sql.Context(g.Org.Id, g.Admin);
        Assert.True((await verify.Courses.SingleAsync()).IsPublished);
        Assert.Equal("Synthetic", (await verify.Courses.SingleAsync()).Description);
        Assert.Equal(0, await verify.AuditEvents.CountAsync(x => x.OrganizationId == g.Org.Id));
    }

    [SqlIntegrationFact]
    public async Task Repeated_saves_emit_one_verified_publication_success()
    {
        var g = await Seed();
        await using (var db = sql.Context(g.Org.Id, g.Admin))
        { (await db.Courses.SingleAsync()).Archive(); await db.SaveChangesAsync(); await db.SaveChangesAsync(); }
        await using var verify = sql.Context(g.Org.Id, g.Admin);
        var audit = await verify.AuditEvents.SingleAsync(x => x.OrganizationId == g.Org.Id);
        Assert.Equal(g.Admin, audit.ActorUserId); Assert.Equal(g.Course.Id, audit.ResourceId);
        Assert.Equal("course.publication-changed", audit.Action); Assert.Equal("succeeded", audit.Outcome);
        Assert.DoesNotContain("Synthetic", audit.Metadata); Assert.DoesNotContain("example.invalid", audit.Metadata);
    }
}
