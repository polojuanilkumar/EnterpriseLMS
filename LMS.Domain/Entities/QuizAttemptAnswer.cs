namespace LMS.Domain.Entities
{
    public sealed class QuizAttemptAnswer
    {
        private QuizAttemptAnswer() { }

        public QuizAttemptAnswer(Guid attemptId, Guid questionId,
            string questionTextSnapshot, QuestionType questionTypeSnapshot,
            decimal marksSnapshot, decimal awardedMarks, bool isCorrect)
        {
            Id = Guid.NewGuid();
            AttemptId = attemptId;
            QuestionId = questionId;
            QuestionTextSnapshot = questionTextSnapshot;
            QuestionTypeSnapshot = questionTypeSnapshot;
            MarksSnapshot = marksSnapshot;
            AwardedMarks = awardedMarks;
            IsCorrect = isCorrect;
        }

        public Guid Id { get; private set; }
        public Guid AttemptId { get; private set; }
        public Guid QuestionId { get; private set; }
        public string QuestionTextSnapshot { get; private set; } = null!;
        public QuestionType QuestionTypeSnapshot { get; private set; }
        public decimal MarksSnapshot { get; private set; }
        public decimal AwardedMarks { get; private set; }
        public bool IsCorrect { get; private set; }
    }
}
