# Angular Student catalog and My Courses

The course/section/lesson viewer extension is now documented in [student-viewer.md](student-viewer.md). The batch record below describes the earlier catalog implementation.

This batch extends the existing same-origin BFF session, organization selection,
Secure/HttpOnly cookies and in-memory Angular CSRF flow. API JWTs remain server-side.
No business handlers, publication/membership rules, schema, completion or scoring
rules were changed. Existing MVC routes and hidden-token forms remain operational.

## Inspected sources and supported behavior

- API `CoursesController` / `CourseResponse` and `EnrollmentsController` /
  `GetMyEnrollmentsResponse`; course metadata includes title, description,
  category name, string level, duration in minutes, publication and status.
- `EnrollCourseHandler`: only a current Student can enroll in a published course;
  Active/Completed duplicates conflict. Cancelled records are reactivated through
  the existing enroll operation, preserving the enrollment identity and timestamp.
- `CancelEnrollmentHandler`: authenticated ownership and Active status are checked
  inside its existing transaction; other users are denied. Completed/Cancelled
  records cannot be cancelled. No new publication condition is added to cancellation.
- MVC `LearnController`, `LearnerActionsController`, `Views/Learn/Catalog.cshtml`,
  `Views/Learn/Courses.cshtml`, and `LearnerWorkflowApi`: explicit cancellation and
  re-enrollment confirmation, stored enrollment discovery, current context checks,
  ownership checks, publication filtering and mutation recovery are reused.
- The Student API course repository filters unpublished metadata from discovery.
  My Courses therefore retains an enrollment with `course: null` when its course
  is unavailable. It does not silently discard that enrollment.

## Exact BFF routes

All endpoints require an authenticated Web cookie and selected organization.
Every operation verifies current stored Student access through the existing API
context; SuperAdmin, Admin and Instructor are denied for these Student workflows.
Cached session roles are not used as authority. An unselected account gets 403;
an anonymous/expired account gets sanitized JSON 401 with no HTML redirect.

| Method | Route | JSON request | Success |
| --- | --- | --- | --- |
| GET | `/bff/student/courses` | None | 200 published catalog response below |
| GET | `/bff/student/my-courses` | None | 200 current learner's enrollment response below |
| POST | `/bff/student/courses/{courseId:guid}/enroll` | `{confirmedReenrollment: false}` for initial enrollment; `{confirmedReenrollment: true}` for confirmed reactivation | 204, then read stored state with GET |
| POST | `/bff/student/courses/{courseId:guid}/enrollments/{enrollmentId:guid}/cancel` | `{confirmed: true}` | 204, then read stored state with GET |

Booleans default false if omitted. Re-enrollment and cancellation require true
confirmation. Unknown body properties, including supplied user/organization IDs
or roles, are rejected with 400. Both POSTs use the existing Web antiforgery
cookie and `X-CSRF-TOKEN`. No unrestricted API proxy or completion operation exists.
Browser organization headers are ignored. Scope is derived from the protected
session; upstream requests receive only the server-selected organization header.

Both reads return this JSON shape (GUIDs and timestamps are JSON strings):

```typescript
interface StudentCoursesResponse {
  organizationId: string;
  courses: Array<{
    courseId: string;
    course: {
      id: string;
      title: string;
      description: string;
      categoryName: string | null;
      level: string;
      durationInMinutes: number;
      isAvailable: boolean;
    } | null;
    enrollment: {
      id: string;
      status: 'Active' | 'Completed' | 'Cancelled';
      enrolledAt: string;
      completedAt: string | null;
    } | null;
  }>;
}
```

Catalog includes published courses, whether enrolled or not; an unenrolled row
has `enrollment: null`. My Courses includes only the current learner's stored
Active, Completed and Cancelled enrollments, with no catalog-only rows. Enrollment
is non-null on My Courses. `isAvailable` reflects published metadata/status. No
progress statistics, grade, JWT, user ID or session reference are projected.
Category/level missing values use clear UI fallbacks; duration is the actual API
minute count. No thumbnail or untrusted HTML is loaded.

Reads reuse `LearnerApi.Catalog` or its `LearnerAccess`, `EnrollmentRecords`, and
scoped metadata transport. The Angular My Courses wrapper deliberately does not
call MVC's additional progress aggregation. Enrollment reuses `LearnerApi.Enroll`;
cancellation reuses `ChangeEnrollment(..., "cancel", ...)` and `OwnEnrollment`.
API business handlers retain the final transactional, publication, duplicate,
ownership and tenant checks, including races after BFF reads.

All responses are no-store. Errors use the existing `{code, message, fieldErrors}`
BFF shape. Validation/unconfirmed actions return `400 validation_failed`, CSRF
failure `400 csrf_validation_failed`, inaccessible scope `403 access_denied`,
missing/unavailable IDs `404 not_found`, duplicate/nonactive races `409 conflict`,
rate limits 429, and upstream transport failures 502. Malformed GUID routes return
sanitized 404. Upstream validation messages and diagnostics are not exposed.
The shared API-client validation exception now retains 400 versus 409 while
preserving MVC's existing message/redirect handling. An upstream failure during
cookie validation still removes/rejects the account session, as documented in
[bff-authentication.md](bff-authentication.md); this behavior is unchanged.

## Angular routes and interactions

- `/workspace/student/catalog`: lazy-loaded catalog inside the existing shell.
- `/workspace/student/my-courses`: lazy-loaded My Courses inside the shell.
- `/workspace/student`: Student overview now links to both implemented pages.
- Other roles retain their existing explicit Angular workspace mappings.

Desktop and mobile shell navigation include these two links only for Students.
Student guards control navigation only; backend authorization is authoritative.
Session revalidation retains an allowed Student page rather than forcing overview;
role changes still resolve to the verified role's Angular destination. MVC
`homePath` is never treated as an Angular route.

Cards render course metadata and enrollment status as text with native accessible
buttons and visible focus. Loading, empty, unavailable-course, validation,
conflict and recovery errors are explicit. Cancellation and reactivation use an
inline confirmation fieldset with Confirm and Go back actions. No mutation is
sent until confirmation; Completed re-enrollment and nonactive cancellation are
not offered. Active enrollments can still be cancelled if metadata is unavailable,
consistent with MVC. There are no links to an unimplemented viewer.

Every mutation bootstraps the existing in-memory CSRF token, submits once, and
reads the current page again on success, validation/conflict or ambiguous failure.
Mutations are never automatically replayed. A failed recovery GET clears the list
and offers Reload; pending state is released. Response text is never trusted for
user-visible errors. No credentials, state or tokens enter URLs or browser storage.

Page-scoped data is discarded on navigation/destruction, logout, absolute expiry,
role/organization/session changes and revalidation. Requests capture the verified
session object and a local generation; stale reads/mutation responses cannot
update the new context. GET response organization IDs must match the captured
selection. If organization changes while CSRF bootstrap is pending, the old
mutation is not sent. Shell revalidation also destroys the old routed UI.

## Files changed in this batch

Frontend (earlier pending Angular/auth work is preserved):

- `src/app/app.routes.ts`, `src/app/app.routes.spec.ts`
- `src/app/core/auth/auth.guard.ts`, `auth.service.ts`, `workspace-routing.ts`
- `src/app/layout/application-shell.component.ts`, `application-shell.component.spec.ts`
- `src/app/features/workspace/workspace.component.ts`, `workspace.component.spec.ts`
- `src/app/features/student-courses/student-courses.models.ts`
- `src/app/features/student-courses/student-courses.service.ts`, `student-courses.service.spec.ts`
- `src/app/features/student-courses/student-courses.component.ts`, `student-courses.component.html`, `student-courses.component.spec.ts`
- `src/app/features/student-courses/student-courses.testing.ts`
- `docs/student-courses.md`, `docs/bff-authentication.md`

Backend (other pending work is preserved):

- `LMS.Web/Controllers/BffStudentController.cs`
- `LMS.Web/Models/BffStudentModels.cs`, `LMS.Web/Models/LearnerModels.cs`
- `LMS.Web/Services/BffStudentApi.cs`, `AuthoringApi.cs`, `BffJson.cs`
- `LMS.Tests/Integration/BffStudentTests.cs`, `InProcessMvcTests.cs`
- `docs/student-courses.md`, `docs/bff-authentication.md`

## Verification and remaining gaps

Final automated results are recorded below. Tests use fake browser HTTP,
in-process MVC hosts with ephemeral protection/fake upstreams, and mocked business
repositories. No normal hosts, existing databases, migrations or seeds are used.
No packages, branch changes, staging, commits or pushes are part of this batch.

Live HTTPS trust/proxy/shared-cookie behavior, real Identity/security stamps,
real course publication/enrollment races and tenant filtering against SQL, and
visual/focus behavior across real device viewport sizes remain unverified.
The course/section/lesson viewer and production SPA hosting remain outside scope.

Automated results for this batch:

- `npm.cmd run build`: passed, production output with a lazy Student pages chunk.
- `npm.cmd test -- --watch=false --browsers=ChromeHeadless`: **64 passed**, exit 0,
  including existing authentication, shell and role-routing tests. The existing
  legacy Karma builder deprecation warning remains; no dependencies changed.
- `dotnet build EnterpriseLMS.slnx --no-restore --no-incremental -m:1 -p:UseSharedCompilation=false`:
  passed, **0 warnings and 0 errors**, after the final backend test changes.
- `dotnet test LMS.Tests/LMS.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~BffStudentTests|FullyQualifiedName~BffAuthTests|FullyQualifiedName~BffOrganizationTests|FullyQualifiedName~EnrollmentProgressWebTests|FullyQualifiedName~LearnerWebTests|FullyQualifiedName~InProcessMvcTests|FullyQualifiedName~OrganizationSelectionTests" --verbosity minimal`:
  **131 passed**, no failures/skips.
- `dotnet test LMS.Tests/LMS.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LearnerWorkflowPolicyTests|FullyQualifiedName~EnrollmentCancellationTests|FullyQualifiedName~EnrollmentResponseMetadataTests|FullyQualifiedName~TenantHandlerTests" --verbosity minimal`:
  **50 passed**, no failures/skips. These disjoint runs total **181 backend tests**.
- `git diff --check`: passed in both working trees. New batch files were also
  checked for trailing whitespace and extra EOF blank lines.

No requested automated check was blocked. Database integration suites were not
run because this batch expressly excludes database access. Branches remain
`chore/angular-dependency-upgrade` (Angular) and `feature/learning-flow-stability`
(backend), with earlier uncommitted changes preserved.
