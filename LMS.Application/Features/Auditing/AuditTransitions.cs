using LMS.Domain.Entities;
using LMS.Domain.Enums;
namespace LMS.Application.Features.Auditing;
public sealed record AuditTransition(AuditAction Action,Guid ResourceId,Guid OrganizationId,AuditMetadata Metadata);
public static class AuditTransitions
{
    public static IEnumerable<AuditTransition> Course(Course stored,Course changed,Guid organization)
    {
        if(stored.Id!=changed.Id||stored.OrganizationId!=organization||changed.OrganizationId!=organization)throw new UnauthorizedAccessException("Stored audit resource mismatch.");
        if(stored.InstructorId!=changed.InstructorId)yield return new(AuditAction.CourseInstructorChanged,stored.Id,organization,AuditMetadata.Instructors(stored.InstructorId,changed.InstructorId));
        var publication=Publication(AuditAction.CoursePublicationChanged,stored.Id,organization,stored.IsPublished,changed.IsPublished);
        if(publication is not null)yield return publication;
    }
    public static AuditTransition? Publication(AuditAction action,Guid id,Guid org,bool before,bool after)
    {
        if(action is not (AuditAction.CoursePublicationChanged or AuditAction.LessonPublicationChanged or AuditAction.QuizPublicationChanged or AuditAction.AssignmentPublicationChanged))throw new ArgumentException("Invalid publication action.");
        return before==after?null:new(action,id,org,AuditMetadata.Publication(before,after));
    }
    public static AuditTransition? Enrollment(Guid id,Guid course,Guid org,EnrollmentStatus before,EnrollmentStatus after)
        =>before==after||after is not (EnrollmentStatus.Cancelled or EnrollmentStatus.Completed)?null:
            new(after==EnrollmentStatus.Cancelled?AuditAction.EnrollmentCancelled:AuditAction.EnrollmentCompleted,id,org,AuditMetadata.Related(course));
    public static AuditTransition Submission(AssignmentSubmission stored,Guid actor,Guid org)
    {
        if(stored.UserId!=actor)throw new UnauthorizedAccessException("Stored submission actor mismatch.");
        return new(AuditAction.AssignmentSubmitted,stored.Id,org,AuditMetadata.Related(stored.AssignmentId));
    }
    public static AuditTransition Grade(AssignmentGrade stored,Guid actor,Guid org)
    {
        if(stored.GraderId!=actor)throw new UnauthorizedAccessException("Stored grading actor mismatch.");
        return new(AuditAction.AssignmentGraded,stored.Id,org,AuditMetadata.Related(stored.SubmissionId));
    }
}
