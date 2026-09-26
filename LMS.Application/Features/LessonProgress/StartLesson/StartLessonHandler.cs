using LMS.Application.Features.LessonProgress.Common;
using LMS.Application.Features.Lessons.Common;
using LMS.Application.Interfaces.LessonProgresses;
using LMS.Application.Interfaces.Lessons;
using LessonProgressEntity = LMS.Domain.Entities.LessonProgress;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.LessonProgress.StartLesson
{
    public sealed class StartLessonHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly ILessonProgressRepository _lessonProgressRepository;
        private readonly EnsureLessonReadable _readable;

        public StartLessonHandler(
            ILessonRepository lessonRepository,
            ILessonProgressRepository lessonProgressRepository,
            EnsureLessonReadable readable)
        {
            _lessonRepository = lessonRepository;
            _lessonProgressRepository = lessonProgressRepository;
            _readable = readable;
        }

        public async Task<LessonProgressResponse> HandleAsync(
            Guid lessonId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var lesson = await _lessonRepository.GetByIdAsync(
                lessonId,
                cancellationToken);

            if (lesson is null)
            {
                throw new KeyNotFoundException("Lesson not found.");
            }

            await _readable.CheckAsync(lesson, userId, canViewUnpublished: false, cancellationToken);

            var existingProgress =
                await _lessonProgressRepository.GetByUserAndLessonAsync(
                    userId,
                    lessonId,
                    cancellationToken);

            if (existingProgress is not null)
            {
                if (existingProgress.UserId != userId)
                    throw new UnauthorizedAccessException("You are not authorized to access this lesson progress.");
                if (existingProgress.LessonId != lesson.Id)
                    throw new KeyNotFoundException("Lesson progress not found.");

                existingProgress.Access();

                await _lessonProgressRepository.SaveChangesAsync(
                    cancellationToken);

                return MapToResponse(existingProgress);
            }

            var progress = new LessonProgressEntity(
                userId,
                lessonId);

            await _lessonProgressRepository.AddAsync(
                progress,
                cancellationToken);

            await _lessonProgressRepository.SaveChangesAsync(
                cancellationToken);

            return MapToResponse(progress);
        }

        private static LessonProgressResponse MapToResponse(
            LessonProgressEntity progress)
        {
            return new LessonProgressResponse
            {
                Id = progress.Id,
                UserId = progress.UserId,
                LessonId = progress.LessonId,
                IsCompleted = progress.IsCompleted,
                CompletedAt = progress.CompletedAt,
                StartedAt = progress.StartedAt,
                ProgressPercentage = progress.ProgressPercentage,
                LastAccessedAt = progress.LastAccessedAt
            };
        }
    }
}
