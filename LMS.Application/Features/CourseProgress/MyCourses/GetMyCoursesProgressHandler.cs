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
        private readonly IIdentityService _identityService;

        public GetMyCoursesProgressHandler(
            ICourseEnrollmentRepository enrollmentRepository,
            ICourseRepository courseRepository,
            ILessonRepository lessonRepository,
            ILessonProgressRepository lessonProgressRepository,
            IIdentityService identityService)
        {
            _enrollmentRepository = enrollmentRepository;
            _courseRepository = courseRepository;
            _lessonRepository = lessonRepository;
            _lessonProgressRepository = lessonProgressRepository;
            _identityService = identityService;
        }

        public async Task<IReadOnlyList<MyCourseProgressResponse>> HandleAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            var userId = await _identityService.GetUserIdAsync(email);

            if (!userId.HasValue)
            {
                throw new UnauthorizedAccessException(
                    "Authenticated user not found.");
            }

            var enrollments =
                await _enrollmentRepository.GetByUserIdAsync(
                    userId.Value,
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

                var lessonIds = lessons
                    .Select(x => x.Id)
                    .ToList();

                var progressRecords =
                    await _lessonProgressRepository
                        .GetByUserAndLessonIdsAsync(
                            userId.Value,
                            lessonIds,
                            cancellationToken);

                var totalLessons = lessons.Count;

                var completedLessons = progressRecords
                    .Count(x => x.IsCompleted);

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
