using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.UpdateQuiz
{
    public sealed class UpdateQuizHandler
    {
        private readonly IQuizRepository _quizRepository;
        private readonly EnsureQuizEditable _ensureQuizEditable;

        public UpdateQuizHandler(
            IQuizRepository quizRepository, EnsureQuizEditable ensureQuizEditable)
        {
            _quizRepository = quizRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public async Task<QuizResponse> HandleAsync(
            Guid lessonId,
            UpdateQuizRequest request,
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

            await _ensureQuizEditable.CheckAsync(quiz.Id, cancellationToken);

            if (request.PassingPercentage < 0 ||
                request.PassingPercentage > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.PassingPercentage),
                    "Passing percentage must be between 0 and 100.");
            }

            if (request.TimeLimitInMinutes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.TimeLimitInMinutes),
                    "Time limit must be greater than 0.");
            }

            if (request.MaxAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.MaxAttempts),
                    "Maximum attempts must be greater than 0.");
            }

            quiz.Update(
                request.Title,
                request.Description,
                request.PassingPercentage,
                request.TimeLimitInMinutes,
                request.MaxAttempts);

            await _quizRepository.SaveChangesAsync(
                cancellationToken);

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
