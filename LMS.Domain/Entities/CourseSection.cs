using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class CourseSection
    {
        private CourseSection()
        {
        }

        public CourseSection(
            Guid courseId,
            string title,
            string? description,
            int displayOrder)
        {
            Id = Guid.NewGuid();
            CourseId = courseId;
            Title = title;
            Description = description;
            DisplayOrder = displayOrder;
            CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }

        public Guid CourseId { get; private set; }

        public string Title { get; private set; } = null!;

        public string? Description { get; private set; }

        public int DisplayOrder { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        public void Update(
            string title,
            string? description,
            int displayOrder)
        {
            Title = title;
            Description = description;
            DisplayOrder = displayOrder;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
