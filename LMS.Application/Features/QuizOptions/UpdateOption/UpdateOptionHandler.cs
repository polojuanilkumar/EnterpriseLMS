using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizOptions.UpdateOption
{
    public sealed class UpdateOptionHandler
    {
        private readonly IQuizOptionRepository _optionRepository;
        private readonly IQuizQuestionRepository _questionRepository;

        private readonly EnsureQuizEditable _ensureQuizEditable;

        public UpdateOptionHandler(
            IQuizOptionRepository optionRepository,
            IQuizQuestionRepository questionRepository, EnsureQuizEditable ensureQuizEditable)
        {
            _optionRepository = optionRepository;
            _questionRepository = questionRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public async Task HandleAsync(
            Guid quizId,
            Guid questionId,
            Guid optionId,
            UpdateOptionRequest request,
            CancellationToken cancellationToken = default)
        {
            var question = await _questionRepository.GetByIdAsync(
                questionId, cancellationToken);

            if (question is null || question.QuizId != quizId)
                throw new KeyNotFoundException("Question not found.");

            var option = await _optionRepository.GetByIdAsync(
                optionId, cancellationToken);

            if (option is null || option.QuestionId != questionId)
                throw new KeyNotFoundException("Option not found.");

            if (string.IsNullOrWhiteSpace(request.OptionText))
                throw new ArgumentException("Option text is required.");

            if (request.DisplayOrder <= 0)
                throw new ArgumentException(
                    "Display order must be greater than zero.");

            var existingOptions =
                await _optionRepository.GetByQuestionIdAsync(
                    questionId, cancellationToken);

            if (existingOptions.Any(x =>
                x.Id != optionId &&
                x.DisplayOrder == request.DisplayOrder))
            {
                throw new InvalidOperationException(
                    "An option with the same display order already exists.");
            }

            await _ensureQuizEditable.CheckAsync(quizId, cancellationToken);

            option.Update(
                request.OptionText.Trim(),
                request.IsCorrect,
                request.DisplayOrder);
             
            await _optionRepository.SaveChangesAsync(cancellationToken);
        }
    }
}
