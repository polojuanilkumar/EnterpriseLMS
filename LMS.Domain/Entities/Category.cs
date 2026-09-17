using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class Category
    {
        private Category() { }

        public Category(
            string name,
            string description)
        {
            Id = Guid.NewGuid();

            Name = name;
            Description = description;

            IsActive = true;
            CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }

        public string Name { get; private set; } = string.Empty;

        public string Description { get; private set; } = string.Empty;

        public bool IsActive { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        public void Update(
            string name,
            string description)
        {
            Name = name;
            Description = description;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Activate()
        {
            IsActive = true;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Deactivate()
        {
            IsActive = false;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
