using FluentValidation;
using LMS.Application.DTOs.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Validators.Users
{
    public sealed class RegisterUserValidator
      : AbstractValidator<RegisterUserRequest>
    {
        public RegisterUserValidator()
        {
            RuleFor(x => x.FirstName)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.LastName)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(255);

            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(8)
                .MaximumLength(100);
        }
    }
}
