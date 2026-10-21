using System.ComponentModel.DataAnnotations;
namespace LMS.Web.Models;
public sealed record StaffCoursesPage(OrganizationAccess Access, CourseView[] Courses);
public sealed record MemberView(Guid OrganizationId, Guid UserId, int Role, string FirstName = "", string LastName = "", string Email = "");
public sealed record MembersPage(Guid OrganizationId, MemberView[] Members, int Page = 1, int PageSize = 20, int Total = 0, string? Search = null, int? Role = null, MemberForm? Form = null);
public sealed record InstructorPage(Guid OrganizationId, CourseView Course, MemberView[] Instructors, MembersPage? Choices = null);
public sealed record StaffLessonPage(UnitPage Unit, string? Notice = null);
public sealed class ContentForm
{
    [Range(1,5)] public int Type { get; set; } = 1;
    [Required,StringLength(200)] public string Title { get; set; } = "";
    [Range(0,int.MaxValue)] public int DisplayOrder { get; set; }
    public bool IsRequired { get; set; }
    public string? Text { get; set; }
    public string? YouTubeUrl { get; set; }
    public Guid? AssetId { get; set; }
    public IFormFile? File { get; set; }
}
public sealed record ContentEditPage(UnitPage Unit, Guid? ItemId, ContentForm Form);
public sealed class MemberForm
{
    public Guid UserId { get; set; }
    [StringLength(256)] public string? Email { get; set; }
    [Range(1,3)] public int Role { get; set; }
    public bool Confirmed { get; set; }
    [Range(1,10000)] public int Page { get; set; } = 1;
    [Range(1,50)] public int PageSize { get; set; } = 20;
    [StringLength(100)] public string? Search { get; set; }
    [Range(1,3)] public int? FilterRole { get; set; }
}
public sealed record PublicationPage(Guid OrganizationId, Guid CourseId, Guid? SectionId, Guid? LessonId, string Title, string Action);
public sealed record AssetView(Guid Id, Guid LessonId, Guid OrganizationId, int Type);
