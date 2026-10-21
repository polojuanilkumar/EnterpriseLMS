using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace LMS.Infrastructure.Repositories;

public sealed class LessonContentItemRepository(LMSDbContext context) : ILessonContentItemRepository
{
    public async Task<IReadOnlyList<LessonContentItem>> ListAsync(Guid lessonId, CancellationToken token = default)
        => await context.LessonContentItems.Where(x => x.LessonId == lessonId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(token);
    public Task<LessonContentItem?> GetAsync(Guid id, CancellationToken token = default)
        => context.LessonContentItems.SingleOrDefaultAsync(x => x.Id == id, token);
    public void Add(LessonContentItem item) => context.LessonContentItems.Add(item);
    public void Remove(LessonContentItem item) => context.LessonContentItems.Remove(item);
}
