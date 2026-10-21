using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.LessonProgresses;
using LMS.Application.Interfaces.Lessons;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseProgress.MyCourses
{
    public sealed class GetMyCoursesProgressHandler
    {
        private readonly ICourseEnrollmentRepository _enrollmentRepository;
        private readonly ICourseRepository _courseRepository;
        private readonly ILessonRepository _lessonRepository;
        private readonly ILessonProgressRepository _lessonProgressRepository;

        public GetMyCoursesProgressHandler(
            ICourseEnrollmentRepository enrollmentRepository,
            ICourseRepository courseRepository,
            ILessonRepository lessonRepository,
            ILessonProgressRepository lessonProgressRepository)
        {
            _enrollmentRepository = enrollmentRepository;
            _courseRepository = courseRepository;
            _lessonRepository = lessonRepository;
            _lessonProgressRepository = lessonProgressRepository;
        }

        public async Task<IReadOnlyList<MyCourseProgressResponse>> HandleAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
            {
                throw new UnauthorizedAccessException(
                    "Authenticated user not found.");
            }

            var enrollments =
                await _enrollmentRepository.GetByUserIdAsync(
                    userId,
                    cancellationToken);

            var response =
                new List<MyCourseProgressResponse>();

            foreach (var enrollment in enrollments)
            {
                var course = await _courseRepository.GetByIdAsync(
                    enrollment.CourseId,
                    cancellationToken);

                if (course is null)
                {
                    continue;
                }

                var lessons = await _lessonRepository.GetByCourseIdAsync(
                    course.Id,
                    cancellationToken);

                var lessonIds = lessons.Where(x => x.IsPublished)
                    .Select(x => x.Id)
                    .ToList();

                var progressRecords =
                    await _lessonProgressRepository
                        .GetByUserAndLessonIdsAsync(
                            userId,
                            lessonIds,
                            cancellationToken);

                var totalLessons = lessonIds.Count;

                var completedLessons = progressRecords
                    .Where(x => x.UserId == userId && x.IsCompleted && lessonIds.Contains(x.LessonId))
                    .Select(x => x.LessonId).Distinct().Count();

                var progressPercentage = totalLessons == 0
                    ? 0
                    : Math.Round(
                        (decimal)completedLessons /
                        totalLessons * 100,
                        2);

                response.Add(new MyCourseProgressResponse
                {
                    CourseId = course.Id,
                    CourseTitle = course.Title,
                    CourseCode = course.Code,
                    TotalLessons = totalLessons,
                    CompletedLessons = completedLessons,
                    ProgressPercentage = progressPercentage,
                    IsCompleted = totalLessons > 0 &&
                                  completedLessons == totalLessons
                });
            }

            return response;
        }
    }
}
