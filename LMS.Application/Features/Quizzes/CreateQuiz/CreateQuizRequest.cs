using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.CreateQuiz
{
    public sealed class CreateQuizRequest
    {
        public string Title { get; init; } = null!;

        public string? Description { get; init; }

        public decimal PassingPercentage { get; init; }

        public int TimeLimitInMinutes { get; init; }

        public int MaxAttempts { get; init; }
    }
}
