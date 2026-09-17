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

        public PublishLessonHandler(
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

            lesson.Publish();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
