using LMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Infrastructure.Persistence.Configurations
{
    public sealed class QuizAttemptAnswerConfiguration : IEntityTypeConfiguration<QuizAttemptAnswer>
    {
        public void Configure(EntityTypeBuilder<QuizAttemptAnswer> builder)
        {
            builder.ToTable("QuizAttemptAnswers");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.QuestionTextSnapshot).HasMaxLength(2000).IsRequired();
            builder.Property(x => x.QuestionTypeSnapshot).HasConversion<int>();
            builder.Property(x => x.MarksSnapshot).HasPrecision(5, 2);
            builder.Property(x => x.AwardedMarks).HasPrecision(5, 2);
            builder.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
            builder.HasOne<QuizAttempt>().WithMany().HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.HasOne<QuizQuestion>().WithMany().HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
