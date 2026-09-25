using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.GetQuiz
{
    public sealed class GetQuizHandler
    {
        private readonly IQuizRepository _quizRepository;
        private readonly EnsureQuizReadable _ensureQuizReadable;

        public GetQuizHandler(
            IQuizRepository quizRepository, EnsureQuizReadable ensureQuizReadable)
        {
            _quizRepository = quizRepository;
            _ensureQuizReadable = ensureQuizReadable;
        }

        public async Task<QuizResponse> HandleAsync(
            Guid lessonId,
            Guid userId,
            bool canViewUnpublished,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByLessonIdAsync(
                lessonId,
                cancellationToken);

            await _ensureQuizReadable.CheckAsync(quiz, userId, canViewUnpublished, cancellationToken);

            return new QuizResponse
            {
                Id = quiz!.Id,
                LessonId = quiz.LessonId,
                Title = quiz.Title,
                Description = quiz.Description,
                PassingPercentage = quiz.PassingPercentage,
                TimeLimitInMinutes = quiz.TimeLimitInMinutes,
                MaxAttempts = quiz.MaxAttempts,
                IsPublished = quiz.IsPublished,
                CreatedAt = quiz.CreatedAt,
                UpdatedAt = quiz.UpdatedAt
            };
        }
    }
}
