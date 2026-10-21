using System.Data;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LMS.Infrastructure.Persistence;

public partial class LMSDbContext
{
    internal IQueryable<Organization> OrganizationWriteLock(Guid id)
        => Organizations.FromSqlInterpolated($"SELECT * FROM [Organizations] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").AsNoTracking();

    private bool NeedsMembershipWriteGuard() => ChangeTracker.Entries().Any(e =>
        e.Entity is Course && (e.State == EntityState.Added || e.State == EntityState.Modified && e.Property("InstructorId").IsModified)
        || e.Entity is CourseEnrollment enrollment && enrollment.Status == EnrollmentStatus.Active
            && (e.State == EntityState.Added || e.State == EntityState.Modified && e.Property("Status").IsModified));

    private async Task<int> SaveWithMembershipGuard(bool acceptAllChanges, CancellationToken token)
    {
        if (CurrentOrganizationId == Guid.Empty) throw new UnauthorizedAccessException("Select a verified organization before writing learning data.");
        if(ChangeTracker.Entries<Course>().Any(e=>e.State is EntityState.Added or EntityState.Modified && e.Entity.OrganizationId!=CurrentOrganizationId))
            throw new UnauthorizedAccessException("Course must belong to the selected organization.");
        // Enrollment insertion/reactivation and course creation/reassignment must recheck membership
        // while holding the same organization lock used by role changes. Existing outer transactions
        // keep the lock through their commit; otherwise this save owns a transaction.
        var ownsTransaction = Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        var organization=await OrganizationWriteLock(CurrentOrganizationId).SingleOrDefaultAsync(token);
        if(organization is null || !organization.IsActive)throw new UnauthorizedAccessException("Organization access denied.");
        var actorRole = await OrganizationMemberships.AsNoTracking().Where(m=>m.OrganizationId==CurrentOrganizationId && m.UserId==CurrentUserId)
            .Select(m=>(OrganizationRole?)m.Role).SingleOrDefaultAsync(token);
        var platform = await (from a in UserRoles join r in Roles on a.RoleId equals r.Id
                              where a.UserId==CurrentUserId && r.NormalizedName=="SUPERADMIN" select a.UserId).AnyAsync(token);
        foreach (var entry in ChangeTracker.Entries().ToArray())
        {
            Guid? user = null; OrganizationRole? required = null;
            if (entry.Entity is Course course && (entry.State == EntityState.Added || entry.State == EntityState.Modified && entry.Property("InstructorId").IsModified))
            {
                if (!platform && actorRole != OrganizationRole.Admin && !(actorRole==OrganizationRole.Instructor && entry.State==EntityState.Added && course.InstructorId==CurrentUserId))
                    throw new UnauthorizedAccessException("Stored authorization changed. Refresh before assigning courses.");
                user = course.InstructorId; required = OrganizationRole.Instructor;
            }
            if (entry.Entity is CourseEnrollment enrollment && enrollment.Status == EnrollmentStatus.Active
                && (entry.State == EntityState.Added || entry.State == EntityState.Modified && entry.Property("Status").IsModified))
            { user = enrollment.UserId; required = OrganizationRole.Student; }
            if (user.HasValue && !await OrganizationMemberships.AsNoTracking().AnyAsync(m => m.OrganizationId == CurrentOrganizationId && m.UserId == user && m.Role == required, token))
                throw new InvalidOperationException("Stored membership changed. Refresh authorization before assigning a course or starting an enrollment.");
        }
        await ValidateLearningWritesAsync(token);
        var result = await SaveBusinessAndAudit(acceptAllChanges, token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return result;
    }
}
