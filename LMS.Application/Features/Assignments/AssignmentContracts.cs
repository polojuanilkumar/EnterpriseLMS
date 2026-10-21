using System.ComponentModel.DataAnnotations;
using LMS.Domain.Entities;
using LMS.Application.Interfaces.Lessons;
namespace LMS.Application.Features.Assignments;

public sealed class AssignmentRequest
{
    [Required,StringLength(200)] public string Title {get;set;}="";
    [Required,StringLength(20000)] public string Instructions {get;set;}="";
    public DateTimeOffset? DueAtUtc {get;set;}
    public decimal MaximumMarks {get;set;}
}
public sealed record AssignmentResponse(Guid Id,Guid LessonId,string Title,string Instructions,DateTimeOffset? DueAtUtc,decimal MaximumMarks,bool IsPublished,DateTimeOffset CreatedAt,DateTimeOffset? UpdatedAt,bool Frozen);
public sealed record GradeResponse(decimal Marks,string Feedback,Guid GraderId,DateTimeOffset GradedAt);
public sealed record SubmissionResponse(Guid Id,Guid AssignmentId,Guid UserId,string? Text,bool HasAttachment,string? Filename,DateTimeOffset SubmittedAt,string InstructionsSnapshot,decimal MaximumMarksSnapshot,GradeResponse? Grade);
public sealed record AssignmentParent(Guid LessonId,Guid SectionId,Guid CourseId,Guid? OrganizationId,Guid InstructorId,bool CoursePublished,bool LessonPublished);
public sealed record AssignmentContext(AssignmentParent Parent,int? EnrollmentStatus);
public sealed record AssignmentHistory(Guid LessonId,Guid SectionId,Guid CourseId,Guid AssignmentId,Guid SubmissionId,string Title,DateTimeOffset SubmittedAt,bool Graded);
public sealed record PublicationRequest(bool IsPublished,bool Confirmed);
public sealed record GradeRequest(decimal Marks,string Feedback,bool Confirmed);
public interface IAssignmentStorage:IPrivateMediaStorage { }
public sealed record SubmissionPage(int Page,int PageSize,int Total,SubmissionResponse[] Items);
public sealed record SubmissionSlice(int Total,AssignmentSubmission[] Items);
public interface IAssignmentRepository
{
    Task<AssignmentParent?> Parent(Guid lesson,CancellationToken token);
    Task<int?> Enrollment(Guid course,Guid user,CancellationToken token);
    Task<LessonAssignment?> Get(Guid id,CancellationToken token);
    Task<LessonAssignment[]> List(Guid lesson,CancellationToken token);
    Task<AssignmentSubmission[]> Submissions(Guid assignment,Guid? user,CancellationToken token);
    Task<bool> HasSubmissions(Guid assignment,CancellationToken token);
    Task<SubmissionSlice> SubmissionPage(Guid assignment,int page,int pageSize,CancellationToken token);
    Task<AssignmentSubmission?> Submission(Guid id,CancellationToken token);
    Task<AssignmentGrade?> Grade(Guid submission,CancellationToken token);
    Task<AssignmentSubmission[]> History(Guid user,CancellationToken token);
    void Add(object entity);
    void Update(LessonAssignment assignment);
    Task Save(CancellationToken token);
    Task<T> Locked<T>(Guid assignment,Func<CancellationToken,Task<T>> action,Action? beforeCommit,CancellationToken token);
}
