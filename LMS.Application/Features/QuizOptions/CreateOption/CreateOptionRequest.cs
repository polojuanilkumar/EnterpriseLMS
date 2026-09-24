using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizOptions.CreateOption
{
    public sealed class CreateOptionRequest
    {
        public string OptionText { get; init; } = null!;

        public bool IsCorrect { get; init; }

        public int DisplayOrder { get; init; }
    }
}
