# Quiz authoring and learner attempts

The Angular staff quiz/assignment authoring and grading extension is documented in [staff-assessments.md](staff-assessments.md). This earlier record remains as implementation history.

The Angular Student quiz and BFF extension is documented in [student-quiz.md](student-quiz.md). This earlier MVC batch record remains as policy and implementation history.

Implemented in the current branch and working tree. Existing changes and the existing
LMS.Tests project were preserved. No staging, commit, push, branch switch, dependency
restore, database connection, migration generation/application, browser testing, or
Swagger testing was performed.

## Exact routes

The following prefixes include GUID route constraints on all resource IDs:

- Staff prefix `S = /staff/{organizationId}/courses/{courseId}/sections/{sectionId}/lessons/{lessonId}/quiz`
- Learner prefix `L = /learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}/quiz`

| Web method and route | Behavior |
| --- | --- |
| `GET S` | Quiz settings and question/option management |
| `POST S` | Create or update settings; `Form.*` fields |
| `POST S/change` | `actionName=question` with `question.*`; optional `questionId` for edit |
| `POST S/change` | `actionName=option` with `option.*`, `questionId`; optional `optionId` for edit |
| `POST S/change` | `actionName=delete`, `questionId`, optional `optionId`; requires `confirmed=true` |
| `POST S/change` | `actionName=publish` or `unpublish`; requires `confirmed=true` |
| `GET L` | Instructions, eligibility display, attempt counts/history |
| `POST L/start` | Resume a current attempt from history or call the existing start API |
| `GET L/attempts/{attemptId}` | Resume questions or show submitted result/review or expired status |
| `POST L/attempts/{attemptId}/submit` | `Answers[i].QuestionId`, `Answers[i].SelectedOptionIds` |

The existing learner lesson route
`GET /learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}`
also displays quiz instructions, history, marks/pass summaries and links to attempts.
Existing course/section/lesson navigation and media presentation remain in place.
The staff lesson page links to quiz management within that same parent hierarchy.

| Existing API method and route | Used for |
| --- | --- |
| `GET /{lessonId}/quiz` | Quiz lookup; the existing route is at the root, **not** `/api/Lessons/...` |
| `POST /{lessonId}/quiz` | Create quiz |
| `PUT /{lessonId}/quiz` | Update quiz |
| `PATCH /{lessonId}/quiz/publish` | Publication and existing configuration validation |
| `PATCH /{lessonId}/quiz/unpublish` | Unpublish |
| `GET, POST /api/Quizzes/{quizId}/questions` | Read/create questions |
| `PUT, DELETE /api/Quizzes/{quizId}/questions/{questionId}` | Edit/delete question |
| `GET, POST /api/Quizzes/{quizId}/questions/{questionId}/options` | Staff option read/create |
| `PUT, DELETE /api/Quizzes/{quizId}/questions/{questionId}/options/{optionId}` | Edit/delete option |
| `GET /api/Quizzes/{quizId}/questions/{questionId}/options/student` | Learner-safe options |
| `GET /api/Quizzes/{quizId}/attempts` | Owner's history and server deadlines |
| `POST /api/Quizzes/{quizId}/attempts/start` | Start an attempt |
| `POST /api/Quizzes/{quizId}/attempts/{attemptId}/submit` | Authoritative status/deadline checks and scoring |
| `GET /api/Quizzes/{quizId}/attempts/{attemptId}/result` | Owner's submitted result |
| `GET /api/Quizzes/{quizId}/attempts/{attemptId}/answers` | Owner's selected-answer snapshots |

Also reused: organization context, course/section/lesson reads, course progress gate,
enrollment reads and content-item reads. The web adapter uses the existing JSON envelope
and field contracts; web forms and learner option models contain only their needed fields.

## API gaps and policy

No CRUD/start/submit/result operation was missing. No duplicate API was introduced.
An authorization gap was found in the existing staff option-read action: it had role and
organization gating and question/quiz matching, but lacked assigned-course ownership
validation. `QuizQuestionsController.GetOptions` now calls the existing
`EnsureQuizOwnership` before reading staff options. Its route and response contract are
unchanged. The existing exception middleware handles authorization failures.

Every web request requires the selected organization in the protected server session.
Staff requests revalidate organization context, assigned course ownership and the complete
course → section → lesson → quiz → question → option hierarchy before modifications.
The API independently uses verified organization scope, filtered repositories, existing
ownership/editability handlers, and stored parent checks. SuperAdmin must select an
organization through the existing selection flow; a platform role alone does not select it.

All modifying forms carry antiforgery tokens and both new controllers require validation.
Author text and validation messages are Razor-encoded. Only bounded 400/409 validation
messages reach forms; authorization and service diagnostics use existing sanitized error
handling. JWTs remain in the existing protected server session. Answers and tokens are
absent from URLs, storage, recovery notices and logs added by this batch.

Learner options come exclusively from `/options/student`; their view model has no
correctness property. Results use only the attempt-owner API and selected snapshots;
no answer keys or staff option DTOs are supplied to learner views. Completed enrollment
retains historical reads. Start/submit still require Active enrollment in the existing API.
Publication gates, marks, exact-set scoring, maximum attempts, edit locks after attempts,
and course-completion policy are unchanged. Quiz passing is not required for completion.

Resume uses history without restarting the attempt. The displayed timer uses the server
deadline; it does not submit or expire anything itself. Expired history rows do not call
submitted-only result/review endpoints. Duplicate clicks are blocked in JavaScript;
duplicate/repeated POSTs are still decided by the API. Network/time-out/502/invalid start
response failures produce a fixed recovery notice and redirect to GET. No answer payload
is stored in TempData, automatically resent, or automatically restarted. The recovery
page asks the learner to refresh history/check the result before any deliberate retry.

## Files changed by this batch

| Files | Purpose |
| --- | --- |
| `LMS.Api/Controllers/QuizQuestionsController.cs` | Existing staff option-read ownership check |
| `LMS.Web/Controllers/QuizAuthoringController.cs` | Authoring forms, validation and confirmation |
| `LMS.Web/Controllers/QuizLearningController.cs` | Start/resume/submit/results and recovery redirects |
| `LMS.Web/Controllers/LearnController.cs` | Lesson-page quiz integration |
| `LMS.Web/Models/QuizModels.cs` | Forms, safe learner options, authoring and attempt page models |
| `LMS.Web/Models/LearnerModels.cs` | Optional lesson-page quiz summary |
| `LMS.Web/Services/QuizApi.cs` | Existing API integration and nested relationship validation |
| `LMS.Web/Views/QuizAuthoring/Index.cshtml` | Settings, status, publication and question/option management |
| `LMS.Web/Views/QuizAuthoring/_Question.cshtml` | Question editor and confirmed deletion |
| `LMS.Web/Views/QuizAuthoring/_Option.cshtml` | Option editor and confirmed deletion |
| `LMS.Web/Views/QuizLearning/Index.cshtml` | Attempt selections, timer, result and review |
| `LMS.Web/Views/QuizLearning/_Overview.cshtml` | Shared instructions/history/result summaries |
| `LMS.Web/Views/Staff/Lesson.cshtml` | Authoring entry link |
| `LMS.Web/Views/Learn/Unit.cshtml` | Embedded quiz overview and attempt entry link |
| `LMS.Web/wwwroot/js/quiz.js` | Duplicate-click guard and deadline display |
| `LMS.Web/wwwroot/css/site.css` | Scoped learner radio/fieldset presentation |
| `LMS.Tests/QuizWebTests.cs` | Web adapter, controller, hierarchy, confirmation, rendering and failure tests |
| `LMS.Tests/QuizAttemptUiPolicyTests.cs` | Real attempt/publication/read handlers with repository mocks |
| `LMS.Tests/quiz-ui.test.cjs` | Dependency-free Node tests of the actual UI script |
| `docs/quiz-ui-batch.md` | Routes, evidence, limitations and consolidated later checklist |

No schema change was necessary; no migration was generated.

## Automated verification

Commands use cached assets and a separate output directory to avoid locked running DLLs:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/quiz/bin/'
dotnet build EnterpriseLMS.slnx --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/quiz/bin/'
node --test LMS.Tests/quiz-ui.test.cjs
Get-ChildItem -LiteralPath LMS.Web/wwwroot/js -Filter '*.js' | ForEach-Object { node --check $_.FullName }
node --check LMS.Tests/quiz-ui.test.cjs
git diff --check
```

Final automated results:

| Check | Result |
| --- | --- |
| Existing LMS.Tests project, full .NET suite | **560 passed, 0 failed, 0 skipped** |
| Full solution build with `--no-restore` and separate `OutputPath` | **Succeeded; 0 warnings, 0 errors** |
| Node behavior test of duplicate-click/timer behavior | **1 passed, 0 failed** |
| Syntax checks for all four web JavaScript files and the Node test | **Passed** |
| `git diff --check` | **Passed**; existing Windows line-ending notices only |
| Additional `git diff --no-index --check` for new/untracked batch files | **No whitespace errors** |

New tests use HTTP handler fakes,
direct controller calls, compiled Razor rendering, the actual JavaScript in a mocked DOM,
and real domain/application handlers with repository mocks. Existing tests verify real
antiforgery token validation and missing-token rejection, secure session registration,
stored tenant/role policies, EF filters and offline SQL generation. No database is opened.
These tests do not establish browser rendering or real SQL concurrency/rollback behavior.

The initial `UseArtifactsOutput` build attempt redirected the NuGet assets path and failed
with NETSDK1004. It was corrected to `OutputPath`, retaining the existing cached `obj`
assets, without restoring or downloading anything.

## Remaining limitations and unverified behavior

- Answers are submitted together. There is no existing answer autosave API, so navigating
  away or resuming restores the attempt/deadline but not unsubmitted selections.
- Browser clock skew can affect the displayed countdown; the API's server clock remains
  authoritative. History may show Started after the deadline until an API mutation records
  expiration, which the UI labels accordingly.
- Existing publication/read gates still govern course/lesson/quiz discovery. Unpublished
  quizzes display as unavailable; this batch does not expose unpublished content to students.
  Owner-only snapshot APIs retain their existing historical disclosure rules.
- Result/review is available only for Submitted attempts and includes selected option
  snapshots without correctness keys, matching the existing API.
- Native browser layout, keyboard interaction, redirects/cookies across deployment, actual
  API transport, database transactions, races and migration behavior have not been manually
  exercised. No manual browser or Swagger tests were run.

## Consolidated manual checklist for later execution (not executed)

Use a separately authorized test environment and accounts in two organizations.

1. Navigate Course → Section → Lesson as an assigned Instructor and organization Admin.
   Create/edit quiz settings; add/edit/delete each question type and options; verify positive
   unique ordering/marks errors and retention of entered text. Verify deletion requires
   confirmation and cancelled/unconfirmed actions do not change data.
2. Publish invalid configurations (empty quiz, missing options, invalid correct-option counts).
   Confirm readable encoded errors. Fix and publish with confirmation; unpublish only with
   confirmation. Verify existing edit locks after publication/attempt creation.
3. Try unassigned Instructor, Student, foreign organization IDs and mismatched course,
   section, lesson, quiz, question and option parents. Verify denials without mutations or
   staff answer-key disclosure. Verify SuperAdmin cannot enter until explicitly selecting the
   target organization. Remove membership/expire the session and verify the existing errors.
4. Remove or forge antiforgery tokens on every modifying form; verify rejection. Enter HTML
   and quotes in quiz, question and option text; verify encoded authoring, lesson and review
   displays. Inspect responses/URLs/storage to confirm absence of JWTs and correctness keys.
5. As an Active learner, open the lesson overview, check time/attempt/passing instructions,
   start once and resume the same ID/deadline after navigation. Select radio answers for
   SingleChoice/TrueFalse and several checkboxes for MultipleChoice. Check keyboard/mobile
   presentation and clear warning that unsubmitted answers are not saved.
6. Double-click submit and replay a submitted request; verify one scored submission and the
   existing repeated-submission rejection. Submit at/after the server deadline; verify the
   existing expiration boundary and no automatic submission or new attempt from the timer.
   Consume all attempts, including expirations, and verify the server enforces the limit.
7. Interrupt start/submit responses after the server may have accepted them. Verify a GET
   recovery page, no replay on refresh, no automatic start/resubmit, and deliberate history
   inspection before retrying. Check denial/network messages never expose diagnostic bodies.
8. Open results and selected-answer snapshots as their owner, then try another learner's
   attempt ID and another quiz/organization. Complete enrollment with ordinary lesson policy;
   confirm history/result access persists while start/submit remain denied. Confirm failing or
   skipping an optional quiz does not add a course-completion blocker.
9. Recheck the existing staff editors/readiness pages and learner legacy text, YouTube,
   audio/video/PDF content, private media links, organization navigation and session expiry.

SQL locking, concurrent start/submit races and transaction rollback require separately
authorized integration testing; the existing `LMS.Tests/README.md` describes the offline
test boundary and disposable SQL fixture design.
