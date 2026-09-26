using LMS.Application.Features.Lessons.Common;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Quizzes;

namespace LMS.Application.Features.Quizzes.Common
{
    public sealed class EnsureQuizOwnership
    {
        private readonly ILessonRepository _lessons;
        private readonly IQuizRepository _quizzes;
        private readonly EnsureLessonOwnership _lessonOwnership;

        public EnsureQuizOwnership(ILessonRepository lessons, IQuizRepository quizzes,
            EnsureLessonOwnership lessonOwnership)
        {
            _lessons = lessons;
            _quizzes = quizzes;
            _lessonOwnership = lessonOwnership;
        }

        public async Task CheckQuizAsync(Guid quizId, Guid currentUserId, bool isAdmin,
            bool isInstructor, CancellationToken cancellationToken = default)
        {
            var quiz = await _quizzes.GetByIdAsync(quizId, cancellationToken);
            if (quiz is null || quiz.Id != quizId)
                throw new KeyNotFoundException("Quiz not found.");

            await CheckLessonAsync(quiz.LessonId, currentUserId, isAdmin, isInstructor, cancellationToken);
        }

        public async Task CheckLessonAsync(Guid lessonId, Guid currentUserId, bool isAdmin,
            bool isInstructor, CancellationToken cancellationToken = default)
        {
            var lesson = await _lessons.GetByIdAsync(lessonId, cancellationToken);
            if (lesson is null || lesson.Id != lessonId)
                throw new KeyNotFoundException("Lesson not found.");

            if (currentUserId == Guid.Empty || (!isAdmin && !isInstructor))
                throw new UnauthorizedAccessException("You are not authorized to change quiz content.");

            await _lessonOwnership.CheckSectionAsync(lesson.SectionId, currentUserId, isAdmin, cancellationToken);
        }
    }
}
