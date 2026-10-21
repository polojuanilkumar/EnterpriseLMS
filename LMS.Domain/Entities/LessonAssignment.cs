namespace LMS.Domain.Entities;

public sealed class LessonAssignment
{
    private LessonAssignment() { }
    public LessonAssignment(Guid lessonId,string title,string instructions,DateTime? dueAtUtc,decimal maximumMarks,DateTime now)
    {Id=Guid.NewGuid();LessonId=lessonId;Title=title;Instructions=instructions;DueAtUtc=dueAtUtc;MaximumMarks=maximumMarks;CreatedAt=now;}
    public Guid Id {get;private set;}
    public Guid LessonId {get;private set;}
    public string Title {get;private set;}="";
    public string Instructions {get;private set;}="";
    public DateTime? DueAtUtc {get;private set;}
    public decimal MaximumMarks {get;private set;}
    public bool IsPublished {get;private set;}
    public DateTime CreatedAt {get;private set;}
    public DateTime? UpdatedAt {get;private set;}
    public void Edit(string title,string instructions,DateTime? due,decimal marks,DateTime now)
    {Title=title;Instructions=instructions;DueAtUtc=due;MaximumMarks=marks;UpdatedAt=now;}
    public void Publish(bool published,DateTime now){IsPublished=published;UpdatedAt=now;}
}
