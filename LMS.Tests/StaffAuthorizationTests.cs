using LMS.Application.Features.Courses.AssignCourseInstructor;
using LMS.Application.Features.Lessons.Common;
using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Organizations;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;
using Xunit;

namespace LMS.Tests;
public sealed class StaffAuthorizationTests
{
    [Theory]
    [InlineData(OrganizationRole.Student,false)] [InlineData(OrganizationRole.Instructor,false)]
    [InlineData(OrganizationRole.Admin,false)] [InlineData(null,true)]
    public async Task Context_returns_only_stored_membership_and_platform_authority(OrganizationRole? role,bool platform)
    {
        var org=new Organization("A","A");var user=Guid.NewGuid();var repo=new Mock<IOrganizationRepository>(MockBehavior.Strict);
        repo.Setup(x=>x.GetAsync(org.Id,default)).ReturnsAsync(org);
        repo.Setup(x=>x.GetMembershipAsync(org.Id,user,default)).ReturnsAsync(role.HasValue?new OrganizationMembership(org.Id,user,role.Value):null);
        repo.Setup(x=>x.IsSuperAdminAsync(user,default)).ReturnsAsync(platform);
        var context=new OrganizationContext();var handlers=new OrganizationHandlers(repo.Object,context);
        await handlers.SelectAsync(org.Id,user);
        var result=handlers.Current(org.Id);
        Assert.Equal(role?.ToString(),result.Role);Assert.Equal(platform,result.IsSuperAdmin);Assert.Equal(user,result.UserId);
        Assert.Throws<UnauthorizedAccessException>(()=>handlers.Current(Guid.NewGuid()));
    }
    [Theory]
    [InlineData(OrganizationRole.Instructor,true,false,true)]
    [InlineData(OrganizationRole.Instructor,false,false,false)]
    [InlineData(OrganizationRole.Admin,false,false,true)]
    [InlineData(OrganizationRole.Student,true,false,false)]
    [InlineData(OrganizationRole.Admin,true,true,false)]
    public async Task Legacy_staff_reads_require_stored_scope_and_ownership(OrganizationRole role,bool owner,bool foreign,bool allowed)
    {
        var user=Guid.NewGuid();var org=Guid.NewGuid();var context=new OrganizationContext();context.Initialize(org,user,role,false);
        var course=new Course("Course","C","",owner?user:Guid.NewGuid(),CourseLevel.Beginner);course.AssignOrganization(foreign?Guid.NewGuid():org);
        var section=new CourseSection(course.Id,"Section",null,0);var lesson=new Lesson(section.Id,"Lesson",null,LessonType.Text,"Body",null,1,0);
        var courses=new Mock<ICourseRepository>();courses.Setup(x=>x.GetByIdAsync(course.Id,default)).ReturnsAsync(course);
        var sections=new Mock<ICourseSectionRepository>();sections.Setup(x=>x.GetByIdAsync(section.Id,default)).ReturnsAsync(section);
        var enrollments=new Mock<ICourseEnrollmentRepository>(MockBehavior.Strict);
        var readable=new EnsureLessonReadable(courses.Object,sections.Object,enrollments.Object,context);
        var error=await Record.ExceptionAsync(()=>readable.CheckAsync(lesson,user,true));
        if(allowed)Assert.Null(error);else Assert.NotNull(error);
        enrollments.VerifyNoOtherCalls();
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Instructor_assignment_validates_admin_scope_and_stored_target_membership(int scenario)
    {
        var org=Guid.NewGuid();var actor=Guid.NewGuid();var target=Guid.NewGuid();var context=new OrganizationContext();
        if(scenario!=5)context.Initialize(org,actor,scenario==1?OrganizationRole.Instructor:OrganizationRole.Admin,scenario==4);
        var course=new Course("Course","C","",Guid.NewGuid(),CourseLevel.Beginner);course.AssignOrganization(scenario==2?Guid.NewGuid():org);
        var original=course.InstructorId;var courses=new Mock<ICourseRepository>();courses.Setup(x=>x.GetByIdAsync(course.Id,default)).ReturnsAsync(course);
        var repo=new Mock<IOrganizationRepository>();repo.Setup(x=>x.GetMembershipAsync(org,target,default)).ReturnsAsync(new OrganizationMembership(org,target,scenario==3?OrganizationRole.Student:OrganizationRole.Instructor));
        var work=new Mock<IUnitOfWork>();var handler=new AssignCourseInstructorHandler(courses.Object,repo.Object,context,work.Object);
        var error=await Record.ExceptionAsync(()=>handler.HandleAsync(course.Id,target));
        if(scenario is 0 or 4){Assert.Null(error);Assert.Equal(target,course.InstructorId);work.Verify(x=>x.SaveChangesAsync(default),Times.Once);}
        else{Assert.NotNull(error);Assert.Equal(original,course.InstructorId);work.Verify(x=>x.SaveChangesAsync(default),Times.Never);}
    }
    [Fact]
    public async Task Global_instructor_role_without_organization_membership_cannot_be_assigned()
    {
        var org=Guid.NewGuid();var context=new OrganizationContext();context.Initialize(org,Guid.NewGuid(),OrganizationRole.Admin,false);
        var course=new Course("C","C","",Guid.NewGuid(),CourseLevel.Beginner);course.AssignOrganization(org);
        var courses=new Mock<ICourseRepository>();courses.Setup(x=>x.GetByIdAsync(course.Id,default)).ReturnsAsync(course);
        var work=new Mock<IUnitOfWork>();
        var handler=new AssignCourseInstructorHandler(courses.Object,Mock.Of<IOrganizationRepository>(),context,work.Object);
        await Assert.ThrowsAsync<ArgumentException>(()=>handler.HandleAsync(course.Id,Guid.NewGuid()));
        work.Verify(x=>x.SaveChangesAsync(default),Times.Never);
    }
}
