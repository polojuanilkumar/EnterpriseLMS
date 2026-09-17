using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class Course
    {
        private Course()
        {
        }

        public Course(
            string title,
            string code,
            string description,
            Guid instructorId,
            CourseLevel level)
        {
            Id = Guid.NewGuid();

            Title = title;
            Code = code;
            Description = description;
            InstructorId = instructorId;
            Level = level;

            Status = CourseStatus.Draft;

            IsPublished = false;

            CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }

        public string Title { get; private set; } = string.Empty;

        public string Code { get; private set; } = string.Empty;

        public string Description { get; private set; } = string.Empty;

        public Guid? CategoryId { get; private set; }

        public Category? Category { get; private set; }

        public Guid InstructorId { get; private set; }

        public string? ThumbnailUrl { get; private set; }

        public CourseStatus Status { get; private set; }

        public CourseLevel Level { get; private set; }

        public int DurationInMinutes { get; private set; }

        public bool IsPublished { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        public void Update(
            string title,
            string code,
            string description,
            CourseLevel level,
            int durationInMinutes,
            string? thumbnailUrl)
        {
            Title = title;
            Code = code;
            Description = description;
            Level = level;
            DurationInMinutes = durationInMinutes;
            ThumbnailUrl = thumbnailUrl;

            UpdatedAt = DateTime.UtcNow;
        }

        public void SetCategory(Guid? categoryId)
        {
            CategoryId = categoryId;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Publish()
        {
            Status = CourseStatus.Published;
            IsPublished = true;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Archive()
        {
            Status = CourseStatus.Archived;
            IsPublished = false;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
