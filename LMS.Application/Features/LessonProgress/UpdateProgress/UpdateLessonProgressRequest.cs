using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.LessonProgress.UpdateProgress
{
    public sealed class UpdateLessonProgressRequest
    {
        public decimal ProgressPercentage { get; init; }
    }
}
