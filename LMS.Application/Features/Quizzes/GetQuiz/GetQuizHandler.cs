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

        public GetQuizHandler(
            IQuizRepository quizRepository)
        {
            _quizRepository = quizRepository;
        }

        public async Task<QuizResponse> HandleAsync(
            Guid lessonId,
            bool canViewUnpublished,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByLessonIdAsync(
                lessonId,
                cancellationToken);

            if (quiz is null)
            {
                throw new KeyNotFoundException(
                    "Quiz not found.");
            }
            if (!quiz.IsPublished && !canViewUnpublished)
            {
                throw new KeyNotFoundException("Quiz not found.");
            }
            return new QuizResponse
            {
                Id = quiz.Id,
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
