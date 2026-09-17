using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.DeleteLesson
{
    public sealed class DeleteLessonHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DeleteLessonHandler(
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

            _lessonRepository.Remove(lesson);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
