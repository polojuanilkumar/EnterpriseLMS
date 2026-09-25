using LMS.Application.Features.Quizzes.Common;
﻿using LMS.Application.Features.QuizQuestions.Common;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizQuestions.GetQuestions
{
    public sealed class GetQuestionsHandler
    {
        private readonly IQuizQuestionRepository _questionRepository;
        private readonly IQuizRepository _quizRepository;
        private readonly EnsureQuizReadable _ensureQuizReadable;
        public GetQuestionsHandler(
            IQuizQuestionRepository questionRepository, IQuizRepository quizRepository, EnsureQuizReadable ensureQuizReadable)
        {
            _questionRepository = questionRepository;
            _quizRepository = quizRepository;
            _ensureQuizReadable = ensureQuizReadable;
        }

        public async Task<IReadOnlyList<QuizQuestionResponse>> HandleAsync(
            Guid quizId,
            Guid userId,
            bool canViewUnpublished,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByIdAsync(
    quizId, cancellationToken);

            await _ensureQuizReadable.CheckAsync(quiz, userId, canViewUnpublished,
                cancellationToken, "Quiz not found.");


            var questions =
                await _questionRepository.GetByQuizIdAsync(
                    quizId,
                    cancellationToken);

            return questions
                .Select(x => new QuizQuestionResponse
                {
                    Id = x.Id,
                    QuizId = x.QuizId,
                    QuestionText = x.QuestionText,
                    QuestionType = x.QuestionType,
                    DisplayOrder = x.DisplayOrder,
                    Marks = x.Marks,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToList();
        }
    }
}
