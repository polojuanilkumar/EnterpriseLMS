using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizQuestions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizQuestions.DeleteQuestion
{
    public sealed class DeleteQuestionHandler
    {
        private readonly EnsureQuizOwnership _ownership;
        private readonly IQuizQuestionRepository _questionRepository;

        private readonly EnsureQuizEditable _ensureQuizEditable;

        public DeleteQuestionHandler(
            IQuizQuestionRepository questionRepository, EnsureQuizEditable ensureQuizEditable,
            EnsureQuizOwnership ownership)
        {
            _ownership = ownership;
            _questionRepository = questionRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public Task HandleAsync(
            Guid quizId,
            Guid questionId,
            Guid currentUserId,
            bool isAdmin,
            bool isInstructor,
            CancellationToken cancellationToken = default)
            => _ensureQuizEditable.ExecuteAsync(quizId, token => HandleCoreAsync(quizId, questionId, currentUserId, isAdmin, isInstructor, token), cancellationToken);

        private async Task HandleCoreAsync(
            Guid quizId,
            Guid questionId,
            Guid currentUserId,
            bool isAdmin,
            bool isInstructor,
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
            await _ownership.CheckQuizAsync(question.QuizId, currentUserId, isAdmin, isInstructor, cancellationToken);

            await _ensureQuizEditable.CheckAsync(quizId, cancellationToken);

            _questionRepository.Remove(question);

            await _questionRepository.SaveChangesAsync(
                cancellationToken);
        }
    }
}
