using LMS.Application.Features.QuizQuestions.Common;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizQuestions.GetQuestionById
{
    public sealed class GetQuestionByIdHandler
    {
        private readonly IQuizQuestionRepository _questionRepository;

        private readonly IQuizRepository _quizRepository;

        public GetQuestionByIdHandler(
            IQuizQuestionRepository questionRepository, IQuizRepository quizRepository)
        {
            _questionRepository = questionRepository;
            _quizRepository = quizRepository;
        }

        public async Task<QuizQuestionResponse> HandleAsync(
            Guid questionId,
            bool canViewUnpublished,
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

            var quiz = await _quizRepository.GetByIdAsync(
question.QuizId, cancellationToken);

            if (quiz is null || (!quiz.IsPublished && !canViewUnpublished))
                throw new KeyNotFoundException("Question not found.");

            return new QuizQuestionResponse
            {
                Id = question.Id,
                QuizId = question.QuizId,
                QuestionText = question.QuestionText,
                QuestionType = question.QuestionType,
                DisplayOrder = question.DisplayOrder,
                Marks = question.Marks,
                CreatedAt = question.CreatedAt,
                UpdatedAt = question.UpdatedAt
            };
        }
    }
}
