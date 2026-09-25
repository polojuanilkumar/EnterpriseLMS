using LMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Infrastructure.Persistence.Configurations
{
    public sealed class QuizAttemptAnswerOptionConfiguration : IEntityTypeConfiguration<QuizAttemptAnswerOption>
    {
        public void Configure(EntityTypeBuilder<QuizAttemptAnswerOption> builder)
        {
            builder.ToTable("QuizAttemptAnswerOptions");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.OptionTextSnapshot).HasMaxLength(1000).IsRequired();
            builder.HasIndex(x => new { x.AnswerId, x.OptionId }).IsUnique();
            builder.HasOne<QuizAttemptAnswer>().WithMany().HasForeignKey(x => x.AnswerId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.HasOne<QuizOption>().WithMany().HasForeignKey(x => x.OptionId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
