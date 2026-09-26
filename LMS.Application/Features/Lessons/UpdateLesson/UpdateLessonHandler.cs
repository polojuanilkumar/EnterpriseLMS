using LMS.Application.Features.Lessons.Common;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.UpdateLesson
{
    public sealed class UpdateLessonHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly EnsureLessonOwnership _ownership;

        public UpdateLessonHandler(
            ILessonRepository lessonRepository,
            EnsureLessonOwnership ownership,
            IUnitOfWork unitOfWork)
        {
            _lessonRepository = lessonRepository;
            _unitOfWork = unitOfWork;
            _ownership = ownership;
        }

        public async Task<LessonResponse> HandleAsync(
            Guid id,
            UpdateLessonRequest request,
            Guid currentUserId,
            bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            var lesson =
                await _lessonRepository.GetByIdAsync(
                    id,
                    cancellationToken);

            if (lesson is null || lesson.Id != id)
            {
                throw new KeyNotFoundException(
                    "Lesson not found.");
            }

            await _ownership.CheckSectionAsync(lesson.SectionId, currentUserId, isAdmin, cancellationToken);

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new ArgumentException(
                    "Lesson title is required.");
            }

            if (request.DisplayOrder < 1)
            {
                throw new ArgumentException(
                    "Display order must be greater than 0.");
            }

            if (request.DurationInMinutes < 0)
            {
                throw new ArgumentException(
                    "Duration cannot be negative.");
            }

            if (!Enum.IsDefined(
                    typeof(LMS.Domain.Enums.LessonType),
                    request.LessonType))
            {
                throw new ArgumentException(
                    "Invalid lesson type.");
            }

            lesson.Update(
                request.Title.Trim(),
                request.Description?.Trim(),
                request.LessonType,
                request.Content?.Trim(),
                request.VideoUrl?.Trim(),
                request.DurationInMinutes,
                request.DisplayOrder);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);

            return new LessonResponse
            {
                Id = lesson.Id,
                SectionId = lesson.SectionId,
                Title = lesson.Title,
                Description = lesson.Description,
                LessonType = lesson.LessonType.ToString(),
                Content = lesson.Content,
                VideoUrl = lesson.VideoUrl,
                DurationInMinutes = lesson.DurationInMinutes,
                DisplayOrder = lesson.DisplayOrder,
                IsPublished = lesson.IsPublished,
                CreatedAt = lesson.CreatedAt,
                UpdatedAt = lesson.UpdatedAt
            };
        }
    }
}
