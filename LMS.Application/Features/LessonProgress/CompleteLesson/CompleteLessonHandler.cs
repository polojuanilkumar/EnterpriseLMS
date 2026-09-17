using LMS.Application.Features.LessonProgress.Common;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.LessonProgresses;
using LMS.Application.Interfaces.Lessons;
using LessonProgressEntity = LMS.Domain.Entities.LessonProgress;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.LessonProgress.CompleteLesson
{
    public sealed class CompleteLessonHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly ILessonProgressRepository _lessonProgressRepository;
        private readonly IIdentityService _identityService;

        public CompleteLessonHandler(
            ILessonRepository lessonRepository,
            ILessonProgressRepository lessonProgressRepository,
            IIdentityService identityService)
        {
            _lessonRepository = lessonRepository;
            _lessonProgressRepository = lessonProgressRepository;
            _identityService = identityService;
        }

        public async Task<LessonProgressResponse> HandleAsync(
            Guid lessonId,
            string email,
            CancellationToken cancellationToken = default)
        {
            var lesson = await _lessonRepository.GetByIdAsync(
                lessonId,
                cancellationToken);

            if (lesson is null)
            {
                throw new KeyNotFoundException("Lesson not found.");
            }

            var userId = await _identityService.GetUserIdAsync(email);

            if (!userId.HasValue)
            {
                throw new UnauthorizedAccessException(
                    "Authenticated user not found.");
            }

            var progress =
                await _lessonProgressRepository.GetByUserAndLessonAsync(
                    userId.Value,
                    lessonId,
                    cancellationToken);

            if (progress is null)
            {
                throw new KeyNotFoundException(
                    "Lesson progress not found. Start the lesson first.");
            }

            progress.Complete();

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
