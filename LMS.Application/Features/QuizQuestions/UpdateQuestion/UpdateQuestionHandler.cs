using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizQuestions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizQuestions.UpdateQuestion
{
    public sealed class UpdateQuestionHandler
    {
        private readonly EnsureQuizOwnership _ownership;
        private readonly IQuizQuestionRepository _questionRepository;

        private readonly EnsureQuizEditable _ensureQuizEditable;

        public UpdateQuestionHandler(
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
            UpdateQuestionRequest request,
            Guid currentUserId,
            bool isAdmin,
            bool isInstructor,
            CancellationToken cancellationToken = default)
            => _ensureQuizEditable.ExecuteAsync(quizId, token => HandleCoreAsync(quizId, questionId, request, currentUserId, isAdmin, isInstructor, token), cancellationToken);

        private async Task HandleCoreAsync(
            Guid quizId,
            Guid questionId,
            UpdateQuestionRequest request,
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

            if (string.IsNullOrWhiteSpace(request.QuestionText))
            {
                throw new ArgumentException(
                    "Question text is required.");
            }

            if (request.DisplayOrder <= 0)
            {
                throw new ArgumentException(
                    "Display order must be greater than zero.");
            }

            if (request.Marks <= 0)
            {
                throw new ArgumentException(
                    "Marks must be greater than zero.");
            }

            var existingQuestions =
                await _questionRepository.GetByQuizIdAsync(
                    quizId,
                    cancellationToken);

            if (existingQuestions.Any(x =>
                x.Id != questionId &&
                x.DisplayOrder == request.DisplayOrder))
            {
                throw new InvalidOperationException(
                    "A question with the same display order already exists.");
            }

            question.Update(
                request.QuestionText,
                request.QuestionType,
                request.DisplayOrder,
                request.Marks);

            await _questionRepository.SaveChangesAsync(
                cancellationToken);
        }
    }
}
