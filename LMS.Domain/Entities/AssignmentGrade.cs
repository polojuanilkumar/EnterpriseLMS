namespace LMS.Domain.Entities;

public sealed class AssignmentGrade
{
    private AssignmentGrade() { }
    public AssignmentGrade(Guid submission,decimal marks,string feedback,Guid grader,DateTime now)
    {Id=Guid.NewGuid();SubmissionId=submission;Marks=marks;Feedback=feedback;GraderId=grader;GradedAt=now;}
    public Guid Id {get;private set;}
    public Guid SubmissionId {get;private set;}
    public decimal Marks {get;private set;}
    public string Feedback {get;private set;}="";
    public Guid GraderId {get;private set;}
    public DateTime GradedAt {get;private set;}
}
