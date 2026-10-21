namespace LMS.Domain.Entities;

public sealed class AssignmentSubmission
{
    private AssignmentSubmission() { }
    public AssignmentSubmission(LessonAssignment assignment,Guid user,string? text,Guid? attachment,string? filename,long? size,DateTime now)
    {Id=Guid.NewGuid();AssignmentId=assignment.Id;UserId=user;Text=text;AttachmentId=attachment;Filename=filename;AttachmentSize=size;SubmittedAt=now;InstructionsSnapshot=assignment.Instructions;MaximumMarksSnapshot=assignment.MaximumMarks;}
    public Guid Id {get;private set;}
    public Guid AssignmentId {get;private set;}
    public Guid UserId {get;private set;}
    public string? Text {get;private set;}
    public Guid? AttachmentId {get;private set;}
    public string? Filename {get;private set;}
    public long? AttachmentSize {get;private set;}
    public DateTime SubmittedAt {get;private set;}
    public string InstructionsSnapshot {get;private set;}="";
    public decimal MaximumMarksSnapshot {get;private set;}
}
