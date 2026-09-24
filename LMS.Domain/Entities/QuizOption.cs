using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class QuizOption
    {
        private QuizOption()
        {
        }

        public QuizOption(
            Guid questionId,
            string optionText,
            bool isCorrect,
            int displayOrder)
        {
            Id = Guid.NewGuid();
            QuestionId = questionId;
            OptionText = optionText;
            IsCorrect = isCorrect;
            DisplayOrder = displayOrder;
            CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }

        public Guid QuestionId { get; private set; }

        public string OptionText { get; private set; } = null!;

        public bool IsCorrect { get; private set; }

        public int DisplayOrder { get; private set; }

        public DateTime CreatedAt { get; private set; } 

        public DateTime? UpdatedAt { get; private set; }

        public void Update(
            string optionText,
            bool isCorrect,
            int displayOrder)
        {
            OptionText = optionText;
            IsCorrect = isCorrect;
            DisplayOrder = displayOrder;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
