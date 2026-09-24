using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.DeleteQuiz
{
    public sealed class DeleteQuizHandler
    {
        private readonly IQuizRepository _quizRepository;
        private readonly EnsureQuizEditable _ensureQuizEditable;
        public DeleteQuizHandler(
            IQuizRepository quizRepository, EnsureQuizEditable ensureQuizEditable)
        {
            _quizRepository = quizRepository;
            _ensureQuizEditable = ensureQuizEditable;
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
            await _ensureQuizEditable.CheckAsync(quiz.Id, cancellationToken);
            _quizRepository.Remove(quiz);

            await _quizRepository.SaveChangesAsync(  
                cancellationToken);
        }
    }
}
