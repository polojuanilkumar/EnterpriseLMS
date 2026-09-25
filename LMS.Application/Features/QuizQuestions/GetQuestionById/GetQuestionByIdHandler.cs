using LMS.Application.Features.Quizzes.Common;
﻿using LMS.Application.Features.QuizQuestions.Common;
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
        private readonly EnsureQuizReadable _ensureQuizReadable;

        public GetQuestionByIdHandler(
            IQuizQuestionRepository questionRepository, IQuizRepository quizRepository, EnsureQuizReadable ensureQuizReadable)
        {
            _questionRepository = questionRepository;
            _quizRepository = quizRepository;
            _ensureQuizReadable = ensureQuizReadable;
        }

        public async Task<QuizQuestionResponse> HandleAsync(
            Guid questionId,
            Guid userId,
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

            await _ensureQuizReadable.CheckAsync(quiz, userId, canViewUnpublished,
                cancellationToken, "Question not found.");

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
