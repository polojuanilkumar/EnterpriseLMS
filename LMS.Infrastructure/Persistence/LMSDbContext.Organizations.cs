using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LMS.Infrastructure.Persistence;

public partial class LMSDbContext
{
    private readonly OrganizationContext? _organization;
    public Guid CurrentOrganizationId => _organization?.OrganizationId ?? Guid.Empty;
    public Guid CurrentUserId => _organization?.UserId ?? Guid.Empty;
    public bool RestrictToAssignedCourses => _organization?.Role == LMS.Domain.Enums.OrganizationRole.Instructor
        && !_organization.IsSuperAdmin;
    public bool CanManageContent => _organization?.CanManageContent == true;
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<LessonContentItem> LessonContentItems => Set<LessonContentItem>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<LessonAssignment> LessonAssignments => Set<LessonAssignment>();
    public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
    public DbSet<AssignmentGrade> AssignmentGrades => Set<AssignmentGrade>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();

    private void ConfigureOrganizationFilters(ModelBuilder model)
    {
        // Context properties are SQL parameters, not captured constants in EF's cached model.
        // Child filters traverse stored parents. No client-supplied organization on child entities.
        model.Entity<Course>().HasQueryFilter(x => CurrentOrganizationId != Guid.Empty
            && x.OrganizationId != null && x.OrganizationId == CurrentOrganizationId);
        model.Entity<CourseSection>().HasQueryFilter(x => Courses.Any(c => c.Id == x.CourseId));
        model.Entity<Lesson>().HasQueryFilter(x => CourseSections.Any(s => s.Id == x.SectionId));
        model.Entity<MediaAsset>().HasQueryFilter(x => x.OrganizationId == CurrentOrganizationId && Lessons.Any(l => l.Id == x.LessonId));
        model.Entity<LessonContentItem>().HasQueryFilter(x => Lessons.Any(l => l.Id == x.LessonId));
        model.Entity<Quiz>().HasQueryFilter(x => Lessons.Any(l => l.Id == x.LessonId));
        model.Entity<LessonAssignment>().HasQueryFilter(x => Lessons.Any(l => l.Id == x.LessonId));
        model.Entity<AssignmentSubmission>().HasQueryFilter(x => LessonAssignments.Any(a => a.Id == x.AssignmentId));
        model.Entity<AssignmentGrade>().HasQueryFilter(x => AssignmentSubmissions.Any(s => s.Id == x.SubmissionId));
        model.Entity<QuizQuestion>().HasQueryFilter(x => Quizzes.Any(q => q.Id == x.QuizId));
        model.Entity<QuizOption>().HasQueryFilter(x => QuizQuestions.Any(q => q.Id == x.QuestionId));
        model.Entity<CourseEnrollment>().HasQueryFilter(x => Courses.Any(c => c.Id == x.CourseId));
        model.Entity<LessonProgress>().HasQueryFilter(x => Lessons.Any(l => l.Id == x.LessonId));
        model.Entity<QuizAttempt>().HasQueryFilter(x => Quizzes.Any(q => q.Id == x.QuizId));
        model.Entity<QuizAttemptAnswer>().HasQueryFilter(x => QuizAttempts.Any(a => a.Id == x.AttemptId));
        model.Entity<QuizAttemptAnswerOption>().HasQueryFilter(x => QuizAttemptAnswers.Any(a => a.Id == x.AnswerId));
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAuditWrites();
        if (NeedsMembershipWriteGuard()) return await SaveWithMembershipGuard(acceptAllChangesOnSuccess, cancellationToken);
        await ValidateLearningWritesAsync(cancellationToken);
        return await SaveBusinessAndAudit(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditWrites();
        if(ChangeTracker.Entries<AuditEvent>().Any(e=>e.State==EntityState.Added))throw new InvalidOperationException("Audit changes must use SaveChangesAsync.");
        // Production repositories use async saving. Reject synchronous learning writes rather than bypass validation.
        if (ChangeTracker.Entries().Any(e => IsLearningEntity(e.Entity) && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Learning changes must use SaveChangesAsync.");
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private static bool IsLearningEntity(object entity) => entity is Course or CourseSection or Lesson or LessonContentItem or MediaAsset or Quiz
        or QuizQuestion or QuizOption or CourseEnrollment or LessonProgress or QuizAttempt or QuizAttemptAnswer or QuizAttemptAnswerOption or LessonAssignment or AssignmentSubmission or AssignmentGrade;

    private async Task ValidateLearningWritesAsync(CancellationToken token)
    {
        var entries = ChangeTracker.Entries().Where(e => IsLearningEntity(e.Entity) &&
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        if (entries.Count == 0) return;
        if (CurrentOrganizationId == Guid.Empty) throw new UnauthorizedAccessException("Select a verified organization before writing learning data.");
        foreach (var entry in entries)
        {
            if(entry.Entity is AssignmentSubmission or AssignmentGrade)
                if(entry.State!=EntityState.Added)throw new InvalidOperationException("Submitted work and grades are immutable and retained.");
            if(entry.Entity is LessonAssignment&&entry.State==EntityState.Deleted)throw new InvalidOperationException("Assignment deletion is not supported.");
            if (entry.Entity is MediaAsset media)
            {
                if (entry.State != EntityState.Added) throw new InvalidOperationException("Committed media assets are immutable and retained for learner history.");
                if (media.OrganizationId != CurrentOrganizationId) throw new UnauthorizedAccessException("Foreign media organization.");
            }
            if (entry.Entity is LessonContentItem item && entry.State != EntityState.Deleted && item.AssetId.HasValue)
            {
                if (!await MediaAssets.AnyAsync(a => a.Id == item.AssetId && a.LessonId == item.LessonId && a.Type == item.Type, token))
                    throw new UnauthorizedAccessException("Media asset does not match content ownership/type.");
            }
            if (entry.State != EntityState.Added)
            {
                // Parent IDs and ownership cannot be changed via tracked or attached updates.
                foreach (var property in entry.Properties.Where(p => p.Metadata.Name is "OrganizationId" or "CourseId" or "SectionId"
                    or "LessonId" or "QuizId" or "QuestionId" or "AttemptId" or "AnswerId" or "AssignmentId" or "SubmissionId"))
                    if (property.IsModified) throw new InvalidOperationException("Learning resource ownership cannot be changed.");
                var id = (Guid)entry.Property("Id").CurrentValue!;
                var visible = entry.Entity switch
                {
                    Course => await Courses.AnyAsync(x => x.Id == id, token),
                    CourseSection => await CourseSections.AnyAsync(x => x.Id == id, token),
                    Lesson => await Lessons.AnyAsync(x => x.Id == id, token),
                    LessonContentItem => await LessonContentItems.AnyAsync(x => x.Id == id, token),
                    Quiz => await Quizzes.AnyAsync(x => x.Id == id, token),
                    LessonAssignment => await LessonAssignments.AnyAsync(x => x.Id == id, token),
                    QuizQuestion => await QuizQuestions.AnyAsync(x => x.Id == id, token),
                    QuizOption => await QuizOptions.AnyAsync(x => x.Id == id, token),
                    CourseEnrollment => await CourseEnrollments.AnyAsync(x => x.Id == id, token),
                    LessonProgress => await LessonProgresses.AnyAsync(x => x.Id == id, token),
                    QuizAttempt => await QuizAttempts.AnyAsync(x => x.Id == id, token),
                    QuizAttemptAnswer => await QuizAttemptAnswers.AnyAsync(x => x.Id == id, token),
                    QuizAttemptAnswerOption => await QuizAttemptAnswerOptions.AnyAsync(x => x.Id == id, token),
                    _ => false
                };
                if (!visible) throw new UnauthorizedAccessException("Learning resource is outside the selected organization.");
            }
            if (entry.Entity is Course course)
            {
                if (course.OrganizationId != CurrentOrganizationId)
                    throw new UnauthorizedAccessException("Course must belong to the selected organization.");
                continue;
            }
            if (entry.State != EntityState.Added) continue;
            var parentVisible = entry.Entity switch
            {
                CourseSection x => await Courses.AnyAsync(p => p.Id == x.CourseId, token),
                CourseEnrollment x => await Courses.AnyAsync(p => p.Id == x.CourseId, token),
                Lesson x => await CourseSections.AnyAsync(p => p.Id == x.SectionId, token),
                MediaAsset x => await Lessons.AnyAsync(p => p.Id == x.LessonId, token),
                LessonContentItem x => await Lessons.AnyAsync(p => p.Id == x.LessonId, token),
                LessonProgress x => await Lessons.AnyAsync(p => p.Id == x.LessonId, token),
                Quiz x => await Lessons.AnyAsync(p => p.Id == x.LessonId, token),
                LessonAssignment x => CanManageContent && await Lessons.AnyAsync(p => p.Id == x.LessonId, token),
                AssignmentSubmission x => _organization?.Role==LMS.Domain.Enums.OrganizationRole.Student && x.UserId==CurrentUserId && await LessonAssignments.AnyAsync(p => p.Id == x.AssignmentId, token),
                AssignmentGrade x => CanManageContent && x.GraderId==CurrentUserId && await AssignmentSubmissions.AnyAsync(p => p.Id == x.SubmissionId, token),
                QuizQuestion x => await Quizzes.AnyAsync(p => p.Id == x.QuizId, token),
                QuizOption x => await QuizQuestions.AnyAsync(p => p.Id == x.QuestionId, token),
                QuizAttempt x => await Quizzes.AnyAsync(p => p.Id == x.QuizId, token),
                QuizAttemptAnswer x => await QuizAttempts.AnyAsync(p => p.Id == x.AttemptId, token),
                QuizAttemptAnswerOption x => entries.Any(e => e.State == EntityState.Added && e.Entity is QuizAttemptAnswer a && a.Id == x.AnswerId)
                    || await QuizAttemptAnswers.AnyAsync(p => p.Id == x.AnswerId, token),
                _ => false
            };
            if (!parentVisible) throw new UnauthorizedAccessException("Parent resource is outside the selected organization.");
        }
    }
}
