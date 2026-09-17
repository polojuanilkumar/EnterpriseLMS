using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class UserProfile
    {
        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        public string? PhoneNumber { get; private set; }

        public string? EmployeeCode { get; private set; }

        public string? Department { get; private set; }

        public string? Designation { get; private set; }

        public string? ProfileImageUrl { get; private set; }

        public bool IsActive { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        private UserProfile()
        {
        }

        public UserProfile(Guid userId)
        {
            Id = Guid.NewGuid();
            UserId = userId;
            IsActive = true;
            CreatedAt = DateTime.UtcNow;
        }

        public void Update(
            string? phoneNumber,
            string? employeeCode,
            string? department,
            string? designation,
            string? profileImageUrl)
        {
            PhoneNumber = phoneNumber;
            EmployeeCode = employeeCode;
            Department = department;
            Designation = designation;
            ProfileImageUrl = profileImageUrl;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
