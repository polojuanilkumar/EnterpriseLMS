using System;
using System.Collections.Generic;
using System.Text;
using LMS.Domain.Enums;

namespace LMS.Domain.Entities
{
    public sealed class Lesson
    {
        private Lesson()
        {
        }

        public Lesson(
            Guid sectionId,
            string title,
            string? description,
            LessonType lessonType,
            string? content,
            string? videoUrl,
            int durationInMinutes,
            int displayOrder)
        {
            Id = Guid.NewGuid();
            SectionId = sectionId;
            Title = title;
            Description = description;
            LessonType = lessonType;
            Content = content;
            VideoUrl = videoUrl;
            DurationInMinutes = durationInMinutes;
            DisplayOrder = displayOrder;
            IsPublished = false;
            CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }

        public Guid SectionId { get; private set; }

        public string Title { get; private set; } = null!;

        public string? Description { get; private set; }

        public LessonType LessonType { get; private set; }

        public string? Content { get; private set; }

        public string? VideoUrl { get; private set; }

        public int DurationInMinutes { get; private set; }

        public int DisplayOrder { get; private set; }

        public bool IsPublished { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        public void Update(
            string title,
            string? description,
            LessonType lessonType,
            string? content,
            string? videoUrl,
            int durationInMinutes,
            int displayOrder)
        {
            Title = title;
            Description = description;
            LessonType = lessonType;
            Content = content;
            VideoUrl = videoUrl;
            DurationInMinutes = durationInMinutes;
            DisplayOrder = displayOrder;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Publish()
        {
            IsPublished = true;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Unpublish()
        {
            IsPublished = false;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
