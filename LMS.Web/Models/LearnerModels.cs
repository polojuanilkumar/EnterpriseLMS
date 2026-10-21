using System.ComponentModel.DataAnnotations;
namespace LMS.Web.Models;

public sealed class LoginForm
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(1024)] public string Password { get; set; } = "";
}
public sealed record OrganizationView(Guid Id,string Name,bool IsActive,string Code = "");
public sealed record CourseView(Guid Id,string Title,string Description,bool IsPublished,Guid InstructorId = default,
    string? CategoryName = null, string Level = "", int DurationInMinutes = 0, string Status = "Published");
public sealed record SectionView(Guid Id,Guid CourseId,string Title,int DisplayOrder);
public sealed record LessonView(Guid Id,Guid SectionId,string Title,string? Description,bool IsPublished,int DisplayOrder);
public sealed record ItemView(Guid Id,Guid LessonId,int Type,string Title,int DisplayOrder,bool IsRequired,string? Text,string? YouTubeVideoId,Guid? AssetId = null);
public sealed record LegacyView(string? Content,string? VideoUrl);
public sealed record ContentView(ItemView[] Items,LegacyView? Legacy);
public sealed record EnrollmentView(Guid CourseId,int Status);
public sealed record CoursesPage(Guid OrganizationId,CourseView[] Courses);
public sealed record SectionsPage(Guid OrganizationId,CourseView Course,SectionView[] Sections)
{
    public CourseLearningPanel? Learning { get; init; }
    public string? Notice { get; init; }
}
public sealed record LessonsPage(Guid OrganizationId,CourseView Course,SectionView Section,LessonView[] Lessons);
public sealed record UnitPage(Guid OrganizationId,Guid CourseId,Guid SectionId,LessonView Lesson,ContentView Content)
{
    public LearnerQuizPage? Quiz { get; init; }
    public LessonProgressPanel? Progress { get; init; }
    public string? Notice { get; init; }
}

public sealed record OrganizationAccess(Guid OrganizationId, Guid UserId, string? Role, bool IsSuperAdmin)
{
    public bool IsAdmin => IsSuperAdmin || Role == "Admin";
    public bool IsStaff => IsAdmin || Role == "Instructor";
    public string Home => IsAdmin ? $"/admin/{OrganizationId}" : Role == "Instructor"
        ? $"/staff/{OrganizationId}/courses" : $"/learn/{OrganizationId}/courses";
}
