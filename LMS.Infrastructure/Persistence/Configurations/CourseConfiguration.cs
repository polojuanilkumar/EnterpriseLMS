using LMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Persistence.Configurations
{
    public sealed class CourseConfiguration
        : IEntityTypeConfiguration<Course>
    {
        public void Configure(
            EntityTypeBuilder<Course> builder)
        {
            builder.ToTable("Courses");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.Code)
                .HasMaxLength(50)
                .IsRequired();

            builder.HasIndex(x => x.Code)
                .IsUnique();

            builder.Property(x => x.Description)
                .HasMaxLength(5000)
                .IsRequired();

            builder.Property(x => x.ThumbnailUrl)
                .HasMaxLength(500);

            builder.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.Level)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.DurationInMinutes)
                .IsRequired();

            builder.Property(x => x.IsPublished)
                .IsRequired();

            builder.Property(x => x.InstructorId)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.HasIndex(x => x.InstructorId);
        }
    }
}
