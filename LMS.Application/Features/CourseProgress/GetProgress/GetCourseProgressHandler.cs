using LMS.Application.Features.CourseProgress.Common;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.LessonProgresses;
using LMS.Application.Interfaces.Lessons;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseProgress.GetProgress
{
    public sealed class GetCourseProgressHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly ILessonProgressRepository _lessonProgressRepository;
        private readonly IIdentityService _identityService;

        public GetCourseProgressHandler(
            ILessonRepository lessonRepository,
            ILessonProgressRepository lessonProgressRepository,
            IIdentityService identityService)
        {
            _lessonRepository = lessonRepository;
            _lessonProgressRepository = lessonProgressRepository;
            _identityService = identityService;
        }

        public async Task<CourseProgressResponse> HandleAsync(
            Guid courseId,
            string email,
            CancellationToken cancellationToken = default)
        {
            var userId = await _identityService.GetUserIdAsync(email);

            if (!userId.HasValue)
            {
                throw new UnauthorizedAccessException(
                    "Authenticated user not found.");
            }

            var lessons = await _lessonRepository.GetByCourseIdAsync(
                courseId,
                cancellationToken);

            var lessonIds = lessons
                .Select(x => x.Id)
                .ToList();

            var progressRecords =
                await _lessonProgressRepository.GetByUserAndLessonIdsAsync(
                    userId.Value,
                    lessonIds,
                    cancellationToken);

            var completedLessons = progressRecords
                .Count(x => x.IsCompleted);

            var totalLessons = lessons.Count;

            var progressPercentage = totalLessons == 0
                ? 0
                : Math.Round(
                    (decimal)completedLessons / totalLessons * 100,
                    2);

            return new CourseProgressResponse
            {
                CourseId = courseId,
                UserId = userId.Value,
                TotalLessons = totalLessons,
                CompletedLessons = completedLessons,
                ProgressPercentage = progressPercentage,
                IsCompleted = totalLessons > 0 &&
                              completedLessons == totalLessons
            };
        }
    }
}
