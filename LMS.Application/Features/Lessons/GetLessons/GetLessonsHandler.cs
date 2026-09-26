using LMS.Application.Features.Lessons.Common;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Lessons;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.GetLessons
{
    public sealed class GetLessonsHandler
    {
        private readonly EnsureLessonReadable _readable;
        private readonly ILessonRepository _lessonRepository;
        private readonly ICourseSectionRepository _sectionRepository;

        public GetLessonsHandler(
            ILessonRepository lessonRepository,
            ICourseSectionRepository sectionRepository,
            EnsureLessonReadable readable)
        {
            _readable = readable;
            _lessonRepository = lessonRepository;
            _sectionRepository = sectionRepository;
        }

        public async Task<IReadOnlyList<LessonResponse>> HandleAsync(
            Guid sectionId,
            Guid userId,
            bool canViewUnpublished,
            CancellationToken cancellationToken = default)
        {
            var section =
                await _sectionRepository.GetByIdAsync(
                    sectionId,
                    cancellationToken);

            if (section is null)
            {
                throw new KeyNotFoundException(
                    "Course section not found.");
            }

            await _readable.CheckCourseAsync(section.CourseId, userId, canViewUnpublished, cancellationToken);

            var lessons =
                await _lessonRepository.GetBySectionIdAsync(
                    sectionId,
                    cancellationToken);

            return lessons
                .Where(x => canViewUnpublished || x.IsPublished)
                .Select(x => new LessonResponse
                {
                    Id = x.Id,
                    SectionId = x.SectionId,
                    Title = x.Title,
                    Description = x.Description,
                    LessonType = x.LessonType.ToString(),
                    Content = x.Content,
                    VideoUrl = x.VideoUrl,
                    DurationInMinutes = x.DurationInMinutes,
                    DisplayOrder = x.DisplayOrder,
                    IsPublished = x.IsPublished,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToList();
        }
    }
}
