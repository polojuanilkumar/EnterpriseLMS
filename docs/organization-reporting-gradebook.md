# Organization reporting and learner gradebook

The Angular organization administration, reports and audit extension is documented in [organization-administration.md](organization-administration.md). This earlier record remains as policy and implementation history.

The Angular Student assignments/history/My Grades and BFF extension is documented in [student-assignments.md](student-assignments.md). This earlier record remains as policy and implementation history.

This batch adds read-only reports and My grades in the current branch/working tree. Existing uncommitted changes and `LMS.Tests` were preserved. No applications were started; no database connections, migration application, browser/Swagger testing, staging, commits, pushes, or branch switches were performed.

## Migration requirement

No new entity, schema, index, or migration was needed for reporting. **The pending `20261004061629_AddLessonAssignments` migration and its preceding migration chain are required before these queries can execute against a deployment database. This application is not compatible with a database lacking the assignment tables.** The assignment migration remains unapplied. Its offline review, policies, storage limitations, and migration guard are documented in [lesson-assignments-v1.md](lesson-assignments-v1.md). Existing enrollment uniqueness/indexes, quiz attempt snapshots, assignment snapshots, and grade/submission indexes are reused.

## Exact API routes

All routes are authenticated, marked `OrganizationScope`, and require the selected `X-Organization-Id`. Existing middleware verifies the active organization, stored membership role/platform privilege, and replaces global JWT organization-role claims before authorization. Handlers also require the verified scoped `OrganizationContext`. No route accepts a learner/user ID for My grades.

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/Reports/courses` | Paginated course summaries, including unpublished courses |
| GET | `/api/Reports/courses/{courseId:guid}/learners` | Authorized course reference plus paginated enrollment/learner details |
| GET | `/api/Gradebook/courses` | Authenticated Student's Active/Completed enrolled courses, including historical unpublished courses |
| GET | `/api/Gradebook/courses/{courseId:guid}` | Own course/enrollment/current-progress header |
| GET | `/api/Gradebook/courses/{courseId:guid}/quizzes` | Own individual stored attempts, plus published quizzes without any own attempt |
| GET | `/api/Gradebook/courses/{courseId:guid}/assignments` | Own submission/grade/feedback or accessible published assignments not yet submitted |

Responses use the existing `ApiResponse<T>` envelope. Pages contain `organizationId`, `ownerUserId` (own Student ID for gradebook; null for staff reports), `page`, `pageSize`, `total`, `pages`, and bounded `items`. Course IDs and navigational assessment parents come from stored joins, not caller-supplied ownership fields. Handler/web checks reject mismatched course, organization, owner, and Instructor scope in returned contracts.

Invalid filters return 400. Invalid/expired JWTs return 401. Verified-role/organization/ownership failures return 403. A guessed foreign, unassigned, missing, or inaccessible course returns 404 without disclosing counts. An empty or past-end permitted page returns 200 with no items. No new mutation endpoints exist.

## Exact MVC routes and navigation

| Method | Route | Page |
| --- | --- | --- |
| GET | `/staff/{organizationId:guid}/reports/courses` | Organization/assigned-course reports |
| GET | `/staff/{organizationId:guid}/reports/courses/{courseId:guid}/learners` | Paginated learner details |
| GET | `/learn/{organizationId:guid}/grades` | My grades course list |
| GET | `/learn/{organizationId:guid}/grades/courses/{courseId:guid}` | Quiz attempts and assignment grades, with independent pagination |

Staff Courses and the Admin home link to reports. My Courses links to My grades and retains assignment-history navigation. Existing learner/staff/quiz/media navigation remains in place.

Staff learner rows link their most recent assignment submission to the existing authorized review route:

`/staff/{organizationId}/courses/{courseId}/sections/{sectionId}/lessons/{lessonId}/assignments/{assignmentId}/submissions/{submissionId}`.

This is a navigation shortcut, **not** a selected/official grade; all submission counts remain present. Other work can be reviewed through the existing course → section → lesson → assignment pages. Existing quiz detailed result APIs/pages are owner-only; no staff answer-key/review endpoint or bypass was added.

Learner rows use the existing owner review route:

`/learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}/assignments/{assignmentId}/submissions/{submissionId}`.

Published quiz parents link to the existing learner quiz route, appending `/attempts/{attemptId}` for individual attempts. If the course/lesson/quiz is unpublished, the existing quiz MVC page cannot safely render it, so the gradebook displays stored outcomes and explicitly marks detailed navigation unavailable. It does not weaken the existing lesson/quiz viewer's publication gate. An unsubmitted published assignment links to its existing instructions page. All linked destinations independently authorize fresh requests.

The URL organization must match the selected protected server-side session; every API call carries the JWT only in a server-side Authorization header. Each page verifies current organization access. Role changes, forbidden resources, expired sessions, network failures, and malformed responses use the existing safe web handling. Razor encodes titles, names, search values, and grade feedback. Existing private/no-store, nosniff, and referrer protections remain in use. There is no browser storage, new JavaScript, token-in-URL behavior, or new mutation form. Filter forms use GET. No GET changes `LastAccessedAt`, progress, enrollment, attempts, submissions, or grades.

## Access matrix

| Operation | Student | Instructor | Admin | SuperAdmin |
| --- | --- | --- | --- | --- |
| Course/learner reports | Denied | Stored assigned courses in selected org only | Selected org only | Explicitly selected org only |
| Own course gradebook | Own Active/Completed enrollment | Denied | Denied | Denied |
| Own quiz attempt rows | Same, including stored historical attempts after unpublishing | Denied | Denied | Denied |
| Assignment grades/feedback | Own work, Active/Completed; historical work after unpublishing | Use existing authorized staff review | Same within org | Same within explicit org |

Instructor ownership filtering is in `ReportingQueries.Courses(true)` and is reused for headers, totals, enrollment details, assessment aggregates, and review-link projections. It is not only a UI/handler filter. All child queries resolve stored course parents; totals use the same scoped/filtering source as page rows. Organization/global model filters remain enabled.

Cancelled enrollment is excluded from every learner gradebook query, including direct assessment routes. Historical work is not deleted. Staff reports retain Cancelled enrollment and its stored assessment history within authorized courses. Subject membership/identity changes do not erase historical enrollment/report records; the **acting** user's current verified role controls access. Re-enrollment/reactivation behavior, if explicitly supported by the existing enrollment API, is unchanged. Missing identity names are displayed as empty names with the necessary learner ID; no email, phone, employee profile, credentials, password hashes, security stamps, attachment IDs, storage paths, submitted text, instructions snapshots, or detailed answers are projected into staff reports.

## Pagination and documented filters

- Default `page=1`, `pageSize=20`; accepted page 1–10000 and page size 1–50. Invalid limits are rejected rather than silently expanded. Maximum offset is bounded below 500,000. Pages beyond available results remain empty; totals are still scoped. Large data beyond the page ceiling needs narrower filters or a later paging design.
- Staff course summaries, staff learner details, and gradebook course lists accept `search` (up to 100 characters), `published` (true/false/omitted), and `enrollmentStatus` (staff 1 Active, 2 Completed, 3 Cancelled; learner lists 1/2 only).
- Course-list search is a trimmed course-title substring. Learner-detail search is a trimmed first-name **or** last-name substring, not a global user search. Empty/whitespace search is ignored. Database collation controls case/accent behavior; no alternate SQL collation is introduced.
- Staff course `enrollmentStatus` selects courses that have at least one such enrollment; summary counters still include **all** stored enrollments/history in each selected course. Learner-detail `enrollmentStatus` selects the displayed enrollment rows. Learner course filtering selects the authenticated Student's own state. `published` on learner details filters the stored course's publication, not assessment history.
- Quiz API pagination adds `status=0` (no own attempt), 1 (stored Started), 2 (Submitted), or 3 (Expired); omitted means all. Assignment API adds `status=0` (not submitted), 1 (submitted/ungraded), or 2 (graded); 3 is rejected.
- The MVC course gradebook accepts `quizPage=1`, `assignmentPage=1`, common `pageSize=20`, `quizStatus`, and `assignmentStatus`. Applying filters resets both to page 1. Pagination preserves the other section's page/filter.
- Deterministic order: courses by current title then CourseId; learners by current last name, first name, UserId; quiz rows by current title, QuizId, attempt number, AttemptId; assignments by current title, AssignmentId. Publication/title edits can shift offset pages during concurrent navigation.

## Report formulas and counting rules

All counts use database projections/correlated aggregations, with no materialization of organization-wide entities/history. Only bounded DTO rows are returned. Name lookups and the recent-review link are SQL subqueries inside the page query, not additional network calls per learner. Per endpoint, the repository runs a count query and a bounded data query; course details also have a small authorized header query. The MVC learner assessment page uses one context access request and three gradebook requests (header, quiz page, assignment page), independent of row count. No per-row API calls are made.

| Field | Definition / denominator |
| --- | --- |
| Course enrollment total | Stored course enrollment rows; known-state subtotals count 1/2/3 separately |
| Published lessons | Currently `IsPublished` lessons under the stored course/sections, regardless of the course's current publication; matches existing My Courses counting |
| Learner completed published lessons | Count published lessons having at least one matching completed progress row for that enrollment user; duplicate progress rows cannot inflate it |
| Learner current percentage | `Round(completedPublishedLessons / publishedLessons * 100, 2)`; zero lessons gives 0% |
| Current content complete | At least one published lesson and completed count equals total; not a historical enrollment state or quiz/assignment prerequisite |
| Course aggregate progress numerator | Distinct completed `(course, enrolled user, currently published lesson)` pairs, across all stored enrollment states including Cancelled |
| Course aggregate progress denominator | `enrollment row count × published lesson count` (64-bit product); zero denominator gives 0%. Percentage uses the same two-decimal formula |
| Historical enrollment status/completion | Stored enrollment status, `CompletedAt`, and stored enrollment percentage; never inferred or overwritten from current progress/grades |
| Quiz Started/Submitted/Expired | All stored course attempts with that status, including unpublished history. No status transition on GET |
| Quiz Passed/Failed | Submitted attempt with non-null stored earned marks and `IsPassed=true/false`; no threshold recomputation |
| Quiz unavailable result | Submitted attempt missing earned marks or passing outcome; included in Submitted count but neither Passed nor Failed |
| Assignment count | All stored lesson assignments under the course, including unpublished definitions |
| Submission count | All stored final course submissions, including unpublished and Cancelled historical work for staff |
| Graded/Ungraded | Presence/absence of a grade row for that submission; marks of zero count as graded |
| Recent assignment review | Latest stored submission time, then stable submission ID tie-break; navigation only, no grade aggregation |

Course assessment totals include all stored course work. Learner-detail assessment counts are restricted to each displayed enrollment user, so course totals need not equal sums of a filtered/paginated learner page. Progress uses enrollment/lesson pairs; assessment totals never become a completion prerequisite.

## Learner gradebook values and snapshots

- Each existing quiz attempt is its own row. Started/submitted/deadline dates come from the stored attempt. An overdue Started row stays Started and gets a separate “deadline passed” label using server UTC (`now > deadline`, matching the existing quiz boundary). Expired is the stored Expired state. Nothing is expired or resubmitted by reading a gradebook.
- Quiz earned marks, possible marks, passing percentage, and passing outcome are stored attempt/result fields. Only Submitted attempts show result marks/passing outcome. Missing results are labelled unavailable. No live question/options/answer tables or current quiz threshold are used to recalculate a grade.
- A currently published course/lesson/quiz with no **own** attempt gets one “Not attempted” row. Unpublished never-attempted definitions are hidden. Such rows have no invented maximum marks, threshold, grade, or date.
- Assignments with an own submission remain visible after any parent unpublishes. Published definitions without an own submission show “Not submitted”; another Student's submission does not change that status or reveal their work. Submitted/ungraded and graded zero are separate cases.
- A submitted assignment's maximum marks come from `MaximumMarksSnapshot`; grade/feedback/date come from the immutable grade record. For a never-submitted published assignment, the UI clearly labels maximum marks as the **current definition**. It does not invent feedback/grade.
- Course/assessment titles and learner names are current metadata: historical title/name snapshots do not exist. Stored grades, thresholds, maxima, feedback, and dates are not reconstructed from edited definitions.
- There is no overall course grade, weighting, best/latest official attempt selection, pass requirement, CSV export, notification, or certificate. Course completion and current progress are displayed separately.

## Consistency and remaining risks

These are read-only queries with the existing SQL Server provider/default isolation, not a reporting snapshot transaction. The count and row statements, course header, and different assessment pages can observe different committed versions. Unless the deployment independently supplies statement snapshot isolation, even correlated counters within a statement can reflect concurrent changes. A request does not promise an atomically reconciled partition or enrollment/publication/grade snapshot. Refresh if counts disagree; the report does not repair records, clamp counts to hide changes, or mutate statuses. Authorization/root scope is applied on each query; membership is verified at request entry using the existing middleware.

Queries use existing indexes and parameterized expressions. SQL translation is verified offline, but no real SQL execution, query plans, latency/load profiles, isolation behavior, or deployment compatibility was tested. Wide courses can make correlated counts expensive despite bounded rows; assess real plans and index needs before large deployments. No speculative schema/index migration was added without evidence.

Existing unpublished quiz MVC details remain unavailable; the gradebook safely shows authorized own stored outcomes without opening that viewer. Staff quiz answer review remains owner-only in the existing API and is not broadened. Private assignment download/storage/crash limitations remain as documented in the assignment report. Nothing here changes completion, scoring, grading, publication, enrollment, or private media policy.

## Changed files

New:

- `LMS.Application/Features/Reporting/ReportingContracts.cs`
- `LMS.Application/Features/Reporting/ReportingHandlers.cs`
- `LMS.Infrastructure/Repositories/ReportingQueries.cs`
- `LMS.Infrastructure/Repositories/ReportingRepository.cs`
- `LMS.Api/Controllers/ReportsController.cs`
- `LMS.Api/Controllers/GradebookController.cs`
- `LMS.Web/Models/ReportingModels.cs`
- `LMS.Web/Services/ReportingApi.cs`
- `LMS.Web/Controllers/ReportsController.cs`
- `LMS.Web/Controllers/GradebookController.cs`
- `LMS.Web/Views/Reports/_Filters.cshtml`
- `LMS.Web/Views/Reports/Index.cshtml`
- `LMS.Web/Views/Reports/Learners.cshtml`
- `LMS.Web/Views/Gradebook/Index.cshtml`
- `LMS.Web/Views/Gradebook/Course.cshtml`
- `LMS.Tests/ReportingPolicyTests.cs`
- `LMS.Tests/ReportingSqlTests.cs`
- `LMS.Tests/ReportingWebTests.cs`
- `docs/organization-reporting-gradebook.md`

Updated:

- `LMS.Api/Program.cs`: scoped reporting handler/repository registration.
- `LMS.Web/Views/Staff/Courses.cshtml`: reports navigation.
- `LMS.Web/Views/Admin/Index.cshtml`: reports navigation.
- `LMS.Web/Views/Learn/Courses.cshtml`: My grades navigation.
- `LMS.Tests/LMS.Tests.csproj`: compile the actual new API controllers in the existing isolated tests.

Existing infrastructure, session, organization middleware, storage, entities, mappings, and migration files were not replaced or changed by this batch.

## Automated verification

Using cached dependencies and separate output under `artifacts/reporting/bin`:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/reporting/bin/'
dotnet build EnterpriseLMS.slnx --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/reporting/bin/'
node --test LMS.Tests/quiz-ui.test.cjs LMS.Tests/assignments-ui.test.cjs
git diff --check
```

- Full suite: **732 passed, 0 failed, 0 skipped**; 39 new reporting tests, with the prior 693 tests retained.
- Full solution build: **0 warnings, 0 errors**.
- JavaScript syntax checks for all five existing application scripts and both existing Node behavior tests: passed. No new JS was necessary.
- `git diff --check` and supplementary CRLF-aware no-index whitespace checks for all 24 changed reporting files: passed.
- Offline EF pending-model check: no pending model changes; existing assignment migration still required and unapplied.
- Tests cover two-organization/mixed-role projections, Instructor query filtering, Admin/SuperAdmin boundaries, empty/foreign courses, pagination and filters, own versus other-user/cancelled grades, current versus historical progress, quiz/assignment snapshots and states, stored review parents, read-only entity values, compiled Razor encoding/navigation/statuses, protected sessions, GET-only routes, fixed API call counts, role-claim replacement, and actual framework query binding defaults.
- Production query translation is tested with the SQL Server provider and the existing offline connection stub/interceptors. Tests generate SQL and assert tenant parameters/paging/minimal selected fields/no tracked entities. Policy counting tests run the **same production projection expressions** over in-memory sources. Neither verifies actual SQL Server execution or query performance. No API host/seeder or web server was started.

EF checking uses the existing strictly offline design-time factory and cached tool, Infrastructure startup assembly, and separate runtime/deps files (it does not run the API application):

```powershell
dotnet exec --depsfile 'artifacts/reporting/bin/LMS.Api.deps.json' --runtimeconfig 'artifacts/reporting/bin/LMS.Api.runtimeconfig.json' 'C:/Users/poloj/.dotnet/tools/.store/dotnet-ef/10.0.11/dotnet-ef/10.0.11/tools/net8.0/any/tools/net8.0/any/ef.dll' migrations has-pending-model-changes --assembly 'artifacts/reporting/bin/LMS.Infrastructure.dll' --startup-assembly 'artifacts/reporting/bin/LMS.Infrastructure.dll' --context LMSDbContext -- --offline
```

Browser behavior, live HTTP/JWT/session deployment behavior, SQL Server execution/count consistency/query plans/performance, and migration deployment remain **unverified**.

## Consolidated manual checklist for later execution

1. In a disposable authorized SQL Server environment, separately review/apply the full required migration chain including pending assignments. Verify the report queries against actual schema; do not deploy this batch to a database missing assignment tables. Check representative query plans/index usage and load/latency on large courses.
2. Create two organizations with the same user holding different roles, multiple assigned/unassigned courses, and Active/Completed/Cancelled enrollments. Verify Admin selected-org scope, Instructor repository filtering, explicit SuperAdmin selection, forged global roles, revoked membership, reassignment, cross-org course IDs, direct forged gradebook IDs, and non-owner feedback access.
3. Check empty and past-end pages, default and boundary page sizes, invalid filters/types, search/collation behavior, publication and enrollment-state filters, stable tie-breaks, and preservation of filters/independent quiz/assignment pages.
4. Compare course counts to stored enrollments and assessments, including unpublished history, graded zero, ungraded work, each quiz status, nullable submitted results, and cancelled former learners. Verify current published-lesson numerators/denominators, duplicate progress immunity, zero-lesson behavior, and historical completion independently from current progress/grades.
5. As learners, verify each attempt is listed independently with stored maxima/thresholds/outcomes/dates; no best/latest or overall official grade is selected. Verify not-attempted, overdue Started, stored Expired, unavailable result, not-submitted, ungraded, and graded-zero labels. Confirm reads never expire attempts or update `LastAccessedAt`.
6. Unpublish assignment/lesson/course, and check Active/Completed own historical grades and review links; verify Cancelled learner access denial with staff history retained. Check unavailable quiz detail notices and all existing assignment/result review authorization at link destinations. Confirm most-recent staff review is a navigation shortcut only.
7. Render script-like course/assessment names, learner names, search text, and feedback; check encoding and that responses/HTML/logs/storage never expose other learners' work, credentials, tokens, detailed answer keys, attachment paths/IDs, or submitted text in reports. Check expired sessions and API/network errors safely.
8. Change publication, enrollments, progress, titles, and grading during report pagination. Characterize actual isolation/count skew and UX under concurrent reads; do not infer snapshot consistency from unit/SQL-generation tests. Recheck learner/staff/quiz/enrollment/progress/media/assignment navigation and ensure no scoring or completion-policy changes.
