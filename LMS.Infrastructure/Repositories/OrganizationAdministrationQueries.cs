using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Infrastructure.Identity;
using LMS.Domain.Enums;

namespace LMS.Infrastructure.Repositories;

internal static class OrganizationAdministrationQueries
{
    internal static IQueryable<Guid> Admins(IQueryable<OrganizationMembership> memberships,Guid organization)
        => memberships.Where(x=>x.OrganizationId==organization && x.Role==OrganizationRole.Admin).Select(x=>x.UserId);
    internal static IQueryable<Guid> AssignedCourses(IQueryable<Course> courses,Guid organization,Guid user)
        => courses.Where(x=>x.OrganizationId==organization && x.InstructorId==user).Select(x=>x.Id);
    internal static IQueryable<Guid> ActiveEnrollments(IQueryable<CourseEnrollment> enrollments,IQueryable<Course> courses,Guid organization,Guid user)
        => from e in enrollments join c in courses on e.CourseId equals c.Id
           where c.OrganizationId==organization && e.UserId==user && e.Status==EnrollmentStatus.Active select e.Id;
    internal static IQueryable<OrganizationMemberRow> Members(IQueryable<OrganizationMembership> memberships,
        IQueryable<ApplicationUser> users, Guid organization, OrganizationListFilter filter)
    {
        filter.Validate(true);
        var query = from m in memberships
                    join u in users on m.UserId equals u.Id
                    where m.OrganizationId == organization
                    select new OrganizationMemberRow {
                        OrganizationId=m.OrganizationId,UserId=m.UserId,FirstName=u.FirstName,LastName=u.LastName,Email=u.Email??"",Role=m.Role };
        if (filter.Role.HasValue) query = query.Where(x => x.Role == filter.Role.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x => x.FirstName.Contains(search) || x.LastName.Contains(search) || x.Email.Contains(search));
        }
        return query;
    }
    internal static IQueryable<PlatformOrganizationRow> Organizations(IQueryable<Organization> organizations, OrganizationListFilter filter)
    {
        filter.Validate(false);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            organizations = organizations.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
        }
        return organizations.Select(x => new PlatformOrganizationRow {Id=x.Id,Code=x.Code,Name=x.Name,IsActive=x.IsActive});
    }
    internal static IQueryable<Guid> ExactAccount(IQueryable<ApplicationUser> users, string normalizedEmail)
        => users.Where(x => x.NormalizedEmail == normalizedEmail).OrderBy(x => x.Id).Select(x => x.Id).Take(2);
}
