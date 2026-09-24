using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.UnpublishQuiz
{
    public sealed class UnpublishQuizHandler
    {
        private readonly IQuizRepository _quizRepository;

        public UnpublishQuizHandler(
            IQuizRepository quizRepository)
        {
            _quizRepository = quizRepository;
        }

        public async Task HandleAsync(
            Guid lessonId,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByLessonIdAsync(
                lessonId,
                cancellationToken);

            if (quiz is null)
            {
                throw new KeyNotFoundException(
                    "Quiz not found.");
            }

            quiz.Unpublish();

            await _quizRepository.SaveChangesAsync(
                cancellationToken);
        }
    }
}
