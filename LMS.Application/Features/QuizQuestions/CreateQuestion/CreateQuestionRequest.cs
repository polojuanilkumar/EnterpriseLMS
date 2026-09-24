using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizQuestions.CreateQuestion
{
    public sealed class CreateQuestionRequest
    {
        public string QuestionText { get; init; } = null!;

        public QuestionType QuestionType { get; init; }

        public int DisplayOrder { get; init; }

        public decimal Marks { get; init; }
    }
}
