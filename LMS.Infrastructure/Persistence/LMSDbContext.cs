using LMS.Domain.Entities;
using LMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Persistence
{
    public class LMSDbContext
       : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {
        public LMSDbContext(
            DbContextOptions<LMSDbContext> options)
            : base(options)
        {
        }

        // UserProfile table
        public DbSet<UserProfile> UserProfiles
        {
            get;
            set;
        }

        public DbSet<Course> Courses => Set<Course>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<CourseEnrollment> CourseEnrollments { get; set; }
        public DbSet<CourseSection> CourseSections { get; set; }

        public DbSet<Lesson> Lessons { get; set; }

        public DbSet<LessonProgress> LessonProgresses { get; set; }

        public DbSet<Quiz> Quizzes { get; set; }

        public DbSet<QuizQuestion> QuizQuestions { get; set; }

        public DbSet<QuizOption> QuizOptions { get; set; }

        public DbSet<QuizAttempt> QuizAttempts { get; set; }

        public DbSet<QuizAttemptAnswer> QuizAttemptAnswers { get; set; }

        public DbSet<QuizAttemptAnswerOption> QuizAttemptAnswerOptions { get; set; }
        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.ToTable("UserProfiles");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.PhoneNumber)
                    .HasMaxLength(20);

                entity.Property(x => x.EmployeeCode)
                    .HasMaxLength(50);

                entity.Property(x => x.Department)
                    .HasMaxLength(100);

                entity.Property(x => x.Designation)
                    .HasMaxLength(100);

                entity.Property(x => x.ProfileImageUrl)
                    .HasMaxLength(500);

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.HasIndex(x => x.UserId)
                    .IsUnique();
            });

            modelBuilder.Entity<CourseSection>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Title)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Description)
                    .HasMaxLength(1000);

                entity.Property(x => x.DisplayOrder)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.HasOne<Course>()
                    .WithMany()
                    .HasForeignKey(x => x.CourseId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.CourseId,
                    x.DisplayOrder
                });
            });
            modelBuilder.Entity<Lesson>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Title)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Description)
                    .HasMaxLength(1000);

                entity.Property(x => x.LessonType)
                    .IsRequired();

                entity.Property(x => x.Content)
                    .HasColumnType("nvarchar(max)");

                entity.Property(x => x.VideoUrl)
                    .HasMaxLength(1000);

                entity.Property(x => x.DurationInMinutes)
                    .IsRequired();

                entity.Property(x => x.DisplayOrder)
                    .IsRequired();

                entity.Property(x => x.IsPublished)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.HasOne<CourseSection>()
                    .WithMany()
                    .HasForeignKey(x => x.SectionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.SectionId,
                    x.DisplayOrder
                });
            });

            modelBuilder.Entity<LessonProgress>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.UserId)
                    .IsRequired();

                entity.Property(x => x.LessonId)
                    .IsRequired();

                entity.Property(x => x.IsCompleted)
                    .IsRequired();

                entity.Property(x => x.CompletedAt);

                entity.Property(x => x.StartedAt)
                    .IsRequired();

                entity.Property(x => x.ProgressPercentage)
                    .HasPrecision(5, 2)
                    .IsRequired();

                entity.Property(x => x.LastAccessedAt)
                    .IsRequired();

                entity.HasOne<Lesson>()
                    .WithMany()
                    .HasForeignKey(x => x.LessonId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.UserId,
                    x.LessonId
                })
                .IsUnique();
            });

            modelBuilder.Entity<Quiz>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.LessonId)
                    .IsRequired();

                entity.Property(x => x.Title)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Description)
                    .HasMaxLength(1000);

                entity.Property(x => x.PassingPercentage)
                    .HasPrecision(5, 2)
                    .IsRequired();

                entity.Property(x => x.TimeLimitInMinutes)
                    .IsRequired();

                entity.Property(x => x.MaxAttempts)
                    .IsRequired();

                entity.Property(x => x.IsPublished)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.Property(x => x.UpdatedAt);

                entity.HasOne<Lesson>()
                    .WithMany()
                    .HasForeignKey(x => x.LessonId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => x.LessonId)
                    .IsUnique();
            });

            modelBuilder.Entity<QuizQuestion>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.QuizId)
                    .IsRequired();

                entity.Property(x => x.QuestionText)
                    .IsRequired()
                    .HasMaxLength(2000);

                entity.Property(x => x.QuestionType)
                    .IsRequired();

                entity.Property(x => x.DisplayOrder)
                    .IsRequired();

                entity.Property(x => x.Marks)
                    .HasPrecision(5, 2)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.Property(x => x.UpdatedAt);

                entity.HasOne<Quiz>()
                    .WithMany()
                    .HasForeignKey(x => x.QuizId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.QuizId,
                    x.DisplayOrder
                })
                .IsUnique();
            });



            modelBuilder.Entity<QuizOption>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.QuestionId)
                    .IsRequired();

                entity.Property(x => x.OptionText)
                    .IsRequired()
                    .HasMaxLength(1000);

                entity.Property(x => x.IsCorrect)
                    .IsRequired();

                entity.Property(x => x.DisplayOrder)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.Property(x => x.UpdatedAt);

                entity.HasOne<QuizQuestion>()
                    .WithMany()
                    .HasForeignKey(x => x.QuestionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.QuestionId,
                    x.DisplayOrder
                })
                .IsUnique();
            });

            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(LMSDbContext).Assembly);
        }

    }
}
