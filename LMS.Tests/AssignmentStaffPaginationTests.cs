using LMS.Domain.Enums;
using Xunit;
using LMS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
namespace LMS.Tests;
public sealed class AssignmentStaffPaginationTests
{
    [Fact] public void Production_query_uses_stable_tenant_filtered_SQL_pagination_without_opening_a_database()
    {
        var org=Guid.NewGuid();var assignment=Guid.NewGuid();using var db=TenantSqlTests.CreateContext(TenantSqlTests.Scope(org,OrganizationRole.Instructor));
        var sql=new AssignmentRepository(db).SubmissionPageQuery(assignment,2,20).ToQueryString();
        Assert.Contains("OFFSET",sql);Assert.Contains("FETCH NEXT",sql);Assert.Contains("ORDER BY",sql);Assert.Contains("[SubmittedAt] DESC",sql);Assert.Contains("[OrganizationId]",sql);Assert.Contains(org.ToString(),sql);Assert.Contains(assignment.ToString(),sql);
    }
    [Fact] public async Task Pages_are_bounded_stable_and_historical_without_enrollment_gates()
    {
        var f=new AssignmentPolicyTests.Fixture(OrganizationRole.Instructor);f.Repo.EnrollmentState=3;f.A.Publish(false,f.Clock.Now.UtcDateTime);
        for(var i=0;i<43;i++)f.Work(Guid.NewGuid());
        var pages=new List<Guid>();for(var page=1;page<=3;page++){var result=await f.Handler.StaffSubmissionPage(f.Parent.LessonId,f.A.Id,page,20,default);Assert.Equal(43,result.Total);Assert.InRange(result.Items.Length,1,20);pages.AddRange(result.Items.Select(x=>x.Id));}
        Assert.Equal(43,pages.Distinct().Count());Assert.Empty((await f.Handler.StaffSubmissionPage(f.Parent.LessonId,f.A.Id,4,20,default)).Items);
    }
    [Theory][InlineData(0,20)][InlineData(1,51)][InlineData(10001,1)]
    public async Task Invalid_pagination_is_rejected(int page,int size)
    {var f=new AssignmentPolicyTests.Fixture(OrganizationRole.Admin);await Assert.ThrowsAsync<ArgumentException>(()=>f.Handler.StaffSubmissionPage(f.Parent.LessonId,f.A.Id,page,size,default));}
    [Fact] public async Task Student_foreign_tenant_owner_and_nested_assignment_are_denied()
    {
        var student=new AssignmentPolicyTests.Fixture();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>student.Handler.StaffSubmissionPage(student.Parent.LessonId,student.A.Id,1,20,default));
        var f=new AssignmentPolicyTests.Fixture(OrganizationRole.Instructor);f.Repo.ParentValue=f.Parent with {InstructorId=Guid.NewGuid()};await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Handler.StaffSubmissionPage(f.Parent.LessonId,f.A.Id,1,20,default));
        f.Repo.ParentValue=f.Parent with {OrganizationId=Guid.NewGuid()};await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.StaffSubmissionPage(f.Parent.LessonId,f.A.Id,1,20,default));
        f.Repo.ParentValue=f.Parent;await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.StaffSubmissionPage(f.Parent.LessonId,Guid.NewGuid(),1,20,default));
    }
}
