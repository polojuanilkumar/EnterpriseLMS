using LMS.Application.Features.LessonContent;
using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class LessonContentTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=abc")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=10")]
    [InlineData("https://m.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/live/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/embed/dQw4w9WgXcQ")]
    public void Supported_urls_normalize(string url)
        => Assert.Equal("dQw4w9WgXcQ", LessonContentItem.NormalizeYouTubeUrl(url));

    [Theory]
    [InlineData("https://evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com@evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://user@youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be:444/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/short")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ/extra")]
    [InlineData("https://youtu.be/%64Qw4w9WgXcQ")]
    [InlineData("https://youtube.com/playlist?list=dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ")]
    public void Unsafe_or_unsupported_urls_rejected(string url)
        => Assert.Throws<ArgumentException>(() => LessonContentItem.NormalizeYouTubeUrl(url));

    [Theory]
    [InlineData(0, "text", null)]
    [InlineData(3, "text", null)]
    [InlineData(1, " ", null)]
    [InlineData(1, "text", "https://youtu.be/dQw4w9WgXcQ")]
    [InlineData(2, "text", "https://youtu.be/dQw4w9WgXcQ")]
    [InlineData(2, null, null)]
    public void Invalid_type_payload_rejected(int type, string? text, string? url)
        => Assert.Throws<ArgumentException>(() => new LessonContentItem(Guid.NewGuid(), (LessonContentType)type, "Title", 0, true, text, url));

    [Fact]
    public void Switching_types_clears_old_payload_and_failed_update_is_atomic()
    {
        var item = Text(Guid.NewGuid(), 0);
        item.Update(LessonContentType.YouTube, "Video", 1, false, null, "https://youtu.be/dQw4w9WgXcQ");
        Assert.Null(item.Text); Assert.Equal("dQw4w9WgXcQ", item.YouTubeVideoId);
        Assert.Throws<ArgumentException>(() => item.Update(LessonContentType.Text, "Bad", -1, true, "body", null));
        Assert.Equal("Video", item.Title);
        item.Update(LessonContentType.Text, "Text", 0, true, "body", null);
        Assert.Null(item.YouTubeVideoId);
    }

    [Fact]
    public async Task Ordering_is_stable_and_reorder_is_complete_and_saved_once()
    {
        var f = new Fixture(); var a = Text(f.Lesson.Id, 8); var b = Text(f.Lesson.Id, 1);
        f.Items.AddRange([a, b]);
        Assert.Equal(new[] { b.Id, a.Id }, (await f.Handler.ListAsync(f.Lesson.Id)).Items.Select(x => x.Id));
        await f.Handler.ReorderAsync(f.Lesson.Id, new([a.Id, b.Id]));
        Assert.Equal(0, a.DisplayOrder); Assert.Equal(1, b.DisplayOrder);
        f.Work.Verify(x => x.SaveChangesAsync(default), Times.Once);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Invalid_reorders_do_not_mutate_or_save(int mode)
    {
        var f = new Fixture(); var a = Text(f.Lesson.Id, 4); var b = Text(f.Lesson.Id, 7); f.Items.AddRange([a,b]);
        Guid[] ids = mode == 0 ? [a.Id] : mode == 1 ? [a.Id,a.Id] : [a.Id,Guid.NewGuid()];
        await Assert.ThrowsAsync<ArgumentException>(() => f.Handler.ReorderAsync(f.Lesson.Id, new(ids)));
        Assert.Equal(4, a.DisplayOrder); f.Work.Verify(x => x.SaveChangesAsync(default), Times.Never);
    }
    [Fact]
    public async Task Legacy_fallback_never_duplicates_items_or_modifies_lesson()
    {
        var f = new Fixture(); var response = await f.Handler.ListAsync(f.Lesson.Id);
        Assert.Empty(response.Items); Assert.Equal("Legacy body", response.Legacy!.Content);
        Assert.Equal("https://legacy.example/video", response.Legacy.VideoUrl);
        var item = await f.Handler.CreateAsync(f.Lesson.Id, new(LessonContentType.Text,"New",0,true,"Body",null));
        Assert.Null((await f.Handler.ListAsync(f.Lesson.Id)).Legacy);
        await f.Handler.DeleteAsync(f.Lesson.Id,item.Id);
        Assert.NotNull((await f.Handler.ListAsync(f.Lesson.Id)).Legacy);
        Assert.Equal("Legacy body", f.Lesson.Content); Assert.Equal("https://legacy.example/video", f.Lesson.VideoUrl);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Instructor_requires_ownership_for_reads_and_writes(bool write)
    {
        var f = new Fixture(OrganizationRole.Instructor, owner: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => write ? f.Create() : f.Read());
        f.Work.Verify(x => x.SaveChangesAsync(default), Times.Never);
    }
    [Theory]
    [InlineData(OrganizationRole.Student)] [InlineData(OrganizationRole.Instructor)] [InlineData(OrganizationRole.Admin)]
    public async Task Foreign_organization_is_denied_even_when_repository_returns_foreign_parent(OrganizationRole role)
    {
        var f = new Fixture(role, foreign: true);
        await Assert.ThrowsAsync<KeyNotFoundException>(f.Read);
        await Assert.ThrowsAsync<KeyNotFoundException>(f.Create);
    }
    [Fact]
    public async Task Mismatched_item_parent_is_denied_for_detail_update_and_delete()
    {
        var f = new Fixture(); var foreign = Text(Guid.NewGuid(),0); f.Items.Add(foreign);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Handler.GetAsync(f.Lesson.Id,foreign.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Handler.UpdateAsync(f.Lesson.Id,foreign.Id,new(LessonContentType.Text,"X",0,true,"X",null)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Handler.DeleteAsync(f.Lesson.Id,foreign.Id));
        f.Work.Verify(x => x.SaveChangesAsync(default), Times.Never);
    }
    [Theory]
    [InlineData(true,true,0,true)] [InlineData(true,true,1,true)]
    [InlineData(true,true,2,false)] [InlineData(true,true,3,false)]
    [InlineData(false,true,0,false)] [InlineData(true,false,0,false)]
    public async Task Learner_publication_and_enrollment_rules(bool lessonPublished, bool coursePublished, int enrollment, bool allowed)
    {
        var f = new Fixture(OrganizationRole.Student, lessonPublished: lessonPublished, coursePublished: coursePublished, enrollment: enrollment);
        f.Items.Add(Text(f.Lesson.Id,0));
        if (allowed) { Assert.Single((await f.Handler.ListAsync(f.Lesson.Id)).Items); await f.Handler.GetAsync(f.Lesson.Id,f.Items[0].Id); }
        else
        {
            var ex = await Record.ExceptionAsync(f.Read);
            Assert.IsType(!lessonPublished || !coursePublished ? typeof(KeyNotFoundException) : typeof(UnauthorizedAccessException), ex);
            Assert.NotNull(await Record.ExceptionAsync(() => f.Handler.GetAsync(f.Lesson.Id,f.Items[0].Id)));
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(f.Create);
    }
    [Fact]
    public async Task Admin_and_owner_can_preview_unpublished_and_update()
    {
        foreach (var role in new[] { OrganizationRole.Admin, OrganizationRole.Instructor })
        {
            var f = new Fixture(role,lessonPublished:false,coursePublished:false);
            await f.Create(); var item = f.Items.Single();
            await f.Handler.UpdateAsync(f.Lesson.Id,item.Id,new(LessonContentType.YouTube,"Video",2,false,null,"https://youtu.be/dQw4w9WgXcQ"));
            Assert.Equal("dQw4w9WgXcQ", (await f.Handler.GetAsync(f.Lesson.Id,item.Id)).YouTubeVideoId);
        }
    }
    [Fact]
    public void Model_has_parent_filter_index_and_additive_table()
    {
        using var db = new LMSDbContext(new DbContextOptionsBuilder<LMSDbContext>().UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var entity = db.Model.FindEntityType(typeof(LessonContentItem))!;
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Contains(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Lesson));
        Assert.Contains(entity.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "LessonId", "DisplayOrder" }));
        var sql = db.LessonContentItems.ToQueryString();
        Assert.Contains("OrganizationId",sql); Assert.Contains("CourseSections",sql);
    }
    [Fact]
    public void Actual_item_filter_excludes_foreign_and_unassigned_organization_graphs()
    {
        using var graph = new TenantGraph();
        Assert.Equal(graph.First.Lesson.Id, Assert.Single(graph.Visible<LessonContentItem>()).LessonId);
        graph.Selected = graph.B;
        Assert.Equal(graph.Second.Lesson.Id, Assert.Single(graph.Visible<LessonContentItem>()).LessonId);
        graph.Selected = Guid.Empty;
        Assert.Empty(graph.Visible<LessonContentItem>());
    }
    [Fact]
    public async Task Missing_context_rejects_item_writes_without_database_access()
    {
        using var db = new LMSDbContext(new DbContextOptionsBuilder<LMSDbContext>().UseSqlServer("Server=unused;Database=unused").Options);
        db.LessonContentItems.Add(Text(Guid.NewGuid(), 0));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public void Endpoint_metadata_requires_organization_and_authoring_roles()
    {
        var controller = typeof(LMS.Api.Controllers.LessonContentItemsController);
        Assert.True(Attribute.IsDefined(controller, typeof(LMS.Api.Middleware.OrganizationScopeAttribute)));
        foreach (var name in new[] { "Create", "Update", "Delete", "Reorder" })
        {
            var authorization = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)Attribute.GetCustomAttribute(
                controller.GetMethod(name)!, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute))!;
            Assert.Equal("Instructor,Admin,SuperAdmin", authorization.Roles);
        }
    }
    private static LessonContentItem Text(Guid lessonId, int order) => new(lessonId,LessonContentType.Text,"Title",order,true,"Body",null);
    internal sealed class Fixture
    {
        public OrganizationContext Context { get; } = new();
        public Mock<IMediaAssetRepository> Assets { get; } = new();
        public Lesson Lesson { get; }
        public List<LessonContentItem> Items { get; } = [];
        public Mock<IUnitOfWork> Work { get; } = new();
        public LessonContentHandlers Handler { get; }
        public Fixture(OrganizationRole role = OrganizationRole.Instructor, bool owner = true, bool foreign = false,
            bool lessonPublished = true, bool coursePublished = true, int enrollment = 0, bool superAdmin = false)
        {
            var user = Guid.NewGuid(); var org = Guid.NewGuid(); var context = Context; context.Initialize(org,user,role,superAdmin);
            var course = new Course("Course","CODE","Description",owner ? user : Guid.NewGuid(),CourseLevel.Beginner);
            course.AssignOrganization(foreign ? Guid.NewGuid() : org); if (coursePublished) course.Publish();
            var section = new CourseSection(course.Id,"Section",null,0);
            Lesson = new Lesson(section.Id,"Lesson",null,LessonType.Text,"Legacy body","https://legacy.example/video",1,0);
            if (lessonPublished) Lesson.Publish();
            var lessons = new Mock<ILessonRepository>(); lessons.Setup(x => x.GetByIdAsync(Lesson.Id,default)).ReturnsAsync(Lesson);
            var sections = new Mock<ICourseSectionRepository>(); sections.Setup(x => x.GetByIdAsync(section.Id,default)).ReturnsAsync(section);
            var courses = new Mock<ICourseRepository>(); courses.Setup(x => x.GetByIdAsync(course.Id,default)).ReturnsAsync(course);
            var enrollments = new Mock<ICourseEnrollmentRepository>(); var e = new CourseEnrollment(course.Id,user);
            if (enrollment == 1) e.Complete(); if (enrollment == 2) e.Cancel();
            enrollments.Setup(x => x.GetByCourseAndUserAsync(course.Id,user,default)).ReturnsAsync(enrollment == 3 ? null : e);
            var items = new Mock<ILessonContentItemRepository>();
            items.Setup(x => x.ListAsync(Lesson.Id,default)).ReturnsAsync(() => Items.Where(x => x.LessonId == Lesson.Id).ToArray());
            items.Setup(x => x.GetAsync(It.IsAny<Guid>(),default)).ReturnsAsync((Guid id,CancellationToken _) => Items.SingleOrDefault(x => x.Id == id));
            items.Setup(x => x.Add(It.IsAny<LessonContentItem>())).Callback((LessonContentItem item) => Items.Add(item));
            items.Setup(x => x.Remove(It.IsAny<LessonContentItem>())).Callback((LessonContentItem item) => Items.Remove(item));
            Handler = new(lessons.Object,sections.Object,courses.Object,enrollments.Object,items.Object,Work.Object,context,Assets.Object);
        }
        public async Task Read() => await Handler.ListAsync(Lesson.Id);
        public async Task Create() => await Handler.CreateAsync(Lesson.Id,new(LessonContentType.Text,"New",0,true,"Body",null));
    }
}
