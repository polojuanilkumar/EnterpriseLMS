using LMS.Application.Features.Lessons.Common;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.PublishLesson
{
    public sealed class PublishLessonHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly EnsureLessonOwnership _ownership;

        public PublishLessonHandler(
            ILessonRepository lessonRepository,
            EnsureLessonOwnership ownership,
            IUnitOfWork unitOfWork)
        {
            _lessonRepository = lessonRepository;
            _unitOfWork = unitOfWork;
            _ownership = ownership;
        }

        public async Task HandleAsync(
            Guid id,
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

            lesson.Publish();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
