# Angular Student lesson progress and enrollment completion

This extends [student-viewer.md](student-viewer.md). Before editing, the Angular
viewer, that document, MVC LearnerActionsController/LearnerWorkflowApi and progress
partials, actual API controllers/response contracts, LessonProgress entity and
start/update/complete/read handlers, and CompleteEnrollmentHandler were inspected.

## Rules preserved

- Reads permit Active and Completed enrollments and do not create progress, save
  entities, call Access(), or change LastAccessedAt. A lesson without stored progress
  is returned as null, matching the existing API's 404 not-started representation.
- Lesson start is explicit. Update/complete require an existing progress record.
  Percentages are decimals in [0,100]. An explicit update to 100 completes the lesson
  under the existing entity behavior. Lesson completion does not complete enrollment.
- Mutations require Active enrollment. Course completion requires at least one
  currently published lesson and every currently published lesson completed. The
  existing guarded enrollment API rechecks this within its transaction.
- Current published-lesson counts/percentage/completion are separate from historical
  enrollment status/completion timestamp. Historical completion survives publication
  changes; a Completed enrollment can show current progress below 100%.
- No quiz-pass, required-item, assignment or playback tracking requirements were added.
  Existing business handlers/repositories/schema and MVC workflows were not changed.

## Exact BFF routes

Prefix C = `/bff/student/courses/{courseId:guid}`.
Prefix L = `C/sections/{sectionId:guid}/lessons/{lessonId:guid}`.

| Method | Route | JSON body | Success |
| --- | --- | --- | --- |
| GET | `C/progress` | none | 200 safe stored response below; sectionId/lessonId/lesson null |
| GET | `L/progress` | none | 200 same response with nested IDs and stored lesson or null |
| POST | `L/progress/start` | `{}` | 204, then browser GET `L/progress` |
| PATCH | `L/progress` | `{progressPercentage: number}` | 204, then browser GET `L/progress` |
| POST | `L/progress/complete` | `{}` | 204, then browser GET `L/progress` |
| POST | `C/enrollments/{enrollmentId:guid}/complete` | `{confirmed: true}` | 204, then browser GET `C/progress` |

Required JSON body contracts reject unknown properties, including user, role and
organization authority. Update requires a non-null percentage between 0 and 100;
confirmation defaults false. Every mutation uses the existing global Web antiforgery
filter, Secure/HttpOnly antiforgery cookie and in-memory `X-CSRF-TOKEN` flow. Request
bodies are capped at 16 KiB. Reads need only the authenticated session cookie.

```typescript
interface StudentProgress {
  organizationId: string;
  courseId: string;
  sectionId: string | null;
  lessonId: string | null;
  current: {
    totalLessons: number;
    completedLessons: number;
    progressPercentage: number;
    isCompleted: boolean;
  };
  enrollment: {
    id: string;
    status: 'Active' | 'Completed';
    enrolledAt: string;
    completedAt: string | null;
  };
  lesson: null | {
    isCompleted: boolean;
    progressPercentage: number;
    startedAt: string;
    lastAccessedAt: string;
    completedAt: string | null;
  };
}
```

IDs and timestamps are strings. No user ID, JWT, server-session reference, physical
path, arbitrary URL or upstream diagnostics is projected. All responses are no-store.
The server obtains user and selected organization solely from the verified session,
checks live Student access (cached roles are not authority), publication, owned
Active/Completed enrollment, every stored parent, and stored progress user/lesson
identity. Supplied enrollment IDs must match the course's stored owned enrollment.
Browser organization headers cannot override server selection.

Reads reuse StudentSections/StudentLessons, CourseLearning/CourseProgress,
OwnEnrollment, and the scoped authenticated API client. Lesson detail is checked
against the stored section before reading progress. No content, quiz, assignment
or unrestricted asset endpoint is needed for progress reads. Existing viewer/media
routes are retained. Each mutation revalidates the stored context and calls
ChangeLessonProgress or ChangeEnrollment; the existing API remains authoritative
when state changes between reads and submission. BFF does not preempt the API's
zero/unfinished-prerequisite decision with a new completion algorithm.

Upstream operations are exactly:

- GET `/api/Courses/{courseId}/progress` and `/api/Lessons/{lessonId}/progress`.
- POST `/api/Lessons/{lessonId}/start` with `{}`.
- PATCH `/api/Lessons/{lessonId}/progress` with `{progressPercentage}`.
- PATCH `/api/Lessons/{lessonId}/complete` with `{}`.
- PATCH `/api/Enrollments/{enrollmentId}/complete` with `{}`.

Errors reuse sanitized BFF `{code,message,fieldErrors}`. 400 covers invalid JSON,
percentage/confirmation and CSRF failure; 401 expires the session; 403 denies scope,
ownership or enrollment access; 404 hides missing/unpublished/mismatched resources.
Completed-enrollment mutation attempts produce 409 before transport, and guarded
API prerequisite/race conflicts retain 409. Other API authorization failures keep
their actual safe status. Transport failures return 502; mutations are never replayed.
The BFF treats lesson-progress 404 as not-started only after validating accessible
published parents. A concurrent unpublication can race that read; every later action
and every media request still revalidates access.

## Angular interaction and state

The existing course page now loads a stored course-progress panel and the existing
lesson viewer loads a stored lesson-progress panel, each via GET only. Section
navigation remains content-only. Native media playback, ending playback, PDF opening,
YouTube activity and navigation never call a mutation or create a progress record.
The panels show loading/error/reload states, timestamps, zero-lesson/incomplete
prerequisite explanations, and separate current versus historical information.

An Active lesson offers Start when no record exists, then percentage update and
explicit Complete while not completed, matching the MVC UI. Saving 100% explains its
completion effect. Completed enrollment permits reads but offers no learner mutation
controls. Course completion has a separate confirmation step and is offered as enabled
only when the current read suggests eligibility. This eligibility is advisory; the
server rechecks every submission. There is no automatic completion or retry.

Pending state disables fieldsets/actions and a service guard rejects duplicate clicks.
CSRF bootstrap is followed by a captured-session check before sending a write. Success,
validation/conflict and ambiguous transport outcomes refresh only stored progress via
GET. Failed recovery clears progress and disables actions until a successful reload.
401/403/404 clears the progress context and tells the parent viewer to clear content
and release media; 401 also uses the existing session-expiry interceptor. A missing
progress record after concurrent changes can also yield 404; safely reload the viewer.

Progress, input/confirmation and error state live only in a component-scoped service.
Logout, expiry, organization/role/session changes, uncertain/revalidating context and
navigation clear it. Captured session identity, epoch and read sequence reject stale
GET/mutation results, including CSRF bootstrap races. Returned organization and parent
IDs must match the selection; enrollment IDs must be canonical GUIDs. Progress reloads
do not reload content or disturb authorized playback. The existing viewer cleanup and
same-origin authenticated media controls remain in place. No CSP/frame/security header
changes or HTML/resource trust bypasses were added for progress.

## Changed files in this batch

Frontend:
- `src/app/features/student-viewer/student-progress.models.ts`
- `src/app/features/student-viewer/student-progress.service.ts`
- `src/app/features/student-viewer/student-progress.service.spec.ts`
- `src/app/features/student-viewer/student-progress.component.ts`
- `src/app/features/student-viewer/student-progress.component.html`
- `src/app/features/student-viewer/student-progress.component.spec.ts`
- `src/app/features/student-viewer/student-progress.testing.ts`
- `src/app/features/student-viewer/student-viewer.component.ts`
- `src/app/features/student-viewer/student-viewer.component.html`
- `src/app/features/student-viewer/student-viewer.component.spec.ts` (isolates existing content/media tests with a progress stub)
- `src/app/features/student-viewer/student-viewer.service.ts` (parent denial cleanup)
- `src/app/app.routes.spec.ts` (nested navigation reads progress without writing)
- `docs/student-viewer.md`, `docs/student-progress.md`

Backend:
- `LMS.Web/Models/BffStudentProgressModels.cs`
- `LMS.Web/Services/BffStudentProgressApi.cs`
- `LMS.Web/Controllers/BffStudentProgressController.cs`
- `LMS.Tests/Integration/BffStudentProgressTests.cs`
- `docs/student-viewer.md`, `docs/student-progress.md`

## Remaining live verification gaps

Tests use fake HTTP, mocked business repositories and in-process MVC servers; normal
API/Web hosts, databases, seeds and migrations are excluded. No database behavior or
real Identity/SQL transaction/publication races has been verified in this batch.
Browser MP3/MP4 playback/seeking, YouTube and sandboxed native PDF rendering remain
unverified. Real HTTPS/proxy/CSP/shared cookies, device layout, screen-reader/focus
behavior and concurrent real-user completion need a later authorized live check.
The existing Karma builder deprecation warning remains. No packages were added.

Later manual checklist:
1. Open course/lesson pages, navigate, play/seek and open PDF. Inspect the network and
   confirm GET-only reads and unchanged stored LastAccessedAt until an explicit action.
2. Start a lesson, save a fractional percentage, save 100%, and separately complete
   another lesson. Check stored timestamps, counts and explicit-only mutations.
3. Test zero published lessons and unfinished lessons; confirm completion explanations
   and server conflicts. Complete an eligible Active enrollment with confirmation,
   retry completion, then alter published content and inspect history versus current
   counts while Completed reads remain available and mutations stay disabled.
4. Delay/reject writes and recovery reads. Confirm one mutation only, safe messages,
   no retry, cleared stale state, and accurate recovery after an ambiguous commit.
5. Change organization, logout/expire/revoke during CSRF/GET/write requests. Try foreign
   nested IDs, enrollment IDs and browser organization headers; confirm no disclosure,
   stale update or subsequent unauthorized media range.
6. Recheck catalog, enroll/cancel, shell/auth and MVC progress/media workflows.

## Automated results

Final commands and results are recorded here after verification.

Verification on 2026-10-15:

- `npm.cmd run build`: passed, production output with the lazy viewer/progress UI.
- `npm.cmd test -- --watch=false --browsers=ChromeHeadless --progress=false`:
  **94 passed**, zero failures. Existing Karma builder deprecation warning remains.
- `dotnet build EnterpriseLMS.slnx --no-restore --no-incremental -m:1 -p:UseSharedCompilation=false`:
  passed, **0 warnings / 0 errors**.
- The exact backend command below passed **372 tests**, zero failures/skips:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~BffStudentProgressTests|FullyQualifiedName~BffStudentViewerTests|FullyQualifiedName~BffStudentTests|FullyQualifiedName~BffAuthTests|FullyQualifiedName~BffOrganizationTests|FullyQualifiedName~EnrollmentProgressWebTests|FullyQualifiedName~LearnerWebTests|FullyQualifiedName~InProcessMvcTests|FullyQualifiedName~OrganizationSelectionTests|FullyQualifiedName~LearnerWorkflowPolicyTests|FullyQualifiedName~EnrollmentCancellationTests|FullyQualifiedName~EnrollmentResponseMetadataTests|FullyQualifiedName~TenantHandlerTests|FullyQualifiedName~LessonContentTests|FullyQualifiedName~MediaAssetTests|FullyQualifiedName~CourseCompletionTests|FullyQualifiedName~LearnerWriteTests" --verbosity minimal
```

The selected suites include actual business-handler tests that verify read-only
LastAccessedAt, Active/Completed gates and current/history semantics, in addition to
the new fake-upstream BFF integration tests. New Angular tests cover explicit actions,
payloads/CSRF, duplicate guards, confirmation, zero/incomplete/Completed rendering,
read-only navigation, ambiguous recovery, failed recovery, context/expiry cleanup,
parent denial signaling, and stale GET/mutation/CSRF responses. Content/media unit
tests remain isolated with a progress-component stub; lazy-route tests use real panels.

The first Angular run required waiting for ngModel's microtask before asserting its
input value. The first backend run exposed incorrect validation-attribute targeting
on the update request record; attributes were moved to its constructor parameter.
Both full requested suites passed after those corrections.

`git diff --check` and direct changed-file trailing-whitespace/EOF checks passed in
both repositories, including new files. Branches remain
`chore/angular-dependency-upgrade` and `feature/learning-flow-stability`. Existing
pending changes are preserved. No packages, business-rule changes, database access,
migrations, normal hosts, staging, commits, pushes or branch switches occurred.
