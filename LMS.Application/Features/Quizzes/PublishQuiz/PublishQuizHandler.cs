using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.Quizzes;
using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.PublishQuiz
{
    public sealed class PublishQuizHandler
    {
        private readonly IQuizRepository _quizRepository;
        private readonly IQuizQuestionRepository _questionRepository;
        private readonly IQuizOptionRepository _optionRepository;
        private readonly EnsureQuizEditable _ensureQuizEditable;

        public PublishQuizHandler(
            IQuizRepository quizRepository,
            IQuizQuestionRepository questionRepository,
            IQuizOptionRepository optionRepository,
            EnsureQuizEditable ensureQuizEditable)
        {
            _quizRepository = quizRepository;
            _questionRepository = questionRepository;
            _optionRepository = optionRepository;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public Task HandleAsync(
            Guid lessonId,
            CancellationToken cancellationToken = default)
            => _ensureQuizEditable.ExecuteForLessonAsync(lessonId,
                token => HandleCoreAsync(lessonId, token), cancellationToken);

        private async Task HandleCoreAsync(
            Guid lessonId,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByLessonIdAsync(
                lessonId, cancellationToken);

            if (quiz is null)
                throw new KeyNotFoundException("Quiz not found.");

            var questions = await _questionRepository.GetByQuizIdAsync(
                quiz.Id, cancellationToken);

            var error = QuizConfigurationValidation.GetQuestionsError(questions);
            if (error is not null)
                throw new InvalidOperationException(error);

            foreach (var question in questions)
            {
                var options = await _optionRepository.GetByQuestionIdAsync(
                    question.Id, cancellationToken);
                error = QuizConfigurationValidation.GetQuestionError(question, options);
                if (error is not null)
                    throw new InvalidOperationException(error);
            }

            quiz.Publish();
            await _quizRepository.SaveChangesAsync(cancellationToken);
        }
    }
}
