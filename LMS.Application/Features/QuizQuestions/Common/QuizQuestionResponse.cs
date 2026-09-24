using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizQuestions.Common
{
    public sealed class QuizQuestionResponse
    {
        public Guid Id { get; init; }

        public Guid QuizId { get; init; }

        public string QuestionText { get; init; } = null!;

        public QuestionType QuestionType { get; init; }

        public int DisplayOrder { get; init; }

        public decimal Marks { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }
    }
}
