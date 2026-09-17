using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.UnpublishLesson
{
    public sealed class UnpublishLessonHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UnpublishLessonHandler(
            ILessonRepository lessonRepository,
            IUnitOfWork unitOfWork)
        {
            _lessonRepository = lessonRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task HandleAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var lesson =
                await _lessonRepository.GetByIdAsync(
                    id,
                    cancellationToken);

            if (lesson is null)
            {
                throw new KeyNotFoundException(
                    "Lesson not found.");
            }

            lesson.Unpublish();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
