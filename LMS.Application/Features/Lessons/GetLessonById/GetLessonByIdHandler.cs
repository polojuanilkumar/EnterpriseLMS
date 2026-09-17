using LMS.Application.Features.Lessons.Common;
using LMS.Application.Interfaces.Lessons;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.GetLessonById
{
    public sealed class GetLessonByIdHandler
    {
        private readonly ILessonRepository _lessonRepository;

        public GetLessonByIdHandler(
            ILessonRepository lessonRepository)
        {
            _lessonRepository = lessonRepository;
        }

        public async Task<LessonResponse> HandleAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var lesson =
                await _lessonRepository.GetByIdAsync(
                    id,
                    cancellationToken);

            if (lesson is null)
            {
                throw new KeyNotFoundException(
                    "Lesson not found.");
            }

            return new LessonResponse
            {
                Id = lesson.Id,
                SectionId = lesson.SectionId,
                Title = lesson.Title,
                Description = lesson.Description,
                LessonType = lesson.LessonType.ToString(),
                Content = lesson.Content,
                VideoUrl = lesson.VideoUrl,
                DurationInMinutes = lesson.DurationInMinutes,
                DisplayOrder = lesson.DisplayOrder,
                IsPublished = lesson.IsPublished,
                CreatedAt = lesson.CreatedAt,
                UpdatedAt = lesson.UpdatedAt
            };
        }
    }
}
