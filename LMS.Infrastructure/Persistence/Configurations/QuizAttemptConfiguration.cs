using LMS.Domain.Entities;
using LMS.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Infrastructure.Persistence.Configurations
{
    public sealed class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
    {
        public void Configure(EntityTypeBuilder<QuizAttempt> builder)
        {
            builder.ToTable("QuizAttempts");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Status).HasConversion<int>();
            builder.Property(x => x.EarnedMarks).HasPrecision(18, 2);
            builder.Property(x => x.PossibleMarks).HasPrecision(18, 2);
            builder.Property(x => x.PassingPercentageSnapshot).HasPrecision(5, 2);
            builder.HasIndex(x => new { x.QuizId, x.UserId, x.AttemptNumber }).IsUnique();
            // Started = 1. An overdue attempt must be saved as Expired before
            // another Started attempt can be inserted for this student and quiz.
            builder.HasIndex(x => new { x.QuizId, x.UserId })
                .IsUnique()
                .HasFilter("[Status] = 1");
            builder.HasIndex(x => x.UserId);
            builder.HasOne<Quiz>().WithMany().HasForeignKey(x => x.QuizId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
