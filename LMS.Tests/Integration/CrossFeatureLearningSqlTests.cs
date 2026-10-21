using LMS.Application.Features.Assignments;
using LMS.Application.Features.Auditing;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Identity;
using LMS.Infrastructure.Persistence;
using LMS.Infrastructure.Repositories;
using LMS.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace LMS.Tests.Integration;

public sealed partial class CrossFeatureSqlTests
{
    private async Task<Guid> Member(Graph g, OrganizationRole role)
    {
        var user = Guid.NewGuid(); var email = user.ToString("N") + "@example.invalid";
        await using var db = sql.Context();
        db.Users.Add(new ApplicationUser { Id = user, UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString("N"), EmailConfirmed = true });
        db.OrganizationMemberships.Add(new OrganizationMembership(g.Org.Id, user, role)); await db.SaveChangesAsync(); return user;
    }
    [SqlIntegrationFact]
    public async Task Role_change_races_instructor_assignment_without_invalid_stored_assignment()
    {
        var g = await Seed(); var target = await Member(g, OrganizationRole.Instructor);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Assign()
        {
            await using var db = sql.Context(g.Org.Id, g.Admin); await start.Task;
            try { (await db.Courses.SingleAsync()).AssignInstructor(target); await db.SaveChangesAsync(); return true; }
            catch (InvalidOperationException) { return false; }
        }
        async Task<bool> Change()
        {
            await using var db = sql.Context(g.Org.Id, g.Admin); await start.Task;
            try { await new OrganizationRepository(db, Scope(g)).SetMembershipAsync(g.Org.Id, target, OrganizationRole.Student); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var assign = Assign(); var change = Change(); start.SetResult();
        Assert.Equal(1, (await Task.WhenAll(assign, change).WaitAsync(TimeSpan.FromSeconds(30))).Count(x => x));
        await using var verify = sql.Context(g.Org.Id, g.Admin);
        var membership = await verify.OrganizationMemberships.SingleAsync(x => x.UserId == target);
        var stored = await verify.Courses.SingleAsync();
        Assert.Equal(membership.Role == OrganizationRole.Instructor, stored.InstructorId == target);
    }
    [SqlIntegrationFact]
    public async Task Role_change_races_active_enrollment_without_invalid_student_membership()
    {
        var g = await Seed(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Enroll()
        {
            await using var db = sql.Context(g.Org.Id, g.Student, OrganizationRole.Student); await start.Task;
            try { db.Add(new CourseEnrollment(g.Course.Id, g.Student)); await db.SaveChangesAsync(); return true; }
            catch (InvalidOperationException) { return false; }
        }
        async Task<bool> Change()
        {
            await using var db = sql.Context(g.Org.Id, g.Admin); await start.Task;
            try { await new OrganizationRepository(db, Scope(g)).SetMembershipAsync(g.Org.Id, g.Student, OrganizationRole.Instructor); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var enrollment = Enroll(); var change = Change(); start.SetResult();
        Assert.Equal(1, (await Task.WhenAll(enrollment, change).WaitAsync(TimeSpan.FromSeconds(30))).Count(x => x));
        await using var verify = sql.Context(g.Org.Id, g.Admin);
        Assert.Equal(await verify.CourseEnrollments.AnyAsync(),
            (await verify.OrganizationMemberships.SingleAsync(x => x.UserId == g.Student)).Role == OrganizationRole.Student);
    }
    private AssignmentHandlers Assignments(LMSDbContext db, Graph g, Guid actor, OrganizationRole role, TimeProvider? clock = null)
        => new(new AssignmentRepository(db), new AssignmentStorage(Path.Combine(sql.PrivateRoot, "assignments"),
            Path.Combine(sql.PrivateRoot, "wwwroot")), Scope(g, actor, role), clock ?? TimeProvider.System);
    private async Task<LessonAssignment> Assignment(Graph g, DateTime? due = null)
    {
        var assignment = new LessonAssignment(g.Lesson.Id, "Synthetic assignment", "Synthetic instructions", due, 10, DateTime.UtcNow);
        assignment.Publish(true, DateTime.UtcNow);
        await using var db = sql.Context(g.Org.Id, g.Admin);
        db.Add(new CourseEnrollment(g.Course.Id, g.Student)); await db.SaveChangesAsync();
        db.Add(assignment); await db.SaveChangesAsync(); return assignment;
    }
    [SqlIntegrationFact]
    public async Task Final_submission_uniqueness_frozen_fields_single_grade_and_audit_are_enforced()
    {
        var g = await Seed(); var assignment = await Assignment(g, DateTime.UtcNow.AddHours(1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Submit()
        {
            await using var db = sql.Context(g.Org.Id, g.Student, OrganizationRole.Student); await start.Task;
            try { await Assignments(db, g, g.Student, OrganizationRole.Student).Submit(g.Lesson.Id, assignment.Id, "Synthetic answer", true, null, null, null, null, default); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var first = Submit(); var second = Submit(); start.SetResult();
        Assert.Equal(1, (await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30))).Count(x => x));
        Guid submission;
        await using (var db = sql.Context(g.Org.Id, g.Admin))
        {
            submission = (await db.AssignmentSubmissions.SingleAsync()).Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Assignments(db, g, g.Admin, OrganizationRole.Admin).Edit(g.Lesson.Id, assignment.Id,
                new AssignmentRequest { Title = assignment.Title, Instructions = "Changed", MaximumMarks = 10, DueAtUtc = new DateTimeOffset(DateTime.SpecifyKind(assignment.DueAtUtc!.Value, DateTimeKind.Utc)) }, default));
        }
        await using (var db = sql.Context(g.Org.Id, g.Instructor, OrganizationRole.Instructor))
        { await Assignments(db, g, g.Instructor, OrganizationRole.Instructor).Grade(g.Lesson.Id, assignment.Id, submission, new(8, "Synthetic feedback", true), default); }
        await using (var db = sql.Context(g.Org.Id, g.Instructor, OrganizationRole.Instructor))
        { await Assert.ThrowsAsync<InvalidOperationException>(() => Assignments(db, g, g.Instructor, OrganizationRole.Instructor).Grade(g.Lesson.Id, assignment.Id, submission, new(9, "Changed feedback", true), default)); }
        await using var verify = sql.Context(g.Org.Id, g.Admin);
        Assert.Equal(1, await verify.AssignmentGrades.CountAsync());
        var events = await verify.AuditEvents.Where(x => x.OrganizationId == g.Org.Id).ToArrayAsync();
        Assert.Equal(2, events.Length); Assert.Single(events, x => x.Action == "assignment.submitted"); Assert.Single(events, x => x.Action == "assignment.graded");
        Assert.All(events, x => { Assert.DoesNotContain("Synthetic", x.Metadata); Assert.DoesNotContain("feedback", x.Metadata); });
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    [SqlIntegrationFact]
    public async Task Deadline_boundary_and_unassigned_instructor_deny_without_false_audit_success()
    {
        var g = await Seed(); var due = DateTimeOffset.UtcNow.AddMinutes(5); var assignment = await Assignment(g, due.UtcDateTime);
        await using (var db = sql.Context(g.Org.Id, g.Student, OrganizationRole.Student))
        { await Assert.ThrowsAsync<InvalidOperationException>(() => Assignments(db, g, g.Student, OrganizationRole.Student, new FixedClock(due))
            .Submit(g.Lesson.Id, assignment.Id, "Synthetic answer", true, null, null, null, null, default)); }
        var unassigned = await Member(g, OrganizationRole.Instructor);
        await using (var db = sql.Context(g.Org.Id, unassigned, OrganizationRole.Instructor))
        { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Assignments(db, g, unassigned, OrganizationRole.Instructor).Context(g.Lesson.Id, true, default)); }
        await using var verify = sql.Context(g.Org.Id, g.Admin);
        Assert.Equal(0, await verify.AssignmentSubmissions.CountAsync()); Assert.Equal(0, await verify.AuditEvents.CountAsync(x => x.OrganizationId == g.Org.Id));
    }
    [SqlIntegrationFact]
    public async Task Stored_SuperAdmin_platform_view_excludes_tenants_and_requires_explicit_target()
    {
        var g = await Seed();
        await using (var db = sql.Context())
        {
            var role = await db.Roles.SingleOrDefaultAsync(x => x.NormalizedName == "SUPERADMIN");
            if (role is null) { role = new IdentityRole<Guid>("SuperAdmin") { Id = Guid.NewGuid(), NormalizedName = "SUPERADMIN" }; db.Roles.Add(role); }
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = g.Admin, RoleId = role.Id }); await db.SaveChangesAsync();
        }
        await using (var db = sql.Context(g.Org.Id, g.Admin)) { (await db.Courses.SingleAsync()).Archive(); await db.SaveChangesAsync(); }
        await using var read = sql.Context(); var actor = new AuditRequestContext(); actor.Authenticate(g.Admin);
        var repo = new AuditQueryRepository(read, new OrganizationContext(), actor, TimeProvider.System);
        Assert.All((await repo.Platform(new(), default)).Items, x => Assert.Null(x.OrganizationId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.Organization(g.Org.Id, new(), default));
        var selected = new OrganizationContext(); selected.Initialize(g.Org.Id, g.Admin, null, true);
        var org = await new AuditQueryRepository(read, selected, actor, TimeProvider.System).Organization(g.Org.Id, new(), default);
        Assert.Equal(1, org.Total); Assert.All(org.Items, x => Assert.Equal(g.Org.Id, x.OrganizationId));
    }
}
