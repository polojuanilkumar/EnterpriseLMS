using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Quizzes;
using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.CreateQuiz
{
    public sealed class CreateQuizHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly IQuizRepository _quizRepository;

        public CreateQuizHandler(
            ILessonRepository lessonRepository,
            IQuizRepository quizRepository)
        {
            _lessonRepository = lessonRepository;
            _quizRepository = quizRepository;
        }

        public async Task<QuizResponse> HandleAsync(
            Guid lessonId,
            CreateQuizRequest request,
            CancellationToken cancellationToken = default)
        {
            var lesson = await _lessonRepository.GetByIdAsync(
                lessonId,
                cancellationToken);

            if (lesson is null)
            {
                throw new KeyNotFoundException(
                    "Lesson not found.");
            }

            var existingQuiz =
                await _quizRepository.GetByLessonIdAsync(
                    lessonId,
                    cancellationToken);

            if (existingQuiz is not null)
            {
                throw new InvalidOperationException(
                    "A quiz already exists for this lesson.");
            }

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

            var quiz = new Quiz(
                lessonId,
                request.Title,
                request.Description,
                request.PassingPercentage,
                request.TimeLimitInMinutes,
                request.MaxAttempts);

            await _quizRepository.AddAsync(
                quiz,
                cancellationToken);

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
