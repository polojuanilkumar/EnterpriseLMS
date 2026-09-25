using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizOptions.CreateOption
{
    public sealed class CreateOptionHandler
    {
        private readonly IQuizOptionRepository _optionRepository;
        private readonly IQuizQuestionRepository _questionRepository;
        private readonly EnsureQuizEditable _ensureQuizEditable;

        public CreateOptionHandler(
            IQuizOptionRepository optionRepository,
            IQuizQuestionRepository questionRepository, EnsureQuizEditable ensureQuizEditable)
        {
            _optionRepository = optionRepository;
            _questionRepository = questionRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public Task<Guid> HandleAsync(
            Guid quizId,
            Guid questionId,
            CreateOptionRequest request,
            CancellationToken cancellationToken = default)
            => _ensureQuizEditable.ExecuteAsync(quizId, token => HandleCoreAsync(quizId, questionId, request, token), cancellationToken);

        private async Task<Guid> HandleCoreAsync(
            Guid quizId,
            Guid questionId,
            CreateOptionRequest request,
            CancellationToken cancellationToken = default)
        {
            var question = await _questionRepository.GetByIdAsync(
                questionId,
                cancellationToken);

            if (question is null || question.QuizId != quizId)
            {
                throw new KeyNotFoundException("Question not found.");
            }

            if (string.IsNullOrWhiteSpace(request.OptionText))
            {
                throw new ArgumentException("Option text is required.");
            }

            if (request.DisplayOrder <= 0)
            {
                throw new ArgumentException(
                    "Display order must be greater than zero.");
            }

            var existingOptions =
                await _optionRepository.GetByQuestionIdAsync(
                    questionId,
                    cancellationToken);

            if (existingOptions.Any(x =>
                x.DisplayOrder == request.DisplayOrder))
            {
                throw new InvalidOperationException(
                    "An option with the same display order already exists.");
            }

            await _ensureQuizEditable.CheckAsync(quizId, cancellationToken);

            var option = new QuizOption(
                questionId,
                request.OptionText.Trim(),
                request.IsCorrect,
                request.DisplayOrder);

            await _optionRepository.AddAsync(option, cancellationToken);
            await _optionRepository.SaveChangesAsync(cancellationToken);

            return option.Id;
        }
    }
}
