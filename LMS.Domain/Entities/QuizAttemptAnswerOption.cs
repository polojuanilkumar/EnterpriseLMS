namespace LMS.Domain.Entities
{
    public sealed class QuizAttemptAnswerOption
    {
        private QuizAttemptAnswerOption() { }

        public QuizAttemptAnswerOption(Guid answerId, Guid optionId, string optionTextSnapshot)
        {
            Id = Guid.NewGuid();
            AnswerId = answerId;
            OptionId = optionId;
            OptionTextSnapshot = optionTextSnapshot;
        }

        public Guid Id { get; private set; }
        public Guid AnswerId { get; private set; }
        public Guid OptionId { get; private set; }
        public string OptionTextSnapshot { get; private set; } = null!;
    }
}
