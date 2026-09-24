using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.QuizOptions.UpdateOption
{
    public sealed class UpdateOptionRequest
    {
        public string OptionText { get; init; } = null!;
        public bool IsCorrect { get; init; }
        public int DisplayOrder { get; init; }
    }
}
