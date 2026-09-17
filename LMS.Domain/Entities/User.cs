using LMS.Domain.Common;
using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public class User : BaseEntity
    {
        public string FirstName { get; private set; } = string.Empty;

        public string LastName { get; private set; } = string.Empty;

        public string Email { get; private set; } = string.Empty;

        public string PasswordHash { get; private set; } = string.Empty;

        public UserRole Role { get; private set; }

        public UserStatus Status { get; private set; }

        public DateTime? LastLoginOn { get; private set; }

        private User()
        {
        }

        public User(
            string firstName,
            string lastName,
            string email,
            string passwordHash,
            UserRole role)
        {
            if (string.IsNullOrWhiteSpace(firstName))
                throw new ArgumentException("First name is required.");

            if (string.IsNullOrWhiteSpace(lastName))
                throw new ArgumentException("Last name is required.");

            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email is required.");

            if (string.IsNullOrWhiteSpace(passwordHash))
                throw new ArgumentException("Password hash is required.");

            FirstName = firstName.Trim();

            LastName = lastName.Trim();

            Email = email.Trim().ToLowerInvariant();

            PasswordHash = passwordHash;

            Role = role;

            Status = UserStatus.Active;
        }

        public void UpdateProfile(
            string firstName,
            string lastName)
        {
            if (string.IsNullOrWhiteSpace(firstName))
                throw new ArgumentException("First name is required.");

            if (string.IsNullOrWhiteSpace(lastName))
                throw new ArgumentException("Last name is required.");

            FirstName = firstName.Trim();

            LastName = lastName.Trim();

            SetModified();
        }

        public void UpdatePassword(string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(passwordHash))
                throw new ArgumentException("Password hash is required.");

            PasswordHash = passwordHash;

            SetModified();
        }

        public void RecordLogin()
        {
            LastLoginOn = DateTime.UtcNow;

            SetModified();
        }

        public void ChangeStatus(UserStatus status)
        {
            Status = status;

            SetModified();
        }
    }
}
