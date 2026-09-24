using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Interfaces.Quizzes;
using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizQuestions.CreateQuestion
{
    public sealed class CreateQuestionHandler
    {
        private readonly IQuizQuestionRepository _questionRepository;
        private readonly IQuizRepository _quizRepository;

        private readonly EnsureQuizEditable _ensureQuizEditable;

        public CreateQuestionHandler(
            IQuizQuestionRepository questionRepository,
            IQuizRepository quizRepository, EnsureQuizEditable ensureQuizEditable)
        {
            _questionRepository = questionRepository;
            _quizRepository = quizRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public async Task<Guid> HandleAsync(
            Guid quizId,
            CreateQuestionRequest request,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByIdAsync(
                quizId,
                cancellationToken);

            if (quiz is null)
            {
                throw new KeyNotFoundException(
                    "Quiz not found.");
            }

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
                x.DisplayOrder == request.DisplayOrder))
            {
                throw new InvalidOperationException(
                    "A question with the same display order already exists.");
            }

            var question = new QuizQuestion(
                quizId,
                request.QuestionText,
                request.QuestionType,
                request.DisplayOrder,
                request.Marks);

            await _questionRepository.AddAsync(
                question,
                cancellationToken);

            await _questionRepository.SaveChangesAsync(
                cancellationToken);

            return question.Id;
        }
    }
}
