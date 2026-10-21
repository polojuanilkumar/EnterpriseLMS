using LMS.Application.Features.Courses.CreateCourse;
using LMS.Application.Features.Courses.PublishCourse;
using LMS.Application.Features.Courses.UpdateCourse;
using LMS.Application.Features.CourseSections;
using LMS.Application.Features.CourseSections.UpdateCourseSection;
using LMS.Application.Features.Lessons.Common;
using LMS.Application.Features.Lessons.PublishLesson;
using LMS.Application.Features.Organizations;
using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.Categories;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;
using Xunit;
namespace LMS.Tests;

public sealed class AuthoringPolicyTests
{
    [Theory]
    [InlineData(false,true)] [InlineData(true,true)] [InlineData(true,false)] [InlineData(false,false)]
    public async Task Creation_requires_instructor_validation_and_forces_self_for_instructors(bool admin,bool valid)
    {
        var actor=Guid.NewGuid();var assigned=Guid.NewGuid();var expected=admin?assigned:actor;
        var identity=new Mock<IIdentityService>();identity.Setup(x=>x.UserExistsAsync(expected)).ReturnsAsync(true);
        identity.Setup(x=>x.IsInRoleAsync(expected,"Instructor")).ReturnsAsync(valid);
        var courses=new Mock<ICourseRepository>();Course? added=null;
        courses.Setup(x=>x.AddAsync(It.IsAny<Course>(),default)).Callback((Course c,CancellationToken _)=>added=c).Returns(Task.CompletedTask);
        var work=new Mock<IUnitOfWork>();
        var handler=new CreateCourseHandler(courses.Object,identity.Object,work.Object,Mock.Of<ICategoryRepository>());
        var error=await Record.ExceptionAsync(()=>handler.HandleAsync(new(){Title="New",Code="NEW",Description="Body",InstructorId=assigned,Level=CourseLevel.Intermediate,DurationInMinutes=20},actor,admin));
        if(valid){Assert.Null(error);Assert.Equal(expected,added!.InstructorId);Assert.Equal(20,added.DurationInMinutes);work.Verify(x=>x.SaveChangesAsync(default),Times.Once);}
        else {Assert.NotNull(error);Assert.Null(added);work.Verify(x=>x.SaveChangesAsync(default),Times.Never);}
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Course_update_ownership_is_rechecked(bool owner)
    {
        var actor=Guid.NewGuid();var course=new Course("Before","C","Body",owner?actor:Guid.NewGuid(),CourseLevel.Beginner);
        var courses=new Mock<ICourseRepository>();courses.Setup(x=>x.GetByIdAsync(course.Id,default)).ReturnsAsync(course);
        var handler=new UpdateCourseHandler(courses.Object,Mock.Of<ICategoryRepository>());
        var error=await Record.ExceptionAsync(()=>handler.HandleAsync(course.Id,new(){Title="After",Code="C",Description="Body",Level=CourseLevel.Advanced},actor,false,default));
        if(owner){Assert.Null(error);Assert.Equal("After",course.Title);courses.Verify(x=>x.SaveChangesAsync(default),Times.Once);}
        else {Assert.IsType<UnauthorizedAccessException>(error);Assert.Equal("Before",course.Title);courses.Verify(x=>x.SaveChangesAsync(default),Times.Never);}
    }
    [Fact]
    public async Task Section_update_rejects_mismatched_stored_parent_before_mutation()
    {
        var actor=Guid.NewGuid();var course=new Course("C","C","",actor,CourseLevel.Beginner);
        var section=new CourseSection(Guid.NewGuid(),"Before",null,1);
        var courses=new Mock<ICourseRepository>();courses.Setup(x=>x.GetByIdAsync(course.Id,default)).ReturnsAsync(course);
        var sections=new Mock<ICourseSectionRepository>();sections.Setup(x=>x.GetByIdAsync(section.Id,default)).ReturnsAsync(section);
        var work=new Mock<IUnitOfWork>();var handler=new UpdateCourseSectionHandler(sections.Object,courses.Object,work.Object);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>handler.HandleAsync(course.Id,section.Id,new UpdateCourseSectionRequest{Title="After",DisplayOrder=1},actor,true));
        Assert.Equal("Before",section.Title);work.Verify(x=>x.SaveChangesAsync(default),Times.Never);
    }
    [Fact]
    public async Task Actual_publication_handlers_allow_empty_course_and_empty_lesson()
    {
        var actor=Guid.NewGuid();var course=new Course("Empty","EMPTY","",actor,CourseLevel.Beginner);
        var courses=new Mock<ICourseRepository>();courses.Setup(x=>x.GetByIdAsync(course.Id,default)).ReturnsAsync(course);
        var work=new Mock<IUnitOfWork>();await new PublishCourseHandler(courses.Object,work.Object).HandleAsync(course.Id,actor,false);
        Assert.True(course.IsPublished);
        var section=new CourseSection(course.Id,"S",null,1);var lesson=new Lesson(section.Id,"Empty",null,LessonType.Text,null,null,0,1);
        var sections=new Mock<ICourseSectionRepository>();sections.Setup(x=>x.GetByIdAsync(section.Id,default)).ReturnsAsync(section);
        var lessons=new Mock<ILessonRepository>();lessons.Setup(x=>x.GetByIdAsync(lesson.Id,default)).ReturnsAsync(lesson);
        await new PublishLessonHandler(lessons.Object,new EnsureLessonOwnership(sections.Object,courses.Object),work.Object).HandleAsync(lesson.Id,actor,false);
        Assert.True(lesson.IsPublished);work.Verify(x=>x.SaveChangesAsync(default),Times.Exactly(2));
    }
    [Theory]
    [InlineData(OrganizationRole.Instructor,true,false,true)] [InlineData(OrganizationRole.Instructor,false,false,false)]
    [InlineData(OrganizationRole.Admin,false,false,true)] [InlineData(OrganizationRole.Admin,true,true,false)]
    public async Task Quiz_staff_read_resolves_stored_organization_and_course_owner(OrganizationRole role,bool owner,bool foreign,bool allowed)
    {
        var org=Guid.NewGuid();var actor=Guid.NewGuid();var context=new OrganizationContext();context.Initialize(org,actor,role,false);
        var course=new Course("C","C","",owner?actor:Guid.NewGuid(),CourseLevel.Beginner);course.AssignOrganization(foreign?Guid.NewGuid():org);
        var section=new CourseSection(course.Id,"S",null,1);var lesson=new Lesson(section.Id,"L",null,LessonType.Text,null,null,0,1);
        var quiz=new Quiz(lesson.Id,"Q",null,70,10,2);
        var courses=new Mock<ICourseRepository>();courses.Setup(x=>x.GetByIdAsync(course.Id,default)).ReturnsAsync(course);
        var sections=new Mock<ICourseSectionRepository>();sections.Setup(x=>x.GetByIdAsync(section.Id,default)).ReturnsAsync(section);
        var lessons=new Mock<ILessonRepository>();lessons.Setup(x=>x.GetByIdAsync(lesson.Id,default)).ReturnsAsync(lesson);
        var read=new EnsureQuizReadable(lessons.Object,new EnsureLessonReadable(courses.Object,sections.Object,Mock.Of<ICourseEnrollmentRepository>(),context));
        var error=await Record.ExceptionAsync(()=>read.CheckAsync(quiz,actor,true));
        if(allowed)Assert.Null(error);else Assert.NotNull(error);
    }
}
