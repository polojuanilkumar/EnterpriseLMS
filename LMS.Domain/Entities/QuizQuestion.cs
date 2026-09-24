using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class QuizQuestion
    {
        private QuizQuestion()
        {
        }

        public QuizQuestion(
            Guid quizId,
            string questionText,
            QuestionType questionType,
            int displayOrder,
            decimal marks)
        {
            Id = Guid.NewGuid();
            QuizId = quizId;
            QuestionText = questionText;
            QuestionType = questionType;
            DisplayOrder = displayOrder;
            Marks = marks;
            CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }

        public Guid QuizId { get; private set; }

        public string QuestionText { get; private set; } = null!;

        public QuestionType QuestionType { get; private set; }

        public int DisplayOrder { get; private set; }

        public decimal Marks { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        public void Update(
            string questionText,
            QuestionType questionType,
            int displayOrder,
            decimal marks)
        {
            QuestionText = questionText;
            QuestionType = questionType;
            DisplayOrder = displayOrder;
            Marks = marks;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
