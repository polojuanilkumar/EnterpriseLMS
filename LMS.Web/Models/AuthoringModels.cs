using System.ComponentModel.DataAnnotations;
namespace LMS.Web.Models;

public sealed class CourseForm
{
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(50)] public string Code { get; set; } = "";
    [Required, StringLength(5000)] public string Description { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public Guid? InstructorId { get; set; }
    [Range(1,3)] public int Level { get; set; } = 1;
    [Range(0,int.MaxValue)] public int DurationInMinutes { get; set; }
    [StringLength(500)] public string? ThumbnailUrl { get; set; }
}
public class SectionForm
{
    [Required, StringLength(200)] public string Title { get; set; } = "";
    public string? Description { get; set; }
    [Range(1,int.MaxValue)] public int DisplayOrder { get; set; } = 1;
}
public sealed class LessonForm : SectionForm
{
    [Range(1,2)] public int LessonType { get; set; } = 1;
    public string? Content { get; set; }
    public string? VideoUrl { get; set; }
    [Range(0,int.MaxValue)] public int DurationInMinutes { get; set; }
}
public sealed record CategoryChoice(Guid Id,string Name,bool IsActive);
public sealed record CourseDetail(Guid Id,string Title,string Code,string Description,Guid? CategoryId,Guid InstructorId,string Level,string Status,int DurationInMinutes,string? ThumbnailUrl);
public sealed record SectionDetail(Guid Id,Guid CourseId,string Title,string? Description,int DisplayOrder);
public sealed record LessonDetail(Guid Id,Guid SectionId,string Title,string? Description,string LessonType,string? Content,string? VideoUrl,int DurationInMinutes,int DisplayOrder);
public sealed record CourseEditor(Guid OrganizationId,Guid? CourseId,CourseForm Form,CategoryChoice[] Categories,MemberView[] Instructors,bool IsAdmin,MembersPage? Choices=null);
public sealed record SectionEditor(Guid OrganizationId,Guid CourseId,Guid? SectionId,SectionForm Form);
public sealed record LessonEditor(Guid OrganizationId,Guid CourseId,Guid SectionId,Guid? LessonId,LessonForm Form);
public sealed record QuizState(Guid Id,Guid LessonId,string Title,bool IsPublished);
public sealed record ReadyLesson(UnitPage Unit,QuizState? Quiz);
public sealed record ReadySection(SectionView Section,ReadyLesson[] Lessons);
public sealed record ReadinessPage(Guid OrganizationId,CourseDetail Course,ReadySection[] Sections);
