# Angular Student quiz workflow

This extends the existing [lesson viewer](student-viewer.md) and preserves
[lesson/course progress policy](student-progress.md). Before editing, the actual
quiz/attempt controllers and DTOs, start/submit/result/answer/history handlers,
EnsureQuizReadable, student-options handler, MVC QuizApi/QuizLearningController,
views/scripts, and [MVC quiz batch record](../../EnterpriseLMS/docs/quiz-ui-batch.md)
were inspected. That prior record exists in the backend working tree; it was not
present in the frontend at the start of this batch.

## Routes and exact contracts

Angular lazily loads:

- `/workspace/student/courses/:courseId/sections/:sectionId/lessons/:lessonId/quiz`
- The same path plus `/attempts/:attemptId` for resume/status/results/review.

The lesson viewer links to quiz instructions/history. Shell revalidation recognizes
both routes. There is no quiz mutation from opening a lesson, following a link,
refreshing history, playing media or the countdown.

Let B be `/bff/student/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/quiz`.
All resource IDs have GUID constraints. All reads require the verified session and
selected organization plus current Student access, published parents/quiz and owned
Active/Completed enrollment, matching existing MVC discovery policy.

| Method | Route | Request JSON | Success |
| --- | --- | --- | --- |
| GET | B | none | 200 safe page with instructions/history; no selected attempt/questions |
| GET | B`/attempts` | none | 200 same instructions/history contract |
| GET | B`/attempts/{attemptId}` | none | 200 owner-selected attempt/status and permitted questions or result/review |
| GET | B`/attempts/{attemptId}/questions` | none | 200 `{organizationId,courseId,sectionId,lessonId,attemptId,questions}` for Started only |
| GET | B`/attempts/{attemptId}/result` | none | 200 `{organizationId,courseId,sectionId,lessonId,result}` for Submitted only |
| GET | B`/attempts/{attemptId}/review` | none | 200 `{organizationId,courseId,sectionId,lessonId,review}` for Submitted only |
| POST | B`/start` | `{}` | 200 `{attemptId}`; existing live attempt is resumed without an upstream start |
| POST | B`/attempts/{attemptId}/submit` | `{confirmed:true,answers:[{questionId,selectedOptionIds:[]}]}` | 204; then GET selected stored status |

All mutations use existing Web antiforgery cookie plus `X-CSRF-TOKEN`. Unknown body
members are rejected, including injected organization/user/role/correctness fields.
Unconfirmed submission is 400; null answer/selection collections are rejected. Request
bodies are limited to 128 KiB. No browser organization header is authority.

```typescript
interface StudentQuiz {
  organizationId: string; courseId: string; sectionId: string; lessonId: string;
  quiz: null | {
    id: string; lessonId: string; title: string; description: string | null;
    passingPercentage: number; timeLimitInMinutes: number; maxAttempts: number;
    isPublished: boolean;
  };
  active: boolean;
  attempts: Attempt[];
  attempt: Attempt | null;
  questions: Array<{
    id: string; questionText: string; questionType: 1 | 2 | 3;
    displayOrder: number; marks: number;
    options: Array<{ id: string; optionText: string; displayOrder: number }>;
  }>;
  result: null | {
    attemptId: string; earnedMarks: number | null; possibleMarks: number;
    passingPercentageSnapshot: number; isPassed: boolean | null;
  };
  review: null | {
    attemptId: string;
    answers: Array<{
      questionId: string; questionText: string;
      selectedOptions: Array<{ optionId: string; optionText: string }>;
    }>;
  };
}
interface Attempt {
  attemptId: string; attemptNumber: number; status: 1 | 2 | 3;
  deadline: string; earnedMarks: number | null; possibleMarks: number;
  isPassed: boolean | null;
}
```

GUIDs and UTC deadlines are JSON strings. Statuses are Started=1, Submitted=2,
Expired=3; question types are SingleChoice=1, MultipleChoice=2, TrueFalse=3.
Non-Submitted attempts have earnedMarks/isPassed suppressed. No user ID, JWT,
physical path, arbitrary URL, correct-option flag, answer key or per-question
correctness is projected. Only stored selected-answer snapshots are reviewed.
No client grade/percentage recalculation is performed.

## Authorization, reuse and disclosure

The BFF derives user/organization from LearnerSessions.Require, then reuses
StudentUnit for live Student context, publication, owned enrollment and stored
course/section/lesson checks. It calls the existing LearnerQuiz adapter for quiz
lookup, owner history, safe questions/options and submitted-only result/review.
Quiz must belong to the stored lesson; questions must belong to that quiz; selected
attempt must appear in the quiz's owner-scoped history; returned result/review IDs
must match it. Empty/duplicate attempt/question/option IDs and malformed question
contracts fail closed. Option membership is checked by the scoped student-options
API and submitted question/option membership is checked by the existing adapter/API.

Existing upstream routes are reused unchanged:

- GET `/{lessonId}/quiz` (root route, not `/api/Lessons/...`).
- GET `/api/Quizzes/{quizId}/questions`.
- GET `/api/Quizzes/{quizId}/questions/{questionId}/options/student` only.
- GET `/api/Quizzes/{quizId}/attempts`.
- POST `/api/Quizzes/{quizId}/attempts/start`.
- POST `/api/Quizzes/{quizId}/attempts/{attemptId}/submit`, body `{answers:[...]}`.
- GET `/api/Quizzes/{quizId}/attempts/{attemptId}/result` and `/answers`.

No staff option DTO/endpoint or generic proxy is used. APIs independently enforce
verified tenant scope, owner filters, parent checks and scoring/deadline/access rules.
Start/submit additionally require Active enrollment at the BFF boundary and retain
the existing API's authoritative checks. StartQuiz reuses a current live attempt
from history. A returned new attempt ID is read back through owner history before
it is exposed to Angular. Submitted attempts go through the existing repeated-submit
API guard; Started attempts past deadline reach its expiration guard on deliberate
submission. GET never expires an attempt or writes lesson LastAccessedAt/progress.

Completed learners retain MVC-permitted history/result/review and cannot start or
submit. Existing published-parent/quiz discovery gates still apply to nested BFF
history, including historical attempts. This deliberately matches MVC; it does not
broaden access to unpublished content merely because a snapshot API can read it.
Review is Submitted-only and does not call a correctness-key endpoint. Expired rows
never call submitted-only result/review APIs. Scoring, attempt limits, transaction
boundaries, deadline rules, quiz editing policy, and course completion are unchanged.

Errors retain the sanitized BFF `{code,message,fieldErrors}` shape: 400 invalid or
unconfirmed/CSRF requests, 401 expiry, 403 access denial/Completed mutation, 404 hidden
or mismatched resources/foreign attempts, 409 deadline/repeated submission/attempt
limit or non-Submitted result/review, and 502 transport/contract failure. Upstream
messages are not displayed. All responses remain no-store; JWTs remain server-side.

## Angular behavior and recovery

Instructions show passing percentage, time limit, maximum attempts, recorded history,
server deadlines and eligibility. Start is explicit. An existing live attempt has a
resume link that performs GET only. Native radios render SingleChoice/TrueFalse;
checkboxes render MultipleChoice. Labels/legends and all author/snapshot text use
Angular interpolation; no HTML/script/resource trust bypass is added.

The timer compares the stored server deadline with Date.now and never extends it,
starts another attempt, writes expiration, or submits. Device clock skew and browser
throttling can affect the display; the server remains authoritative. A displayed
elapsed timer explains this and leaves any deliberate late submission to the server.
The interval runs outside Angular's zone and is disposed on component destruction.

Selections are memory-only and are not autosaved. Resume/refresh restores status and
deadline, never unsent selections. Router CanDeactivate guards both leaving and
switching attempt IDs; beforeunload requests the native warning for unsent answers
or a pending action. Refresh asks before discarding. Submission has an explicit
confirmation step; pending controls and service guards prevent duplicate clicks.
Unanswered questions are submitted as empty selectedOptionIds; grades come from
stored server results only.

Success, rejection, invalid start response and network/timeout ambiguity recover with
GET history/status, never an automatic repeated POST. Answers are discarded before
recovery; a message explains this. After a rejected/ambiguous action the learner must
inspect stored status and acknowledge it before choosing a new mutation. A Submitted
recovery displays stored results, an Expired recovery displays expired status, and
failed recovery clears the page so no further action is available until a successful
read. A successful explicit start navigates to its returned stored attempt ID.

Captured session identity, epoch and read sequence reject stale GET/start/submit/CSRF
responses. Organization/role/session changes, logout, expiry and uncertain/revalidating
context clear attempts, questions, answers and confirmation. As with the existing
viewer, session revalidation can discard unsent selections; no recovery claim is made.
The leave warning does not block logout/context cleanup. Nothing is persisted in
local/session storage, URLs, TempData or added logs. No completion policy or media
tracking is attached to the quiz. Existing CSP and media/frame headers are unchanged;
styles reuse the external viewer stylesheet.

## Files changed in this batch

Frontend:
- `src/app/features/student-quiz/student-quiz.models.ts`
- `src/app/features/student-quiz/student-quiz.service.ts`, `student-quiz.service.spec.ts`
- `src/app/features/student-quiz/student-quiz.component.ts`, `student-quiz.component.html`, `student-quiz.component.spec.ts`
- `src/app/features/student-quiz/student-quiz.guard.ts`, `student-quiz.testing.ts`
- `src/app/features/student-viewer/student-viewer.routes.ts`, `student-viewer.component.html`
- `src/app/core/auth/workspace-routing.ts`, `src/app/app.routes.spec.ts`
- `docs/student-quiz.md`, `docs/student-viewer.md`

Backend:
- `LMS.Web/Models/BffStudentQuizModels.cs`
- `LMS.Web/Services/BffStudentQuizApi.cs`
- `LMS.Web/Controllers/BffStudentQuizController.cs`
- `LMS.Tests/Integration/BffStudentQuizTests.cs`
- `docs/student-quiz.md`, `docs/quiz-ui-batch.md`

## Remaining live verification and manual checklist

No normal hosts, database connections, migrations, packages, staging, commits,
pushes or branch switches are part of this batch. Tests use HTTP fakes, in-process
MVC and real business handlers with mocked repositories. Actual browser user flows,
real deployment cookies/CSRF/CSP, keyboard/mobile/screen-reader interactions, native
leave prompts and clock-skew behavior remain unverified. SQL concurrency, transactional
rollback, real owner/tenant filtering and deadline races also remain unverified.
Existing media playback/seeking, YouTube and native sandboxed PDF rendering remain
unverified; automated DOM/protocol checks do not constitute live playback testing.

For a later separately authorized environment:
1. Navigate lesson to quiz, inspect instructions/history, start once and resume the
   same ID/deadline after refresh. Verify unsent selections are not claimed restored.
2. Select all three types, use keyboard controls, cancel/accept the submit confirmation,
   double-click controls, and check exact ID-only payloads with CSRF and server-only JWTs.
3. Let the timer elapse and verify zero automatic POSTs. Submit at the server boundary,
   inspect stored expiration, repeat submission and consume the maximum attempts.
4. Interrupt start/submit responses after possible commits. Verify one POST, GET-only
   recovery, discarded selections, explicit status acknowledgement and owner results.
5. Try foreign course/section/lesson/quiz/attempt IDs, another owner, organization
   headers, expired/revoked sessions and missing/forged CSRF. Confirm no correctness
   keys or unauthorized questions/snapshots leak and stale responses stay discarded.
6. Test leaving/switching attempts, refresh, browser close, logout and organization
   change with unsent answers or pending requests. Verify warnings and cleanup.
7. As a Completed learner, check permitted history/results/review and disabled writes.
   Recheck catalog, enrollment, viewer/media, MVC quiz and ordinary course completion;
   skipping/failing a quiz must not add a completion prerequisite.

## Automated verification

Final commands and results follow after final checks.

Final verification on 2026-10-15:

- `npm.cmd run build`: passed, separate lazy Student quiz component chunk.
- `npm.cmd test -- --watch=false --browsers=ChromeHeadless --progress=false`:
  **113 passed**, zero failures. Existing Karma builder deprecation warning remains.
- `dotnet build EnterpriseLMS.slnx --no-restore --no-incremental -m:1 -p:UseSharedCompilation=false`:
  passed, **0 warnings / 0 errors**.
- The following backend regression command passed **449 tests**, zero failures/skips:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-build --no-restore --filter "Category!=SqlServerIntegration&(FullyQualifiedName~Quiz|FullyQualifiedName~BffStudentProgressTests|FullyQualifiedName~BffStudentViewerTests|FullyQualifiedName~BffStudentTests|FullyQualifiedName~BffAuthTests|FullyQualifiedName~BffOrganizationTests|FullyQualifiedName~EnrollmentProgressWebTests|FullyQualifiedName~LearnerWebTests|FullyQualifiedName~InProcessMvcTests|FullyQualifiedName~OrganizationSelectionTests|FullyQualifiedName~LearnerWorkflowPolicyTests|FullyQualifiedName~EnrollmentCancellationTests|FullyQualifiedName~EnrollmentResponseMetadataTests|FullyQualifiedName~TenantHandlerTests|FullyQualifiedName~LessonContentTests|FullyQualifiedName~MediaAssetTests|FullyQualifiedName~CourseCompletionTests|FullyQualifiedName~LearnerWriteTests)" --verbosity minimal
```

- `node --test LMS.Tests/quiz-ui.test.cjs`: **1 passed**, zero failures; existing MVC
  duplicate-click/countdown behavior remains unchanged.
- `Get-ChildItem -LiteralPath LMS.Web/wwwroot/js -Filter '*.js' | ForEach-Object { node --check $_.FullName }`
  and `node --check LMS.Tests/quiz-ui.test.cjs`: passed.
- `git diff --check` in both repositories, plus direct trailing-whitespace/EOF
  checks for all new/edited batch files: passed.

New tests cover safe options/snapshot rendering, CSRF/injected authority, nested,
tenant and owner denials, explicit start/resume without another start, original
server deadline, all three answer types and ID-only payloads, confirmation and
duplicate guards, expiration/repeated submission, ambiguous committed outcomes,
read-only recovery, historical access, unsent-answer guards and stale state.
Existing quiz suites exercise real scoring/deadline/disclosure handlers with mocked
repositories; existing catalog/enrollment/auth/shell/viewer/media/progress suites
also pass. Browser native leave prompts and actual database behavior are not proven
by these tests. The initial Angular test attempts required a test-only callback type
annotation and invoking the captured beforeunload listener without dispatching
Karma's page-reload event. The final full suite passed after those corrections.

Branches remain `chore/angular-dependency-upgrade` (frontend) and
`feature/learning-flow-stability` (backend), with existing changes preserved.
No packages, migrations, business-rule changes, database access, normal hosts,
staging, commits, pushes or branch switches occurred.
