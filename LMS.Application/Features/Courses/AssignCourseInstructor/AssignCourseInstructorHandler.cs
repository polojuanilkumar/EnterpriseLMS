using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.Organizations;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Enums;
namespace LMS.Application.Features.Courses.AssignCourseInstructor;

public sealed class AssignCourseInstructorHandler(ICourseRepository courses, IOrganizationRepository organizations,
    OrganizationContext context, IUnitOfWork work)
{
    public async Task HandleAsync(Guid courseId, Guid instructorId, CancellationToken token = default)
    {
        if (!context.OrganizationId.HasValue || (!context.IsSuperAdmin && context.Role != OrganizationRole.Admin))
            throw new UnauthorizedAccessException("Selected organization administration is required.");
        var course = await courses.GetByIdAsync(courseId, token);
        if (course is null || course.Id != courseId || course.OrganizationId != context.OrganizationId)
            throw new KeyNotFoundException("Course not found.");
        var membership = await organizations.GetMembershipAsync(context.OrganizationId.Value, instructorId, token);
        if (membership is null || membership.OrganizationId != context.OrganizationId || membership.UserId != instructorId
            || membership.Role != OrganizationRole.Instructor)
            throw new ArgumentException("Choose an Instructor member of the selected organization.");
        course.AssignInstructor(instructorId);
        await work.SaveChangesAsync(token);
    }
}
