using LMS.Application.Features.Lessons.Common;
using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;

namespace LMS.Application.Features.Quizzes.Common
{
    public sealed class EnsureQuizReadable
    {
        private readonly ILessonRepository _lessons;
        private readonly EnsureLessonReadable _lessonReadable;

        public EnsureQuizReadable(ILessonRepository lessons, EnsureLessonReadable lessonReadable)
        {
            _lessons = lessons;
            _lessonReadable = lessonReadable;
        }

        public async Task CheckAsync(Quiz? quiz, Guid userId, bool canViewUnpublished,
            CancellationToken cancellationToken = default, string notFoundMessage = "Quiz not found.")
        {
            if (quiz is null || (!quiz.IsPublished && !canViewUnpublished))
                throw new KeyNotFoundException(notFoundMessage);

            if (canViewUnpublished)
                return;

            if (userId == Guid.Empty)
                throw new UnauthorizedAccessException("Authenticated user ID was not found.");

            var lesson = await _lessons.GetByIdAsync(quiz.LessonId, cancellationToken)
                ?? throw new KeyNotFoundException("Lesson not found.");
            await _lessonReadable.CheckAsync(lesson, userId, canViewUnpublished: false, cancellationToken);
        }
    }
}
