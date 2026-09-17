using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Enrollments.GetMyEnrollments
{
    public sealed class GetMyEnrollmentsResponse
    {
        public Guid Id { get; init; }

        public Guid CourseId { get; init; }

        public DateTime EnrolledAt { get; init; }

        public DateTime? CompletedAt { get; init; }

        public EnrollmentStatus Status { get; init; }

        public decimal ProgressPercentage { get; init; }
    }
}
