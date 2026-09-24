using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizOptions.GetOptions
{
    public sealed class GetOptionsHandler
    {
        private readonly IQuizOptionRepository _optionRepository;
        private readonly IQuizQuestionRepository _questionRepository;

        public GetOptionsHandler(
            IQuizOptionRepository optionRepository,
            IQuizQuestionRepository questionRepository)
        {
            _optionRepository = optionRepository;
            _questionRepository = questionRepository;
        }

        public async Task<IReadOnlyList<QuizOptionResponse>> HandleAsync(
            Guid quizId,
            Guid questionId,
            CancellationToken cancellationToken = default)
        {
            var question = await _questionRepository.GetByIdAsync(
                questionId, cancellationToken);

            if (question is null || question.QuizId != quizId)
                throw new KeyNotFoundException("Question not found.");

            var options = await _optionRepository.GetByQuestionIdAsync(
                questionId, cancellationToken);

            return options.Select(x => new QuizOptionResponse
            {
                Id = x.Id,
                QuestionId = x.QuestionId,
                OptionText = x.OptionText,
                IsCorrect = x.IsCorrect,
                DisplayOrder = x.DisplayOrder
            }).ToList();
        }
    }

    public sealed class QuizOptionResponse
    {
        public Guid Id { get; init; }
        public Guid QuestionId { get; init; } 
        public string OptionText { get; init; } = null!;
        public bool IsCorrect { get; init; }
        public int DisplayOrder { get; init; }
    }
}
