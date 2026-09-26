using LMS.Application.Features.CourseProgress.Common;
using LMS.Application.Features.Lessons.Common;
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
        private readonly EnsureLessonReadable _readable;

        public GetCourseProgressHandler(
            ILessonRepository lessonRepository,
            ILessonProgressRepository lessonProgressRepository,
            EnsureLessonReadable readable)
        {
            _lessonRepository = lessonRepository;
            _lessonProgressRepository = lessonProgressRepository;
            _readable = readable;
        }

        public async Task<CourseProgressResponse> HandleAsync(
            Guid courseId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            await _readable.CheckCourseAsync(courseId, userId, canViewUnpublished: false, cancellationToken);

            var lessons = await _lessonRepository.GetByCourseIdAsync(
                courseId,
                cancellationToken);

            var lessonIds = lessons
                .Where(x => x.IsPublished)
                .Select(x => x.Id)
                .ToList();

            var totalLessons = lessonIds.Count;
            var completedLessons = 0;

            // An accessible course with no published lessons has no progress to count.
            if (totalLessons > 0)
            {
                var progressRecords =
                    await _lessonProgressRepository.GetByUserAndLessonIdsAsync(
                        userId,
                        lessonIds,
                        cancellationToken);

                var publishedLessonIds = lessonIds.ToHashSet();
                completedLessons = progressRecords
                    .Where(x => x.UserId == userId && x.IsCompleted
                        && publishedLessonIds.Contains(x.LessonId))
                    .Select(x => x.LessonId)
                    .Distinct()
                    .Count();
            }

            var progressPercentage = totalLessons == 0
                ? 0
                : Math.Round(
                    (decimal)completedLessons / totalLessons * 100,
                    2);

            return new CourseProgressResponse
            {
                CourseId = courseId,
                UserId = userId,
                TotalLessons = totalLessons,
                CompletedLessons = completedLessons,
                ProgressPercentage = progressPercentage,
                IsCompleted = totalLessons > 0 &&
                              completedLessons == totalLessons
            };
        }
    }
}
