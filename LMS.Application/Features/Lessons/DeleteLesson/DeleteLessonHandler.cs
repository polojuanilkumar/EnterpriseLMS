using LMS.Application.Features.Lessons.Common;
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
        private readonly EnsureLessonOwnership _ownership;
        private readonly EnsureQuizEditable _ensureQuizEditable;

        public DeleteLessonHandler(
            ILessonRepository lessonRepository,
            EnsureLessonOwnership ownership,
            IUnitOfWork unitOfWork, EnsureQuizEditable ensureQuizEditable)
        {
            _lessonRepository = lessonRepository;
            _unitOfWork = unitOfWork;
            _ownership = ownership;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public async Task HandleAsync(Guid id, Guid currentUserId, bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            // Check access before quiz-attempt guards can return a conflict.
            var lesson = await _lessonRepository.GetByIdAsync(id, cancellationToken);
            if (lesson is null || lesson.Id != id)
                throw new KeyNotFoundException("Lesson not found.");
            await _ownership.CheckSectionAsync(lesson.SectionId, currentUserId, isAdmin, cancellationToken);

            await _ensureQuizEditable.ExecuteParentDeletionAsync(id, false,
                token => HandleCoreAsync(id, currentUserId, isAdmin, token), cancellationToken);
        }

        private async Task HandleCoreAsync(
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

            _lessonRepository.Remove(lesson);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
