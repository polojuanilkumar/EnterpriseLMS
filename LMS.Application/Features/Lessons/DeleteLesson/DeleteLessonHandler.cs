using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using LMS.Application.Features.Quizzes.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.DeleteLesson
{
    public sealed class DeleteLessonHandler
    {
        private readonly ILessonRepository _lessonRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly EnsureQuizEditable _ensureQuizEditable;

        public DeleteLessonHandler(
            ILessonRepository lessonRepository,
            IUnitOfWork unitOfWork, EnsureQuizEditable ensureQuizEditable)
        {
            _lessonRepository = lessonRepository;
            _unitOfWork = unitOfWork;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public Task HandleAsync(Guid id, CancellationToken cancellationToken = default)
            => _ensureQuizEditable.ExecuteParentDeletionAsync(id, false,
                token => HandleCoreAsync(id, token), cancellationToken);

        private async Task HandleCoreAsync(
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
