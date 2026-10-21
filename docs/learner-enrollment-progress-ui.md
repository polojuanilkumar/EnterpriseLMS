# Learner enrollment and progress UI

This batch extends the existing learner and quiz pages in the current branch/working
tree. Existing changes, LMS.Tests and prior quiz/staff/media behavior were preserved.
No database connection, migration, dependency restore/download, browser/Swagger manual
test, branch switch, staging, commit or push was performed.

## Functionality and reused policy

The published catalog is separate from My Courses. Catalog discovery calls only the
organization context, course metadata list and authenticated learner's enrollment list;
it never requests course progress, sections, lessons, quizzes or content before enrollment.
My Courses retains Active, Completed and Cancelled records, even when course metadata is
unavailable/unpublished. Only published courses with Active/Completed enrollment have
content-navigation links. Active rows offer explicitly confirmed cancellation.

Enrollment uses the existing course-enroll API. `EnrollCourseHandler` already explicitly
reactivates a Cancelled record, so the catalog offers confirmed **Re-enroll** for Cancelled
enrollment in a published course. It calls the same enroll endpoint and preserves the
record ID/history according to the existing handler; this batch introduces no reactivation
endpoint, historical deletion, or domain behavior. Active/Completed duplicates remain
conflicts, including races. Conflicts are displayed safely without automatic retry.

The existing course page displays current published-lesson totals, completed count,
percentage and completion eligibility. Historical enrollment status/completion timestamp
are labelled separately. My Courses also distinguishes stored enrollment percentage
from current published-content progress. A historically Completed enrollment may have
current 50% or 0% progress after publication changes; the UI does not infer or overwrite
historical completion from those values, or manufacture legacy completion timestamps.

Lesson pages display the stored lesson percentage, completion state, StartedAt,
CompletedAt and LastAccessedAt. Missing progress shows an explicit Start lesson form.
Started, incomplete lessons expose manual percentage update and Complete lesson forms.
Saving 100% explicitly invokes the existing rule that completes the lesson. Completed
lessons retain their stored presentation; the UI does not offer redundant completion.
The API remains authoritative for crafted/repeated requests and concurrent status changes.

GETs perform reads only. Opening a lesson, loading/playing media, ending playback,
opening quiz instructions or fetching media does not start/update/complete progress or
touch LastAccessedAt. No media tracking JavaScript was added.

Confirmed enrollment completion calls the existing guarded endpoint. Eligibility is
displayed only for Active enrollment with at least one published lesson and every
published lesson complete. The endpoint independently rechecks publication, ownership,
Active state and all published lesson prerequisites inside its existing transaction.
Zero published lessons remains 0% and ineligible. Unpublished unfinished lessons do not
block completion. No quiz-pass or required-content-item prerequisite was introduced.
Completed learners retain existing permitted lesson, progress, quiz and media reads;
mutations remain Active-only in the API.

## Exact web routes

All IDs in these routes have GUID constraints. Define:

- `B = /learn/{organizationId}/courses`
- `C = B/{courseId}`
- `U = C/sections/{sectionId}/units/{lessonId}`

| Method and route | Behavior / form fields |
| --- | --- |
| `GET B` | My Courses, all stored enrollment states, current aggregate progress |
| `GET B/catalog` | Published metadata catalog and enrollment/re-enrollment forms |
| `POST C/enroll` | Enroll; `confirmedReenrollment=true` required when current enrollment is Cancelled |
| `POST C/enrollments/{enrollmentId}/cancel` | Cancel; `confirmed=true` required |
| `GET C` | Existing sections page plus current/historical progress and eligibility |
| `POST C/enrollments/{enrollmentId}/complete` | Guarded enrollment completion; `confirmed=true` required |
| `GET U` | Existing lesson/quiz/media page plus stored lesson progress and course progress |
| `POST U/progress/start` | Explicit lesson start |
| `POST U/progress/update` | Explicit update; `Form.ProgressPercentage`, decimal 0–100 |
| `POST U/progress/complete` | Explicit lesson completion |
| `GET B/recovery` | Read-only validation/conflict/ambiguous-failure recovery with inspection links |

Every POST requires antiforgery. Successful changes use POST/Redirect/GET. Enrollment
success returns to catalog to show the new state; cancellation returns to My Courses;
course completion returns to the course page; lesson changes return to the lesson page.

Existing section, quiz and media routes remain unchanged. See
[quiz UI routes](quiz-ui-batch.md), [original learner/media routes](learner-web.md),
and [staff routes](staff-web.md).

## Exact reused API routes and contracts

| Method and route | Use |
| --- | --- |
| `GET /api/organizations/{organizationId}/context` | Revalidate selected learner membership/role |
| `GET /api/Courses` | Organization-scoped course metadata; catalog filters IsPublished |
| `GET /api/Enrollments/me` | Caller-owned enrollment states, IDs and historical values |
| `GET /api/Enrollments/{id}` | Confirm stored enrollment ID, CourseId and authenticated UserId |
| `POST /api/Courses/{courseId}/enroll` | Existing enroll/reactivate behavior and duplicate conflicts |
| `PATCH /api/Enrollments/{id}/cancel` | Existing Active-only cancellation |
| `PATCH /api/Enrollments/{id}/complete` | Existing guarded course completion |
| `GET /api/Enrollments/my-courses/progress` | Current published-lesson aggregate counts for enrolled courses |
| `GET /api/Courses/{id}/progress` | Authorized current progress and historical enrollmentStatus/enrollmentCompletedAt |
| `GET /api/Lessons/{id}/progress` | Stored owner progress; absent record returns 404 |
| `POST /api/Lessons/{id}/start` | Start/explicit access update supported by the existing API |
| `PATCH /api/Lessons/{id}/progress` | Existing decimal progressPercentage update |
| `PATCH /api/Lessons/{id}/complete` | Existing explicit lesson completion |

Existing course → section → lesson → content reads continue to validate parent IDs and
publication through the existing learner service. Existing quiz APIs and private media
proxy are unchanged. No essential API gap was found: **no API, schema or migration change
was needed**. Web-specific models deserialize the existing envelopes and field names.

## Security and recovery

The web route organization must match the protected server session. Current organization
context must authorize Student access. Catalog/My Courses calls remain organization
scoped; `/me` is server-filtered to the authenticated user. Enrollment mutations also
read owner detail, validate UserId and stored CourseId, and never accept a user ID from
the form. Lesson actions reload the stored parent hierarchy and validate progress UserId
and LessonId. Current course progress must match both requested CourseId and caller.
API organization middleware, filtered repositories, ownership checks and Active-only
guards are unchanged and independently protect the actual mutations.

All new mutation forms carry antiforgery tokens; `LearnerActionsController` has explicit
AutoValidateAntiforgeryToken as well as the existing global filter. Author text and API
validation/conflict messages are Razor encoded. Existing bounded 400/409 validation
message handling is reused. Unauthorized, missing and expired-session errors continue
through the existing sanitized security middleware; token expiry still revokes the session.
JWTs remain protected server-side and never enter HTML, URLs, storage or logs.

Ambiguous HTTP/network/time-out/502 failures redirect to a dedicated GET recovery page.
It contains no modifying form and never retries anything. Its fixed notice asks the learner
to inspect stored enrollment/progress before deciding on a deliberate retry. Validation
and conflicts also use this read-only page. TempData carries only a bounded notice and
a fixed organization-local inspection path, never tokens or answer payloads. This also
prevents browser refresh of the recovery page from replaying the POST. Inspecting state
is a GET; any later retry requires a fresh, deliberate form submission.

## Files changed by this batch

| File | Change |
| --- | --- |
| `LMS.Web/Controllers/LearnController.cs` | Catalog/My Courses and progress integration with existing quiz/content pages |
| `LMS.Web/Controllers/LearnerActionsController.cs` | Explicit mutations, antiforgery, confirmation, PRG and recovery |
| `LMS.Web/Models/LearnerModels.cs` | Optional progress panels on existing course/lesson models |
| `LMS.Web/Models/LearnerWorkflowModels.cs` | Existing-contract enrollment/progress models and UI forms/pages |
| `LMS.Web/Services/LearnerWorkflowApi.cs` | API integration, selected learner context and stored parent/owner validation |
| `LMS.Web/Views/Learn/Catalog.cshtml` | Published catalog and enrollment forms |
| `LMS.Web/Views/Learn/Courses.cshtml` | All enrollment states with current/historical distinction and cancellation |
| `LMS.Web/Views/Learn/Sections.cshtml` | Course progress, eligibility and confirmed enrollment completion |
| `LMS.Web/Views/Learn/Unit.cshtml` | Stored lesson progress alongside preserved quiz/media/content |
| `LMS.Web/Views/Learn/_CourseProgress.cshtml` | Current/historical progress and prerequisite explanation |
| `LMS.Web/Views/Learn/_LessonProgress.cshtml` | Stored timestamps and explicit lesson actions |
| `LMS.Web/Views/LearnerActions/Recovery.cshtml` | Safe, read-only failure recovery |
| `LMS.Tests/EnrollmentProgressWebTests.cs` | Catalog/state/filtering, routes, confirmation, recovery, scope, owner/parent checks, rendering and antiforgery |
| `LMS.Tests/LearnerWorkflowPolicyTests.cs` | Real enroll/progress/completion handler contracts, historical state and read-only timestamp checks |
| `LMS.Tests/LearnerWebTests.cs` | Existing fake progress endpoint now returns the full production contract for enriched lesson-page regression tests |
| `docs/learner-enrollment-progress-ui.md` | This route/verification/limitations report and consolidated later checklist |
| `docs/learner-web.md` | Link to this report as the current enrollment/progress UI extension |

No existing test project/dependencies were recreated or removed. All earlier test files,
including quiz, staff, authorization, media and completion tests, remain in the suite.

## Automated verification

Used cached dependencies and existing restore assets, with a separate output directory:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/learner-workflow/bin/'
dotnet build EnterpriseLMS.slnx --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/learner-workflow/bin/'
Get-ChildItem -LiteralPath LMS.Web/wwwroot/js -Filter '*.js' | ForEach-Object { node --check $_.FullName }
node --check LMS.Tests/quiz-ui.test.cjs
node --test LMS.Tests/quiz-ui.test.cjs
git diff --check
```

Final automated results:

| Check | Result |
| --- | --- |
| Full existing LMS.Tests .NET suite | **614 passed, 0 failed, 0 skipped** (54 new cases) |
| Full solution build with `--no-restore`, separate OutputPath | **Succeeded; 0 warnings, 0 errors** |
| Syntax checks for all four web JavaScript files and existing Node test | **Passed** |
| Existing quiz JavaScript behavior regression test | **1 passed, 0 failed** |
| `git diff --check` | **Passed**, line-ending conversion notices only |
| Additional no-index whitespace checks for new/untracked batch files | **No whitespace errors** |

New tests use fake HttpMessageHandlers,
direct controllers, compiled Razor rendering, the real antiforgery service and actual
application/domain handlers with repository mocks. Existing tenant tests use EF filters
and offline SQL command generation without opening a database. No API host/seeder was run.
These are automated unit/contract checks, not browser or real-database integration tests.

## Limitations and unverified behavior

- No automatic progress/media tracking, enrollment deletion, new reactivation policy,
  token refresh, or completion-policy changes were added.
- The API does not offer a batch lesson-progress endpoint; progress is displayed on each
  lesson and course aggregate on course/My Courses pages. Reads are sequential and may
  observe a publication/status change between calls. The final API mutation independently
  revalidates its existing rules; the UI eligibility display is not authorization.
- Unpublished/missing course content remains inaccessible under the existing learner read
  gates. My Courses retains the enrollment state and any available aggregate metadata.
  Duplicate/invalid enrollment-list identities fail safely rather than choosing an arbitrary
  historical record; no database repair is attempted.
- Recovery intentionally does not preserve/replay a submitted mutation. The learner must
  inspect stored state before deciding whether to make a new request.
- Browser layout/keyboard interaction, deployment redirects/cookies/TLS, actual API
  transport, media/PDF playback, SQL uniqueness, locking, concurrent mutations and rollback
  remain unverified manually. No browser/Swagger/database tests were run.

## One consolidated manual checklist for later (not executed)

Use a separately authorized disposable environment with learners in organizations A/B,
another learner's enrollments, Active/Completed/Cancelled records, published/draft courses,
and published/draft lessons. Setup and service startup may access a database and require
separate authorization; this batch did not perform them.

1. Sign in and explicitly select A. Open My Courses and Catalog using their navigation links.
   Catalog must show only published metadata and enrollment states, including un-enrolled
   courses, without requesting sections/lessons/content. My Courses must retain Active,
   Completed and Cancelled records and historical timestamps, even for unpublished courses.
2. Enroll once with antiforgery: confirm redirect to catalog and new Active state. Replay a
   duplicate request and race two deliberate requests: verify controlled conflict and no
   automatic retries. Real SQL uniqueness/concurrency results require authorized integration
   evidence rather than unit-test inference.
3. Cancel an Active enrollment: unchecked confirmation must prevent mutation; checked
   confirmation must cancel and return to My Courses. Completed/Cancelled cancellation
   must be rejected by the API. Confirm the historical record was retained. For a Cancelled
   record in a published course, explicitly confirm Re-enroll and verify the existing API
   reuses the same enrollment ID without deleting or creating a historical replacement.
4. Open an Active course/lesson. Before starting, progress should be absent. Open/refresh
   pages and play/load/end media: compare stored LastAccessedAt and progress before/after
   and verify no automatic mutations. Explicitly Start, update a percentage, then Complete;
   verify POST/Redirect/GET and the stored percentage/timestamps. Saving 100% must clearly
   indicate the existing lesson-completion effect.
5. Check course totals with published and draft lessons, missing/partial progress, and zero
   published lessons. Only published lessons count; zero remains 0%/ineligible. Complete
   every published lesson, explicitly confirm enrollment completion and verify its recorded
   timestamp. Repeat completion: conflict and unchanged historical timestamp. Quiz failure
   or required-content-item metadata must not add a completion prerequisite.
6. With Completed enrollment, verify permitted course/lesson/progress/quiz/media reads,
   no mutation controls, and API denial of crafted start/update/complete requests. Change
   current published lessons through separately authorized setup: verify current 50%/0%
   stays visibly distinct from historical Completed status, stored 100% and completion time.
   A null legacy completion timestamp must remain "Not recorded".
7. Substitute foreign organization, course, section, lesson and enrollment IDs; try another
   user's enrollment and forged user fields. Verify no mutation or protected disclosure.
   Revoke organization membership/change role and expire the session; verify existing
   sanitized denials/sign-in guidance. Switching to B must reject old A page/action URLs.
8. Remove/forge antiforgery on every new POST and verify rejection. Enter script-like titles,
   descriptions and validation messages in disposable fixtures: confirm encoded rendering.
   Inspect URLs/browser storage/HTML/logs for absence of JWTs or sensitive payloads.
9. Interrupt enrollment/cancellation/progress/completion responses after acceptance may have
   occurred. Verify GET recovery, no mutation form, no refresh replay and no automatic retry.
   Follow the inspection link, verify actual stored state, then make any retry deliberately.
10. Recheck existing learner quiz start/resume/submission/results, historical attempt access,
    staff authoring/readiness, private audio/video/PDF proxy and legacy lesson presentation.
    No newly introduced GET, media load, playback event or quiz view should update progress.

SQL transaction races/rollback and uniqueness enforcement remain separate integration work;
see the existing offline boundary in `LMS.Tests/README.md` and tenant/completion reports.
