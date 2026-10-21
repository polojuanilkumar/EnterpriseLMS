using LMS.Domain.Entities;
namespace LMS.Application.Interfaces.Lessons;

public interface ILessonContentItemRepository
{
    Task<IReadOnlyList<LessonContentItem>> ListAsync(Guid lessonId, CancellationToken token = default);
    Task<LessonContentItem?> GetAsync(Guid id, CancellationToken token = default);
    void Add(LessonContentItem item);
    void Remove(LessonContentItem item);
}
