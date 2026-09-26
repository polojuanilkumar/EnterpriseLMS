using LMS.Application.Features.Quizzes.Common;
﻿using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.UnpublishQuiz
{
    public sealed class UnpublishQuizHandler
    {
        private readonly EnsureQuizOwnership _ownership;
        private readonly IQuizRepository _quizRepository;

        public UnpublishQuizHandler(
            IQuizRepository quizRepository,
            EnsureQuizOwnership ownership)
        {
            _ownership = ownership;
            _quizRepository = quizRepository;
        }

        public async Task HandleAsync(
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

            quiz.Unpublish();

            await _quizRepository.SaveChangesAsync(
                cancellationToken);
        }
    }
}
