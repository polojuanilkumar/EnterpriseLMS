# Angular Student assignments, submission history and My Grades

This extends the [Student viewer](student-viewer.md), [progress](student-progress.md)
and [quiz](student-quiz.md) workflows. Before editing, actual assignment/reporting
controllers, contracts, handlers, MVC clients/views and these backend reports were
read: [lesson-assignments-v1.md](../../EnterpriseLMS/docs/lesson-assignments-v1.md)
and [organization-reporting-gradebook.md](../../EnterpriseLMS/docs/organization-reporting-gradebook.md).
Existing submission, storage, enrollment, historical-access and scoring policies are
reused; no business handler, schema or completion requirement was added.

## Angular routes and entry points

- `/workspace/student/assignments/history`: independent own-history entry in desktop
  and mobile Student navigation; does not traverse the published lesson viewer.
- `/workspace/student/grades`: paginated own course gradebook, also in navigation.
- `/workspace/student/grades/courses/:courseId`: independently paginated quiz and
  assignment assessments for the selected course.
- `/workspace/student/courses/:courseId/sections/:sectionId/lessons/:lessonId/assignments`:
  list accessible published assignments and own historical work; linked from lesson.
- The same assignment path plus `/:assignmentId`: instructions/status/final form.
- The same path plus `/:assignmentId/submissions/:submissionId`: own stored review,
  snapshot instructions/maximum marks, submitted text, grade/feedback and PDF download.

All pages are lazy-loaded inside the existing shell and use Student navigation
checks. Revalidation recognizes the routes; server authorization remains authoritative.
Historical links can work when the lesson viewer itself is unavailable after unpublishing.

## Exact BFF routes and contracts

Let A = `/bff/student/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/assignments`.

| Method | Route | Input | Success |
| --- | --- | --- | --- |
| GET | A | none | 200 assignment page below |
| GET | A`/{assignmentId:guid}` | none | 200 page with definition and own submissions |
| GET | A`/{assignmentId:guid}/submissions/{submissionId:guid}` | none | 200 page with selected own review |
| POST | A`/{assignmentId:guid}/submit` | multipart `Text`, optional single `File`, `Confirmed=true` | 204, then Angular reads the stored detail with GET |
| GET, HEAD | A`/{assignmentId:guid}/submissions/{submissionId:guid}/attachment` | none | separately authorized PDF download |
| GET | `/bff/student/assignments/history` | none | 200 `{organizationId,rows:History[]}` |
| GET | `/bff/student/grades` | filters below | 200 `{organizationId,courses:Page<GradeCourse>}` |
| GET | `/bff/student/grades/courses/{courseId:guid}` | assessment filters below | 200 `{organizationId,course:GradeCourse,quizzes:Page<QuizGrade>,assignments:Page<AssignmentGrade>}` |

```typescript
interface AssignmentPage {
  organizationId: string; courseId: string; sectionId: string; lessonId: string;
  coursePublished: boolean; lessonPublished: boolean; enrollmentStatus: 1 | 2;
  assignments: Assignment[]; assignment: Assignment | null;
  submissions: Submission[]; submission: Submission | null; canSubmit: boolean;
}
interface Assignment {
  id: string; lessonId: string; title: string; instructions: string;
  dueAtUtc: string | null; maximumMarks: number; isPublished: boolean;
}
interface Submission {
  id: string; assignmentId: string; text: string | null; hasAttachment: boolean;
  submittedAt: string; instructionsSnapshot: string; maximumMarksSnapshot: number;
  grade: null | { marks: number; feedback: string; gradedAt: string };
}
interface History {
  courseId: string; sectionId: string; lessonId: string; assignmentId: string;
  submissionId: string; title: string; submittedAt: string; graded: boolean;
}
interface Page<T> { page: number; pageSize: number; total: number; items: T[]; }
interface GradeCourse {
  courseId: string; title: string; published: boolean; enrollmentStatus: 1 | 2;
  enrollmentCompletedAt: string | null; storedEnrollmentPercentage: number;
  publishedLessons: number; completedPublishedLessons: number; currentPercentage: number;
}
interface QuizGrade {
  courseId: string; sectionId: string; lessonId: string; quizId: string; title: string;
  attemptId: string | null; attemptNumber: number | null; status: number;
  startedAt: string | null; submittedAt: string | null; deadline: string | null;
  earnedMarks: number | null; possibleMarks: number | null;
  passingPercentageSnapshot: number | null; passed: boolean | null;
  overdue: boolean; detailAvailable: boolean; resultAvailable: boolean;
}
interface AssignmentGrade {
  courseId: string; sectionId: string; lessonId: string; assignmentId: string;
  title: string; submissionId: string | null; submittedAt: string | null;
  gradedAt: string | null; maximumMarks: number; marks: number | null;
  feedback: string | null; status: number; published: boolean;
}
```

GUIDs and timestamps are strings. BFF timestamps for report DateTime fields are
explicitly tagged UTC, preserving SQL's stored UTC clock value when DateTime.Kind
is absent. Assignment timestamps already use DateTimeOffset. Due dates show both
UTC and clearly labelled device-local time on detail; UTC remains authoritative.
No submitted text/attachment metadata is included in gradebook rows. User/grader
IDs, owner scope metadata, attachment IDs, storage paths and original filenames are
removed from the Student BFF projection. URLs use stored parent/submission IDs only.
No JWT or unrestricted asset endpoint is exposed.

### Filters and bounds

`GET /bff/student/grades` accepts the existing course filters: `page=1`,
`pageSize=20`, optional `search` (trimmed title substring, max 100 characters),
`published=true|false`, and `enrollmentStatus=1|2`. Defaults are server defaults.
Accepted pages are 1–10000 and page sizes 1–50; invalid limits/states reject with
400 rather than silently expanding. Past-end pages can be empty with scoped totals.

Course assessments accept `quizPage=1`, `assignmentPage=1`, common `pageSize=20`,
optional `quizStatus=0|1|2|3`, and `assignmentStatus=0|1|2`. Quiz states are no attempt,
Started, Submitted, Expired. Assignment states are not submitted, submitted/ungraded,
graded. Pagination preserves the other section's page and applied filters; applying
filters resets both pages. Course navigation preserves applied course filters when
paging. No per-assessment network calls are added to fetch a gradebook page.

Individual attempts use stored marks/possible marks/passing snapshots/outcomes.
Graded zero is shown as zero, not ungraded; absent grades, absent submissions,
Expired attempts and unavailable submitted results have distinct labels. Published
lesson counts/current percentage are separate from stored enrollment completion and
stored enrollment percentage. No overall grade or official best/latest-attempt policy
is invented. DetailAvailable controls quiz navigation; historical unpublished quiz
outcomes remain visible while the existing quiz detail publication policy is respected.
Assignment review links use the historical assignment route, not the published viewer.

## Authorization and reused APIs

Every operation authenticates the Web cookie and derives selected organization/user
from the verified server session. Browser organization fields/headers are not authority.
AssignmentPage rechecks live Student context and API-returned stored organization,
course, section, lesson, assignment, submission and owner relationships. Every review
and attachment request repeats those checks. It deliberately does not use StudentUnit,
whose publication gate would incorrectly block historical assignment work.

The existing assignment context/list/detail/mine/review APIs allow owners with Active
or Completed enrollment to read historical work after assignment, lesson or course
unpublication. Cancelled enrollment cannot read that history or submit. Unpublished
work without the learner's own submission is hidden by the existing APIs. The independent
history API itself resolves each stored parent/owner/enrollment; BFF reuses that checked
transport instead of creating a second history/discovery policy.

Final uploads reuse SubmitAssignment and the exact existing multipart API
`POST /api/Lessons/{lessonId}/assignments/{assignmentId}/submissions`. Active enrollment,
published parents/assignment, confirmation and no previous final submission are
required. Deadline, signature, actual size, transactional duplicate/publication/enrollment
checks and private assignment storage remain in AssignmentHandlers. Server time is
checked after staging and under its existing lock; `now >= dueAtUtc` conflicts. One
final text/PDF submission is immutable: no drafts, resubmission, late override or retry.

Read transport reuses:
- GET `/api/Lessons/{lessonId}/assignments/context` and `/assignments`.
- GET `/api/Lessons/{lessonId}/assignments/{assignmentId}`.
- GET the definition path plus `/submissions/me` or `/submissions/{submissionId}`.
- GET `/api/Assignments/submissions/me` for independent own history.
- GET `/api/Gradebook/courses` and `/api/Gradebook/courses/{courseId}`.
- GET that course path plus `/quizzes?page=...&pageSize=...&status=...` and
  `/assignments?page=...&pageSize=...&status=...` for independent assessment pages.

MyGrades/CourseGrades reuse ReportingApi filters, owner/organization page checks,
course identity/enrollment checks and row-to-course checks. Existing reporting handlers,
filtered repositories, current role checks and stored tenant/owner scopes remain the
final authority. No per-row review request or grade recomputation is introduced.

## Upload and download boundaries

POST is multipart only, with the existing global Web CSRF validation (HttpOnly/Secure
cookie plus `X-CSRF-TOKEN`, also for FormData). Angular does not set Content-Type, so
the browser supplies its multipart boundary. Unknown form fields, browser-supplied
organization authority, multiple files or a file field other than File are rejected.
Text is bounded at 100000 characters. File declarations must be a nonempty PDF,
application/pdf, safe name up to 200 characters and at most **25 MiB (26214400 bytes)**.
The BFF request/form limit is **27 MiB (28311552 bytes)**, allowing bounded multipart
and text overhead. Actual PDF signature, staging length and storage checks are in the
existing API, not replaced by frontend checks. The signature validator is not a malware
scanner or full PDF parser; inherited storage recovery limitations remain applicable.

Attachment GET/HEAD reuses AssignmentDownload with ResponseHeadersRead and upstream
GET/HEAD respectively. It validates upstream application/pdf and streams GET without
loading the PDF into Angular or a Blob. It sends no arbitrary URL, asset ID or browser
JWT. Response disposition is generated `attachment; filename="assignment-{submissionId:N}.pdf"`,
with canonical application/pdf, private/no-store, nosniff and sandbox CSP. Original
filenames and upstream disposition are not forwarded. HEAD does not copy the body.
No inline PDF privileges or broader CSP/frame policy were introduced.

Errors use sanitized BFF `{code,message,fieldErrors}`: 400 invalid form/confirmation/
CSRF, 401 expiry, 403 denied owner/tenant/enrollment, 404 hidden/mismatched resources,
409 duplicate/deadline conflicts, 413 size rejection, and 502 transport failure. The
shared assignment client now retains 409/413 status in validation exceptions for BFF
mapping; existing MVC Change still renders its existing 400 validation form behavior.
Upstream diagnostics are not displayed by Angular or the BFF.

## Angular state and explicit action flow

Tailwind pages use native labelled textarea/file inputs, fieldsets, confirmation and
accessible loading/error/empty states. All instructions, titles, submitted text and
feedback render through interpolation. No HTML trust bypass or new packages are used.
A leave guard/native beforeunload warning protects unsent text/files or an in-flight
upload. Client PDF/text checks improve UX; the server revalidates each submission.

The learner first reviews the final work, then explicitly confirms. Pending fieldsets
and a service guard reject duplicate clicks. After success, rejection or ambiguous
network failure, the service reads stored detail with GET only. It never replays an
upload. After failure it clears text/file and requires stored-state acknowledgement
before another deliberate action; if a final submission exists, no further submission
is offered. Failed recovery clears the page and disables actions until a successful
read. Native download failures can appear as sanitized HTTP error responses; actual
browser download/session behavior remains to be tested.

All sensitive data is component-scoped in memory. Logout, expiry, organization/role/
session changes and uncertain/revalidating context clear text, File references,
confirmation, submission/feedback and grade pages. Native file input values are also
cleared when invalid, reset, detached or destroyed. No browser storage is used. Captured
session identity, epoch and read sequence reject stale GET, upload and CSRF responses.
As in the existing shell, revalidation can discard unsent work; nothing claims it was
saved. No assignment/grade read changes LastAccessedAt or course/lesson completion.

## Migration and deployment prerequisites (not checked or applied)

This batch adds **no migrations or schema changes**. Live assignment/reporting endpoints
require the existing assignment tables and their reviewed prior migration chain,
including **20261004061629_AddLessonAssignments**. The working tree also contains pending
**20261006122454_AddOrganizationInvitations** and **20261006135202_AddAuditEvents**.
Their presence does not prove deployment, and no assignment, invitation or audit
migration is assumed applied. No database was opened, inspected, seeded or migrated.
A separately authorized deployment must review actual schema/migration state and the
full required chain before enabling these endpoints; assignment-backed report queries
will not work against a database missing assignment tables. Audit/session infrastructure
may also depend on its pending schema. No browser-side schema bypass is introduced.

The API needs durable private storage at the configured private assignment location
(existing App_Data/PrivateAssignments default), outside wwwroot, and coordinated
file/database backups. Staged-file/orphan/crash/ambiguous-commit/multi-instance recovery
limitations in lesson-assignments-v1.md are unchanged. No cleanup or migration operation
was run by this batch.

## Changed files

Frontend:
- `src/app/features/student-assignments/student-assignments.models.ts`
- `student-assignments.service.ts`, `student-assignments.service.spec.ts` in that directory
- `student-assignments.component.ts`, `.component.html`, `.component.spec.ts` in that directory
- `student-assignments.guard.ts`, `student-assignments.testing.ts` in that directory
- `src/app/features/student-grades/student-grades.models.ts`
- `student-grades.service.ts`, `.service.spec.ts`, `student-grades.component.ts`, `.component.html`, `.component.spec.ts`, `student-grades.testing.ts` in that directory
- `src/app/app.routes.ts`, `src/app/app.routes.spec.ts`
- `src/app/core/auth/workspace-routing.ts`
- `src/app/features/student-viewer/student-viewer.routes.ts`, `student-viewer.component.html`
- `src/app/layout/application-shell.component.ts`, `.component.spec.ts`
- `docs/student-assignments.md`, `docs/student-viewer.md`

Backend:
- `LMS.Web/Controllers/BffStudentAssignmentsController.cs` (assignment and grades controllers)
- `LMS.Web/Models/BffStudentAssignmentModels.cs`
- `LMS.Web/Services/BffStudentAssignmentApi.cs`
- `LMS.Web/Services/AssignmentApi.cs` (retain validation status, optional upstream HEAD)
- `LMS.Web/Services/AuthoringApi.cs` (retain 413 in validation exception)
- `LMS.Tests/Integration/BffStudentAssignmentsTests.cs`
- `LMS.Tests/Integration/InProcessMvcTests.cs` (optional raw multipart test transport)
- `docs/student-assignments.md`, `docs/lesson-assignments-v1.md`, `docs/organization-reporting-gradebook.md`

## Remaining live verification and later manual checklist

Tests use HTTP fakes/in-process MVC, real handlers with mocked repositories and offline
SQL generation. Live browser uploads/downloads, session/proxy/cookie deployment,
mobile/keyboard/screen-reader/native file/leave behavior, actual database tenant filtering,
transaction locks/races/rollback, query plans/count consistency and private-storage
crash/backup recovery remain unverified. Gradebook counts/offset pages can shift during
concurrent writes. Existing assignment/history lists reuse unpaginated APIs; gradebook
course and assessment pages are bounded/paginated. Existing MP3/MP4/YouTube/PDF viewer
playback/rendering remains unverified. The Karma deprecation warning is unchanged.

1. In a separately authorized disposable environment, review schema/migration and
   durable private-storage prerequisites. Do not assume pending migrations were applied.
2. As Active Student, inspect UTC/local due dates, instructions and state. Submit text,
   PDF, and both; test invalid signatures, empty/oversize/non-PDF/multiple files and
   text limits. Confirm one explicit final action, CSRF enforcement and no retry.
3. Race deadline/publication/enrollment/duplicate submission; check server conflicts.
   Interrupt an accepted upload response, inspect stored state and confirm no replay.
4. Review own snapshots, attachment, graded zero/ungraded feedback after unpublishing
   assignment/lesson/course. Use independent history and My Grades; verify Completed
   reads and blocked mutations, and Cancelled restrictions.
5. Try foreign organization/course/section/lesson/assignment/submission IDs and another
   owner's attachment. Inspect safe headers and absence of tokens, paths and asset IDs.
6. Apply every gradebook filter/bound, page each assessment independently, use empty/
   past-end pages, and check current progress versus historical completion. Verify
   stored attempt snapshots, unavailable quiz links, no overall/best/latest grade.
7. Change organization, logout/expire/revoke while reads, CSRF and uploads are pending.
   Verify stale responses and file/text/feedback are discarded, warnings work, and
   no sensitive data enters browser storage. Recheck catalog, viewer/media, progress,
   quiz, MVC submission/grading and ordinary completion behavior.

## Automated results

Final commands/results follow after final verification.

Final verification on 2026-10-15:

- `npm.cmd run build`: passed, separate lazy assignments and grades chunks.
- `npm.cmd test -- --watch=false --browsers=ChromeHeadless --progress=false`:
  **135 passed**, zero failures. Existing Karma builder deprecation warning remains.
- `dotnet build EnterpriseLMS.slnx --no-restore --no-incremental -m:1 -p:UseSharedCompilation=false`:
  passed, **0 warnings / 0 errors**.
- The following backend regression command passed **601 tests**, zero failures/skips:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-build --no-restore --filter "Category!=SqlServerIntegration&(FullyQualifiedName~Assignment|FullyQualifiedName~Reporting|FullyQualifiedName~Quiz|FullyQualifiedName~BffStudentProgressTests|FullyQualifiedName~BffStudentViewerTests|FullyQualifiedName~BffStudentTests|FullyQualifiedName~BffAuthTests|FullyQualifiedName~BffOrganizationTests|FullyQualifiedName~EnrollmentProgressWebTests|FullyQualifiedName~LearnerWebTests|FullyQualifiedName~InProcessMvcTests|FullyQualifiedName~OrganizationSelectionTests|FullyQualifiedName~LearnerWorkflowPolicyTests|FullyQualifiedName~EnrollmentCancellationTests|FullyQualifiedName~EnrollmentResponseMetadataTests|FullyQualifiedName~TenantHandlerTests|FullyQualifiedName~LessonContentTests|FullyQualifiedName~MediaAssetTests|FullyQualifiedName~CourseCompletionTests|FullyQualifiedName~LearnerWriteTests)" --verbosity minimal
```

- `node --test LMS.Tests/assignments-ui.test.cjs LMS.Tests/quiz-ui.test.cjs`:
  **2 passed**, zero failures.
- `Get-ChildItem -LiteralPath LMS.Web/wwwroot/js -Filter '*.js' | ForEach-Object { node --check $_.FullName }`,
  `node --check LMS.Tests/assignments-ui.test.cjs`, and
  `node --check LMS.Tests/quiz-ui.test.cjs`: passed.
- `git diff --check` in both trees and direct trailing-whitespace/EOF checks on
  new/edited batch files: passed.

New BFF tests exercise real MVC cookies/CSRF and multipart parsing with fake upstream
transport: payloads, confirmation, bad declarations/multiple files, 400/409/413,
ambiguous commits, duplicates, owner/nested/tenant/enrollment denial, unpublishing,
graded zero, download GET/HEAD/headers/MIME, safe projections, report page/owner scope,
bounds/filters, and UTC serialization. Existing assignment handler/storage tests
retain authoritative signature/size/deadline/transaction coverage using mocks; reporting
SQL tests generate/capture SQL offline without opening a database.

Angular tests cover text/PDF/both FormData and CSRF, explicit confirmation/duplicate
guards, validation UX, ambiguous and failed recovery, stale GET/upload/CSRF cleanup,
UTC/local labels, safe work/feedback, historical review, zero/ungraded/missing/expired/
unavailable grades, independent pagination/filter reset, lazy routing and leave guard,
and existing catalog/enrollment/auth/shell/viewer/progress/quiz behavior. The initial
pagination test required removing reliance on ambient router query merging; links now
explicitly preserve the applied query, and the full suite passed.

Branches remain `chore/angular-dependency-upgrade` and
`feature/learning-flow-stability`, with earlier changes preserved. No packages,
schema changes, database access, migrations, normal hosts, staging, commits, pushes
or branch switches occurred. Pending migration state remains unverified.
