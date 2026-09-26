using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.DeleteQuiz
{
    public sealed class DeleteQuizHandler
    {
        private readonly EnsureQuizOwnership _ownership;
        private readonly IQuizRepository _quizRepository;
        private readonly EnsureQuizEditable _ensureQuizEditable;
        public DeleteQuizHandler(
            IQuizRepository quizRepository, EnsureQuizEditable ensureQuizEditable,
            EnsureQuizOwnership ownership)
        {
            _ownership = ownership;
            _quizRepository = quizRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public Task HandleAsync(
            Guid lessonId,
            Guid currentUserId,
            bool isAdmin,
            bool isInstructor,
            CancellationToken cancellationToken = default)
            => _ensureQuizEditable.ExecuteForLessonAsync(lessonId, token => HandleCoreAsync(lessonId, currentUserId, isAdmin, isInstructor, token), cancellationToken);

        private async Task HandleCoreAsync(
            Guid lessonId,
            Guid currentUserId,
            bool isAdmin,
            bool isInstructor,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByLessonIdAsync(
                lessonId,
                cancellationToken);

            if (quiz is null)
            {
                throw new KeyNotFoundException(
                    "Quiz not found.");
            }
            await _ownership.CheckLessonAsync(quiz.LessonId, currentUserId, isAdmin, isInstructor, cancellationToken);

            await _ensureQuizEditable.CheckAsync(quiz.Id, cancellationToken);
            _quizRepository.Remove(quiz);

            await _quizRepository.SaveChangesAsync(  
                cancellationToken);
        }
    }
}
