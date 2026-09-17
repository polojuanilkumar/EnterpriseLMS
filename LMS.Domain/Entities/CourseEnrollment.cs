using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class CourseEnrollment
    {
        private CourseEnrollment()
        {
        }

        public CourseEnrollment(
            Guid courseId,
            Guid userId)
        {
            Id = Guid.NewGuid();
            CourseId = courseId;
            UserId = userId;
            EnrolledAt = DateTime.UtcNow;
            Status = EnrollmentStatus.Active;
            ProgressPercentage = 0;
        }

        public Guid Id { get; private set; }

        public Guid CourseId { get; private set; }

        public Guid UserId { get; private set; }

        public DateTime EnrolledAt { get; private set; }

        public DateTime? CompletedAt { get; private set; }

        public EnrollmentStatus Status { get; private set; }

        public decimal ProgressPercentage { get; private set; }

        public void Complete()
        {
            Status = EnrollmentStatus.Completed;
            ProgressPercentage = 100;
            CompletedAt = DateTime.UtcNow;
        }

        public void Cancel()
        {
            Status = EnrollmentStatus.Cancelled;
        }

        public void UpdateProgress(decimal progressPercentage)
        {
            if (progressPercentage < 0 || progressPercentage > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(progressPercentage),
                    "Progress percentage must be between 0 and 100.");
            }

            ProgressPercentage = progressPercentage;

            if (progressPercentage == 100)
            {
                Complete();
            }
        }
    }
}
