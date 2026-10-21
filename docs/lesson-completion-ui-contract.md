# Lesson completion UI contract

Scope: MVC learner pages in EnterpriseLMS. Angular is unchanged. This removes the MVC percentage editor, not the percentage-update APIs. No schema, stored record, timestamp, or historical enrollment is migrated or reset.

## MVC interaction

The lesson page is `GET /learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}`. Its progress panel displays:

| Stored lesson record | Read-only state | Active-enrollment action |
| --- | --- | --- |
| Absent | Not started; no stored percentage is invented | Start lesson |
| Present, `isCompleted: false` | In progress; exact stored percentage and timestamps | Mark lesson complete, with required confirmation checkbox |
| Present, `isCompleted: true` | Completed; exact stored percentage and timestamps | None |

State comes from record existence and `isCompleted`, not a guessed percentage threshold. Existing partial percentages remain visible without rounding to integer percentages. A historical completed record with a lower percentage (possible through the compatibility update operation) still displays Completed and its actual stored percentage. Completed enrollments allow published-content reads but display no learner progress mutation controls.

The forms retain antiforgery tokens. Start posts to `/learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}/progress/start`. Mark lesson complete posts to the same prefix with `/progress/complete`, including form field `Form.Confirmed=true`. The controller checks confirmation on the server, after validating learner scope/parents through read-only calls. Missing/false confirmation redirects to read-only recovery with no upstream mutation. Confirmed completion sends the existing API PATCH with `{}`; no percentage is supplied. Successful actions redirect to the lesson (POST/redirect/GET). Neither Save progress nor a percentage input is rendered.

Enrollment completion stays a separate confirmed form on the course page: `POST /learn/{organizationId}/courses/{courseId}/enrollments/{enrollmentId}/complete`, form field `confirmed=true` and antiforgery token. It uses the existing guarded completion handler.

## Exact BFF contracts

All IDs below are GUID route segments. Define `C = /bff/student/courses/{courseId}` and `L = C/sections/{sectionId}/lessons/{lessonId}`. These are BFF routes; MVC uses `units`, whereas BFF uses `lessons`.

| Method | Exact route | JSON request | Success |
| --- | --- | --- | --- |
| GET | `/bff/student/courses/{courseId}/progress` | None | 200, progress object |
| GET | `/bff/student/courses/{courseId}/sections/{sectionId}/lessons/{lessonId}/progress` | None | 200, progress object |
| POST | `/bff/student/courses/{courseId}/sections/{sectionId}/lessons/{lessonId}/progress/start` | `{}` | 204, empty body |
| POST | `/bff/student/courses/{courseId}/sections/{sectionId}/lessons/{lessonId}/progress/complete` | `{}` | 204, empty body |
| POST | `/bff/student/courses/{courseId}/enrollments/{enrollmentId}/complete` | `{"confirmed":true}` | 204, empty body |

Start/complete accept the existing empty `BffProgressAction`; they do **not** require a new `confirmed` JSON member. Those request types reject unknown members, including injected user/organization identifiers. A future learner UI should confirm lesson completion before sending `{}`. MVC's new checkbox does not change the BFF schema. Re-read stored progress after successful mutation; 204 contains no progress snapshot. After an uncertain response, recover with GET, never automatic mutation replay.

Both progress GETs return this unwrapped JSON shape (camelCase; no `data` envelope):

```json
{
  "organizationId": "<guid>",
  "courseId": "<guid>",
  "sectionId": "<guid or null>",
  "lessonId": "<guid or null>",
  "current": {
    "totalLessons": 2,
    "completedLessons": 1,
    "progressPercentage": 50,
    "isCompleted": false
  },
  "enrollment": {
    "id": "<guid>",
    "status": "Active",
    "enrolledAt": "<ISO date-time>",
    "completedAt": null
  },
  "lesson": {
    "isCompleted": false,
    "progressPercentage": 37.5,
    "startedAt": "<ISO date-time>",
    "lastAccessedAt": "<ISO date-time>",
    "completedAt": null
  }
}
```

On course reads, `sectionId`, `lessonId`, and `lesson` are null. On an unstarted lesson read, `lesson` is null (200); the API's missing-progress 404 is translated to that representation. Enrollment `status` is `Active` or `Completed`. Completed timestamps are nullable date-time strings, returned as stored; clients must not replace them with navigation time. The BFF does not expose JWTs, server session references, or lesson-progress owner IDs in this response.

### Authorization and CSRF

BFF access uses the authenticated `__Host-LmsLearner` secure/HttpOnly/SameSite=Strict cookie and server-held session. A selected, verified Student organization membership is required; staff/platform roles do not gain learner mutation rights. The server forwards its stored bearer token and selected `X-Organization-Id` to the API. Browser-supplied organization headers do not override that session selection.

Course/section/lesson parent relationships, publication, progress ownership, and enrollment ownership/course are rechecked. Reads allow Active or Completed enrollment. Mutations require Active enrollment; Cancelled enrollment has no learner read/mutation access. The API remains authoritative if state changes after the BFF read checks.

Unsafe BFF requests require `Content-Type: application/json` and `X-CSRF-TOKEN` with the request token from `GET /bff/antiforgery` (response `{"requestToken":"...","headerName":"X-CSRF-TOKEN"}`), plus the antiforgery cookie. Bootstrap a fresh token after login/context changes. MVC posts use their rendered antiforgery form field. CSRF and scope checks were not removed.

### Status/error mappings

BFF errors use `{"code":"...","message":"...","fieldErrors":null}`; automatic model validation may supply a field-errors object instead of null. Upstream private diagnostics are sanitized. Responses are no-store.

| Status | BFF code | Meaning |
| --- | --- | --- |
| 200 | — | Progress read; missing lesson progress is represented by `lesson:null` |
| 204 | — | Mutation accepted; read stored state separately |
| 400 | `validation_failed` | Invalid JSON/value, unknown members, or missing enrollment confirmation |
| 400 | `csrf_validation_failed` | Missing/invalid antiforgery token |
| 401 | `session_expired` | Missing/expired cookie or invalid upstream authentication; no BFF login redirect |
| 403 | `access_denied` | Wrong/revoked membership, organization/owner mismatch, Cancelled/no enrollment, or Active-only API mutation denial |
| 404 | `not_found` | Missing/unpublished resource, wrong route parent/enrollment ID, or completion before Start lesson |
| 405 | `method_not_allowed` | Wrong HTTP method |
| 409 | `conflict` | BFF precheck sees Completed enrollment; repeated enrollment completion; server prerequisites/state conflict |
| 413 | `validation_failed` | Request exceeds limit (progress controller limit: 16,384 bytes) |
| 415 | `validation_failed` | Unsupported content type |
| 429 | `rate_limited` | A configured rate limiter rejected the request |
| 502 | `authentication_service_unavailable` | Transport/timeout/invalid upstream JSON; outcome may be unknown, do not replay |
| 500 | `unexpected_error` | Unhandled BFF failure |

The generic 502 code/message is existing shared BFF behavior, including failures outside login. A race reaching the API after a successful Active BFF precheck can return 403 rather than the BFF's precheck 409. API errors use the API envelope, not BFF error codes.

### Existing repeat behavior (unchanged)

- Starting an already-started lesson with Active enrollment succeeds and updates `lastAccessedAt`; it retains existing progress and completion fields. Start is not a GET substitute.
- Completing before a lesson progress record exists returns 404. Start first.
- Completing an already-completed lesson while enrollment is still Active succeeds through the existing handler. `LessonProgress.Complete()` sets 100% and refreshes `completedAt` and `lastAccessedAt`; this is **not timestamp-idempotent**. The MVC UI hides completion after it is recorded. This change does not alter the domain/API repeat semantics; do not automatically retry completion.
- Once enrollment is Completed, lesson writes are denied (BFF precheck 409; direct API 403) without touching progress/timestamps.
- Repeating enrollment completion returns 409. The first enrollment completion timestamp is retained; no reopening or new history is created. The BFF can reject this before forwarding a mutation.
- GETs, navigation, content/media reads and playback do not start, update, complete, save, or touch lesson progress/LastAccessedAt. No read handler or database record was changed by this UI work.

## Current course and enrollment rules

Course progress is `round(completed published lessons / total published lessons * 100, 2)`, based on distinct completed lesson records owned by the current learner. Partial stored percentages are not averaged. Unpublished lessons are excluded. Zero published lessons yields 0% and incomplete.

Lesson completion records 100% and timestamps but leaves enrollment Active. Enrollment completion requires a separately confirmed action, an owned Active enrollment in a published course, at least one currently published lesson, and every currently published lesson completed. The completion handler rechecks prerequisites in its existing enrollment transaction. It records historical enrollment completion once. Subsequent publication changes may change current course progress without rewriting Completed enrollment history.

No quiz-pass, assignment-grade, required-content, playback/media tracking, or automatic enrollment-completion prerequisite is introduced. Existing percentages, timestamps and histories are displayed, not normalized or backfilled.

## Percentage-update compatibility endpoints remain enabled

| Surface | Exact route/method | Payload | Authorization/success |
| --- | --- | --- | --- |
| API | `PATCH /api/Lessons/{id}/progress` | `{"progressPercentage":37.5}` | Valid bearer JWT, verified Student membership selected by single `X-Organization-Id`, own Active enrollment and published lesson/course; 200 API envelope with stored lesson-progress response |
| BFF | `PATCH /bff/student/courses/{courseId}/sections/{sectionId}/lessons/{lessonId}/progress` | `{"progressPercentage":37.5}` | Authenticated selected Student session, parent/owner checks, Active enrollment, JSON and CSRF; 204 |
| MVC compatibility form route | `POST /learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}/progress/update` | URL-encoded `Form.ProgressPercentage=37.5` and antiforgery field (or CSRF header) | Authenticated Student session for route organization, parent/owner checks and authoritative Active-only API guard; 302 lesson redirect or recovery |

The numeric range is inclusive 0–100. BFF requires a non-null percentage and rejects extra members; MVC validates its range. The direct API's existing decimal request property is non-nullable and has no Required attribute (omitting it currently binds its default 0); its domain handler rejects out-of-range values. A progress record must already exist; update does not start a lesson. Saving 100% still invokes existing lesson completion, without completing enrollment. A lower percentage does not reopen a completed lesson or clear its completion flag/timestamp under existing domain behavior. Explicit updates change LastAccessedAt. The MVC compatibility action is retained but no longer linked from learner pages.

Underlying read/start/complete API routes are `GET /api/Lessons/{id}/progress`, `POST /api/Lessons/{id}/start`, and `PATCH /api/Lessons/{id}/complete`. Start/complete require no fields (the BFF sends `{}`). Success is 200 with `{"success":true,"message":"...","data":{...}}`; `data` contains `id`, `userId`, `lessonId`, `isCompleted`, `completedAt`, `startedAt`, `progressPercentage`, `lastAccessedAt`. Reads require Student membership and Active/Completed enrollment; writes require Active. Direct bearer API calls do not use cookie CSRF. Direct missing-progress reads return 404. Direct enrollment completion remains `PATCH /api/Enrollments/{id}/complete`, with no confirmation field at that API layer; the UI/BFF provide the separate confirmation boundary.

## Changed files and verification

- `LMS.Web/Views/Learn/_LessonProgress.cshtml`: read-only state/percentage, relevant start action, confirmed Mark lesson complete form.
- `LMS.Web/Models/LearnerWorkflowModels.cs`: MVC confirmation binding; percentage compatibility model retained.
- `LMS.Web/Controllers/LearnerActionsController.cs`: server-side completion confirmation with read-only recovery.
- `LMS.Web/Services/LearnerWorkflowApi.cs`: explicitly projects only `progressPercentage` for compatibility updates, so the MVC-only confirmation field is never added to API JSON.
- `LMS.Tests/EnrollmentProgressWebTests.cs`: rendered states/percentages (including completed-with-partial historical data), no editor/update form, confirmed completion, exact unchanged update payload and existing denial/recovery checks.
- `LMS.Tests/Integration/BffStudentProgressTests.cs`: real in-process MVC authentication/CSRF/model binding with fake API transport; confirmed MVC completion uses existing PATCH and `{}`. Existing BFF percentage compatibility, ownership, parent, Active-only, errors and read-only tests retained.
- `LMS.Tests/LearnerWorkflowPolicyTests.cs`: actual start/complete handlers require no percentage and leave enrollment Active; existing percentage-update and separate enrollment completion tests retained.
- `docs/lesson-completion-ui-contract.md`: this contract.

Validation uses only mocks and in-process fake API hosts; no database or live smoke calls.

- `dotnet test LMS.Tests/LMS.Tests.csproj --no-restore --filter 'FullyQualifiedName~EnrollmentProgressWebTests|FullyQualifiedName~LearnerWorkflowPolicyTests|FullyQualifiedName~LearnerWriteTests|FullyQualifiedName~CourseCompletionTests|FullyQualifiedName~BffStudentProgressTests|FullyQualifiedName~LearnerWebTests' --logger 'console;verbosity=minimal'`: **165 passed, 0 failed, 0 skipped**.
- `dotnet build EnterpriseLMS.slnx --no-restore --nologo`: passed, 0 errors; three existing NU1510 package-reference warnings.
- `git diff --check` with repository line-ending settings: passed. All eight changed files (including untracked files) separately checked for trailing spaces/tabs, conflict markers, and final newlines: passed. Search verified no Save progress or Form.ProgressPercentage remains in MVC learner views.

SQL integration and live database checks are intentionally unexecuted. Angular, the running smoke environment, and existing unrelated changes are untouched. No migrations, record resets, restarts, staging, commits, pushes, or branch changes were performed.
