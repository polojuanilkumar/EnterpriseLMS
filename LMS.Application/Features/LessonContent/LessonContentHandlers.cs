using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using LMS.Domain.Enums;

namespace LMS.Application.Features.LessonContent;

public sealed record ContentItemRequest(LessonContentType Type, string Title, int DisplayOrder,
    bool IsRequired, string? Text, string? YouTubeUrl, Guid? AssetId = null);
public sealed record ReorderContentRequest(IReadOnlyList<Guid> ItemIds);
public sealed record LegacyLessonContent(string? Content, string? VideoUrl);
public sealed record LessonContentResponse(IReadOnlyList<LessonContentItem> Items, LegacyLessonContent? Legacy);

public sealed class LessonContentHandlers(ILessonRepository lessons, ICourseSectionRepository sections,
    ICourseRepository courses, ICourseEnrollmentRepository enrollments, ILessonContentItemRepository items,
    IUnitOfWork unitOfWork, OrganizationContext organization, IMediaAssetRepository? assets = null)
{
    public async Task<Lesson> AccessAsync(Guid lessonId, bool write, CancellationToken token)
    {
        if (organization.OrganizationId is null || organization.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("Verified organization context is required.");
        var lesson = await lessons.GetByIdAsync(lessonId, token);
        if (lesson is null || lesson.Id != lessonId) throw new KeyNotFoundException("Lesson not found.");
        var section = await sections.GetByIdAsync(lesson.SectionId, token);
        if (section is null || section.Id != lesson.SectionId) throw new KeyNotFoundException("Section not found.");
        var course = await courses.GetByIdAsync(section.CourseId, token);
        if (course is null || course.Id != section.CourseId || course.OrganizationId != organization.OrganizationId)
            throw new KeyNotFoundException("Course not found.");
        if (organization.CanManageContent)
        {
            if (!organization.IsSuperAdmin && organization.Role != OrganizationRole.Admin
                && course.InstructorId != organization.UserId)
                throw new UnauthorizedAccessException("Course ownership is required.");
        }
        else
        {
            if (write || organization.Role != OrganizationRole.Student)
                throw new UnauthorizedAccessException("Content authoring role is required.");
            if (!lesson.IsPublished || !course.IsPublished) throw new KeyNotFoundException("Lesson not found.");
            var enrollment = await enrollments.GetByCourseAndUserAsync(course.Id, organization.UserId, token);
            if (enrollment is null || enrollment.CourseId != course.Id || enrollment.UserId != organization.UserId
                || enrollment.Status is not (EnrollmentStatus.Active or EnrollmentStatus.Completed))
                throw new UnauthorizedAccessException("An active or completed course enrollment is required.");
        }
        return lesson;
    }
    private async Task<LessonContentItem> ItemAsync(Guid lessonId, Guid itemId, CancellationToken token)
    {
        var item = await items.GetAsync(itemId, token);
        if (item is null || item.Id != itemId || item.LessonId != lessonId)
            throw new KeyNotFoundException("Content item not found.");
        return item;
    }
    public async Task<LessonContentResponse> ListAsync(Guid lessonId, CancellationToken token = default)
    {
        var lesson = await AccessAsync(lessonId, false, token);
        var list = (await items.ListAsync(lessonId, token)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray();
        return new(list, list.Length == 0 ? new(lesson.Content, lesson.VideoUrl) : null);
    }
    public async Task<LessonContentItem> GetAsync(Guid lessonId, Guid itemId, CancellationToken token = default)
    {
        await AccessAsync(lessonId, false, token);
        return await ItemAsync(lessonId, itemId, token);
    }
    public async Task<LessonContentItem> CreateAsync(Guid lessonId, ContentItemRequest request, CancellationToken token = default)
    {
        await AccessAsync(lessonId, true, token);
        await ValidateAssetAsync(lessonId, request, token);
        var item = new LessonContentItem(lessonId, request.Type, request.Title, request.DisplayOrder,
            request.IsRequired, request.Text, request.YouTubeUrl, request.AssetId);
        items.Add(item);
        await unitOfWork.SaveChangesAsync(token);
        return item;
    }
    public async Task<LessonContentItem> UpdateAsync(Guid lessonId, Guid itemId, ContentItemRequest request, CancellationToken token = default)
    {
        await AccessAsync(lessonId, true, token);
        var item = await ItemAsync(lessonId, itemId, token);
        await ValidateAssetAsync(lessonId, request, token);
        item.Update(request.Type, request.Title, request.DisplayOrder, request.IsRequired, request.Text, request.YouTubeUrl, request.AssetId);
        await unitOfWork.SaveChangesAsync(token);
        return item;
    }
    private async Task ValidateAssetAsync(Guid lessonId, ContentItemRequest request, CancellationToken token)
    {
        if (!request.AssetId.HasValue) return; // Domain validation requires one for media types.
        var asset = assets is null ? null : await assets.GetAsync(request.AssetId.Value,token);
        if (asset is null || asset.Id != request.AssetId || asset.LessonId != lessonId
            || asset.OrganizationId != organization.OrganizationId || asset.Type != request.Type)
            throw new ArgumentException("Asset must match the lesson, organization and content type.");
    }
    public async Task DeleteAsync(Guid lessonId, Guid itemId, CancellationToken token = default)
    {
        await AccessAsync(lessonId, true, token);
        items.Remove(await ItemAsync(lessonId, itemId, token));
        await unitOfWork.SaveChangesAsync(token);
    }
    public async Task ReorderAsync(Guid lessonId, ReorderContentRequest request, CancellationToken token = default)
    {
        await AccessAsync(lessonId, true, token);
        var list = await items.ListAsync(lessonId, token);
        if (request.ItemIds is null || request.ItemIds.Count != list.Count
            || request.ItemIds.Distinct().Count() != list.Count || list.Any(x => !request.ItemIds.Contains(x.Id)))
            throw new ArgumentException("Supply every item in this lesson exactly once.");
        var byId = list.ToDictionary(x => x.Id);
        for (var i = 0; i < request.ItemIds.Count; i++) byId[request.ItemIds[i]].Reorder(i);
        await unitOfWork.SaveChangesAsync(token);
    }
}
