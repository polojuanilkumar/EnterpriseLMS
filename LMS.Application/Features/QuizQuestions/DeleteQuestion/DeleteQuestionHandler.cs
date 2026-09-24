using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizQuestions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizQuestions.DeleteQuestion
{
    public sealed class DeleteQuestionHandler
    {
        private readonly IQuizQuestionRepository _questionRepository;

        private readonly EnsureQuizEditable _ensureQuizEditable;

        public DeleteQuestionHandler(
            IQuizQuestionRepository questionRepository, EnsureQuizEditable ensureQuizEditable)
        {
            _questionRepository = questionRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public async Task HandleAsync(
            Guid quizId,
            Guid questionId,
            CancellationToken cancellationToken = default)
        {
            var question =
                await _questionRepository.GetByIdAsync(
                    questionId,
                    cancellationToken);

            if (question is null)
            {
                throw new KeyNotFoundException(
                    "Question not found."); 
            }

            if (question.QuizId != quizId)
            {
                throw new KeyNotFoundException(
                    "Question not found.");
            }
            await _ensureQuizEditable.CheckAsync(quizId, cancellationToken);

            _questionRepository.Remove(question);

            await _questionRepository.SaveChangesAsync(
                cancellationToken);
        }
    }
}
