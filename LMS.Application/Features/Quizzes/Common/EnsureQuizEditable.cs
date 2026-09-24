using LMS.Application.Interfaces.Quizzes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.Common
{
    public sealed class EnsureQuizEditable
    {
        private readonly IQuizRepository _quizRepository;

        public EnsureQuizEditable(IQuizRepository quizRepository)
        {
            _quizRepository = quizRepository;
        }

        public async Task CheckAsync(
            Guid quizId,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByIdAsync(
                quizId, cancellationToken);

            if (quiz is null)
                throw new KeyNotFoundException("Quiz not found.");

            if (quiz.IsPublished)
                throw new InvalidOperationException(
                   "Unpublish the quiz before editing or deleting it or changing its questions or options.");
        }
    }
}
