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

            if (questions.Count == 0)
                throw new InvalidOperationException(
                    "Add at least one question before publishing the quiz.");

            foreach (var question in questions)
            {
                var options = await _optionRepository.GetByQuestionIdAsync(
                    question.Id, cancellationToken);

                if (options.Count < 2)
                    throw new InvalidOperationException(
                        $"Question {question.DisplayOrder} needs at least two options.");

                var correctCount = options.Count(x => x.IsCorrect);

                switch (question.QuestionType)
                {
                    case QuestionType.SingleChoice:
                        if (correctCount != 1)
                            throw new InvalidOperationException(
                                $"Question {question.DisplayOrder} must have exactly one correct option.");
                        break;

                    case QuestionType.MultipleChoice:
                        if (correctCount < 2)
                            throw new InvalidOperationException(
                                $"Question {question.DisplayOrder} needs at least two correct options.");
                        break;

                    case QuestionType.TrueFalse:
                        if (options.Count != 2 || correctCount != 1)
                            throw new InvalidOperationException(
                                $"True/False question {question.DisplayOrder} needs exactly two options and one correct option.");
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Question {question.DisplayOrder} has an unsupported question type.");
                }
            }

            quiz.Publish();
            await _quizRepository.SaveChangesAsync(cancellationToken);
        }
    }
}
