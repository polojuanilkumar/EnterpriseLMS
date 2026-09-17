using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Users.Profile
{
    public sealed class UpdateUserProfileValidator
        : AbstractValidator<UpdateUserProfileRequest>
    {
        public UpdateUserProfileValidator()
        {
            RuleFor(x => x.PhoneNumber)
                .MaximumLength(20)
                .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

            RuleFor(x => x.EmployeeCode)
                .MaximumLength(50)
                .When(x => !string.IsNullOrWhiteSpace(x.EmployeeCode));

            RuleFor(x => x.Department)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.Department));

            RuleFor(x => x.Designation)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.Designation));

            RuleFor(x => x.ProfileImageUrl)
                .MaximumLength(500)
                .When(x => !string.IsNullOrWhiteSpace(x.ProfileImageUrl));
        }
    }
}
