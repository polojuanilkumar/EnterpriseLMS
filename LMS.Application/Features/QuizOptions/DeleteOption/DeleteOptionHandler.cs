using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizOptions.DeleteOption
{
    public sealed class DeleteOptionHandler
    {
        private readonly EnsureQuizOwnership _ownership;
        private readonly IQuizOptionRepository _optionRepository;
        private readonly IQuizQuestionRepository _questionRepository;

        private readonly EnsureQuizEditable _ensureQuizEditable;

        public DeleteOptionHandler(
            IQuizOptionRepository optionRepository,
            IQuizQuestionRepository questionRepository,
            EnsureQuizEditable ensureQuizEditable,
            EnsureQuizOwnership ownership)
        {
            _ownership = ownership;
            _optionRepository = optionRepository;
            _questionRepository = questionRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public Task HandleAsync(
            Guid quizId,
            Guid questionId,
            Guid optionId,
            Guid currentUserId,
            bool isAdmin,
            bool isInstructor,
            CancellationToken cancellationToken = default)
            => _ensureQuizEditable.ExecuteAsync(quizId, token => HandleCoreAsync(quizId, questionId, optionId, currentUserId, isAdmin, isInstructor, token), cancellationToken);

        private async Task HandleCoreAsync(
            Guid quizId,
            Guid questionId,
            Guid optionId,
            Guid currentUserId,
            bool isAdmin,
            bool isInstructor,
            CancellationToken cancellationToken = default)
        {
            var question = await _questionRepository.GetByIdAsync(
                questionId,
                cancellationToken);

            if (question is null || question.QuizId != quizId)
                throw new KeyNotFoundException("Question not found.");

            var option = await _optionRepository.GetByIdAsync(
                optionId,
                cancellationToken);

            if (option is null || option.QuestionId != questionId)
                throw new KeyNotFoundException("Option not found.");

            await _ownership.CheckQuizAsync(question.QuizId, currentUserId, isAdmin, isInstructor, cancellationToken);

            await _ensureQuizEditable.CheckAsync(quizId, cancellationToken);

            _optionRepository.Remove(option);

            await _optionRepository.SaveChangesAsync(cancellationToken);
        }
    }
}
