using System.ComponentModel.DataAnnotations;
namespace LMS.Web.Models;
public sealed record AssignmentView(Guid Id,Guid LessonId,string Title,string Instructions,DateTimeOffset? DueAtUtc,decimal MaximumMarks,bool IsPublished,DateTimeOffset CreatedAt,DateTimeOffset? UpdatedAt,bool Frozen);
public sealed record AssignmentParentView(Guid LessonId,Guid SectionId,Guid CourseId,Guid? OrganizationId,Guid InstructorId,bool CoursePublished,bool LessonPublished);
public sealed record AssignmentContextView(AssignmentParentView Parent,int? EnrollmentStatus);
public sealed record AssignmentGradeView(decimal Marks,string Feedback,Guid GraderId,DateTimeOffset GradedAt);
public sealed record AssignmentSubmissionView(Guid Id,Guid AssignmentId,Guid UserId,string? Text,bool HasAttachment,string? Filename,DateTimeOffset SubmittedAt,string InstructionsSnapshot,decimal MaximumMarksSnapshot,AssignmentGradeView? Grade);
public sealed record AssignmentHistoryView(Guid LessonId,Guid SectionId,Guid CourseId,Guid AssignmentId,Guid SubmissionId,string Title,DateTimeOffset SubmittedAt,bool Graded);
public sealed class AssignmentForm
{
    [Required,StringLength(200)] public string Title{get;set;}="";
    [Required,StringLength(20000)] public string Instructions{get;set;}="";
    public DateTime? DueAtUtc{get;set;}
    [Range(typeof(decimal),"0.01","9999999999999999.99")] public decimal MaximumMarks{get;set;}=100;
}
public sealed class AssignmentSubmissionForm
{
    [StringLength(100000)] public string? Text{get;set;}
    public IFormFile? File{get;set;}
    public bool Confirmed{get;set;}
}
public sealed class AssignmentGradeForm
{
    public decimal Marks{get;set;}
    [StringLength(20000)] public string Feedback{get;set;}="";
    public bool Confirmed{get;set;}
}
public sealed record AssignmentPage(Guid OrganizationId,AssignmentContextView Context,bool Staff,AssignmentView[] Assignments)
{
    public AssignmentView? Assignment{get;init;}
    public AssignmentSubmissionView[] Submissions{get;init;}=[];
    public AssignmentSubmissionView? Submission{get;init;}
    public AssignmentForm Form{get;init;}=new();
    public AssignmentSubmissionForm SubmissionForm{get;init;}=new();
    public AssignmentGradeForm GradeForm{get;init;}=new();
    public string Root=>$"/{(Staff?"staff":"learn")}/{OrganizationId}/courses/{Context.Parent.CourseId}/sections/{Context.Parent.SectionId}/{(Staff?"lessons":"units")}/{Context.Parent.LessonId}/assignments";
    public bool CanSubmit=>!Staff&&Context.EnrollmentStatus==1&&Context.Parent.CoursePublished&&Context.Parent.LessonPublished&&Assignment?.IsPublished==true&&Submissions.Length==0;
}
public sealed record AssignmentRecoveryPage(string Target,string Message);
public sealed record AssignmentHistoryPage(Guid OrganizationId,AssignmentHistoryView[] Rows);
