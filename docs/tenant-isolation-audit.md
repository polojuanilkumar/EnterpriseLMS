# Learning API tenant isolation audit

Branch: `feature/learning-flow-stability`. This is an offline code/test audit, not a
deployment approval. No database was opened, API started, or migration applied.
The existing Organization foundation migration/backfill must precede API use.

Final offline results: **258 tests passed, 0 failed, 0 skipped** (original 89 stability
tests retained); full solution build **0 warnings / 0 errors**; EF reports no pending
model changes; `git diff --check` passes (Git emits line-ending conversion notices).
No new migration was added in this pass. Existing migration and stability work remains.

Files edited/added in this isolation pass (other pre-existing changes preserved):

- `LMS.Api/Controllers/EnrollmentsController.cs`
- `LMS.Api/Controllers/QuizQuestionsController.cs`
- `LMS.Application/Features/CourseProgress/MyCourses/GetMyCoursesProgressHandler.cs`
- `LMS.Application/Features/QuizQuestions/GetQuestionById/GetQuestionByIdHandler.cs`
- `LMS.Infrastructure/Persistence/LMSDbContext.Organizations.cs`
- `LMS.Tests/LMS.Tests.csproj`
- `LMS.Tests/TenantEndpointTests.cs`
- `LMS.Tests/TenantFilterTests.cs`
- `LMS.Tests/TenantHandlerTests.cs`
- `LMS.Tests/TenantSqlTests.cs`
- `LMS.Tests/README.md`
- `docs/enrollment-error-responses.md`
- `docs/organization-foundation.md`
- `docs/tenant-isolation-audit.md`

## Boundary and authorization decisions

All 50 learning actions require authentication and a single nonempty GUID header
`X-Organization-Id`. OrganizationMiddleware runs after authentication and before
authorization. It checks an active stored organization, stored membership, and stored
platform SuperAdmin status; global JWT Admin/Instructor/Student/SuperAdmin claims do
not authorize tenant actions. Missing/malformed/duplicate selection is 400; missing
authentication is 401; unavailable organization/nonmembership is 403.

Selected organization Admin can manage that organization's content. Instructor can
read its content but mutate only their assigned courses and descendants. Platform
SuperAdmin can select an active target without membership for staff operations, but
never bypasses the selected organization's EF filters. SuperAdmin must select a target
even for reads and receives no implicit Student role. Learner-only actions require a
stored Student membership. Enrollment cancellation and personal enrollment reads keep
their existing authenticated-owner rule rather than acquiring a new Student restriction.

All learning repository lists/details resolve through stored Course.OrganizationId.
Course -> section -> lesson -> quiz -> question -> option and course -> enrollment,
lesson -> progress, quiz -> attempt -> answer -> selected option are filtered. Missing
context and null course ownership return no learning rows. Child route IDs do not
establish ownership; handlers check nested parent matches. Catalog courses are public
within the selected organization only when published; content reads additionally
require Active/Completed enrollment for learners. Learner writes require Active.
History/results retain authenticated attempt ownership without new publication checks.

The DbContext save guard rejects unscoped writes, foreign parents/rows and changing
stored parent IDs. Course creation assigns the verified context's organization, not a
body value. Instructor assignment checks membership in the selected organization.
These controls assume request-scoped DI; internal callers must not fabricate contexts
or reuse a context across organizations. The public API is the authorization boundary.

No IgnoreQueryFilters, ExecuteSql, direct SqlConnection, or EF Find bypass exists in
the audited learning repositories/handlers. Raw FromSql lock queries remain composed
with EF tenant predicates. Enrollment and quiz transaction entry checks are scoped;
callbacks resolve scoped resources again. Section/lesson deletion rechecks ownership
inside its transaction; quiz mutation rechecks the lesson's quiz ID after locking.
No claim is made that offline SQL inspection proves lock acquisition or rollback.

Changes in this pass additionally reject an empty active context in the Course filter,
validate the question's route quiz before returning detail, and use NameIdentifier
instead of email for personal course progress. Personal progress counts only published,
distinct lessons completed by that user, consistent with course progress.

## Evidence and endpoint matrix

**verified** means the stated boundary has passing offline tests (not an HTTP/SQL
integration certification). **code-reviewed only** means the complete action path was
reviewed, with shared middleware/filter tests, but no dedicated tenant handler scenario.
**still unscoped** would mean a known open path; none was identified among these 50
actions. End-to-end tenant isolation remains unverified until disposable SQL testing.

Evidence: `TenantEndpointTests` checks all 50 real controller actions with ASP.NET
authorization policies; `OrganizationSelectionTests` checks stored membership/role
replacement and SuperAdmin target selection. `TenantFilterTests` evaluates actual EF
model predicates over A/B/null-owner graphs using .NET expressions, not EF InMemory.
`TenantSqlTests` inspects SQL Server provider SQL for all 11 learning entities, real
repository reads/prechecks and composed lock templates. Its ADO stub cannot connect;
command capture aborts before execution. `TenantHandlerTests` combines filtered graphs
with real handlers. The original stability tests remain present.

Abbreviations: **S** = selected staff role plus Instructor ownership for mutations;
**L** = Student plus publication/enrollment rules; **O** = authenticated resource owner.
All rows inherit verified organization selection and filtered repositories. Braces omit
GUID constraints for readability. Quiz routes intentionally show the existing root
paths: LessonsController has no class Route prefix. No routes were renamed here.

| Method and route | Additional checks | Status / evidence |
|---|---|---|
| POST /api/Courses | S; assigned Instructor membership; context owns new course | code-reviewed only |
| GET /api/Courses | Published for learners; tenant catalog | verified — filter graph + repository SQL |
| GET /api/Courses/{id} | Published for learners | verified — direct ID filter + repository SQL |
| PUT /api/Courses/{id} | S | code-reviewed only |
| POST /api/Courses/{id}/publish | S | code-reviewed only |
| POST /api/Courses/{id}/archive | S | code-reviewed only |
| GET /api/Courses/{id}/progress | L; own progress | verified — progress stability tests + tenant SQL |
| POST /api/Courses/{courseId}/sections | S; stored course | code-reviewed only |
| GET /api/Courses/{courseId}/sections | L for learners; stored course | verified — filter graph + repository SQL |
| GET /api/Courses/{courseId}/sections/{id} | Route course matches section; learner read checks | verified — foreign nested handler test |
| PUT /api/Courses/{courseId}/sections/{id} | S; nested match | code-reviewed only |
| DELETE /api/Courses/{courseId}/sections/{id} | S; nested match; attempts protect history | code-reviewed only |
| POST /api/Sections/{sectionId}/lessons | S; stored section/course | code-reviewed only |
| GET /api/Sections/{sectionId}/lessons | L for learners; published lessons | verified — filter graph + repository SQL |
| GET /api/Lessons/{id} | L for learners | verified — read stability tests + direct ID filtering |
| PUT /api/Lessons/{id} | S | code-reviewed only |
| DELETE /api/Lessons/{id} | S; attempts protect history | code-reviewed only |
| PATCH /api/Lessons/{id}/publish | S | verified — foreign/admin/owner/other-Instructor scenarios |
| PATCH /api/Lessons/{id}/unpublish | S | code-reviewed only |
| POST /api/Lessons/{id}/start | L; Active; own progress | verified — foreign handler + stability tests |
| PATCH /api/Lessons/{id}/progress | L; Active; own progress | verified — stability tests + tenant filters/SQL |
| PATCH /api/Lessons/{id}/complete | L; Active; own progress | verified — stability tests + tenant filters/SQL |
| GET /api/Lessons/{id}/progress | L; Active/Completed; own progress | verified — stability tests + tenant filters/SQL |
| POST /{lessonId}/quiz | S; stored lesson/course | code-reviewed only |
| GET /{lessonId}/quiz | L for learners | verified — read stability tests + tenant filters/SQL |
| PUT /{lessonId}/quiz | S; editable; attempt guards | code-reviewed only |
| PATCH /{lessonId}/quiz/publish | S; valid questions/options | code-reviewed only |
| PATCH /{lessonId}/quiz/unpublish | S | code-reviewed only |
| DELETE /{lessonId}/quiz | S; editable; no attempts | code-reviewed only |
| POST /api/Quizzes/{quizId}/questions | S; editable; no attempts | code-reviewed only |
| GET /api/Quizzes/{quizId}/questions | L for learners | verified — filter graph + repository SQL |
| GET /api/Quizzes/{quizId}/questions/{questionId} | Nested match before detail; L for learners | verified — nested handler test + tenant SQL |
| PUT /api/Quizzes/{quizId}/questions/{questionId} | S; nested match; editable | code-reviewed only |
| DELETE /api/Quizzes/{quizId}/questions/{questionId} | S; nested match; editable | code-reviewed only |
| POST /api/Quizzes/{quizId}/questions/{questionId}/options | S; nested match; editable | code-reviewed only |
| GET /api/Quizzes/{quizId}/questions/{questionId}/options | Staff role; nested match; correctness included | code-reviewed only |
| GET /api/Quizzes/{quizId}/questions/{questionId}/options/student | L; nested match; correctness omitted | code-reviewed only |
| PUT /api/Quizzes/{quizId}/questions/{questionId}/options/{optionId} | S; both nested matches; editable | code-reviewed only |
| DELETE /api/Quizzes/{quizId}/questions/{questionId}/options/{optionId} | S; both nested matches; editable | code-reviewed only |
| POST /api/Courses/{courseId}/enroll | Stored Student membership; published; unique pair | verified — foreign create handler + SQL |
| GET /api/Enrollments/me | O | verified — owner/tenant repository SQL |
| GET /api/Enrollments/{id} | O | verified — tenant direct ID filter; owner code reviewed |
| PATCH /api/Enrollments/{id}/complete | L + O; published lessons completed; row lock | verified — completion unit tests + scoped lock SQL; concurrency unverified |
| PATCH /api/Enrollments/{id}/cancel | O; Active only; same row lock | verified — cancellation unit tests + scoped lock SQL; concurrency unverified |
| GET /api/Enrollments/my-courses/progress | Student; authenticated ID; own published progress | verified — two-org handler test |
| GET /api/Quizzes/{quizId}/attempts | Student + O; historic access; foreign list empty | verified — foreign history + SQL |
| GET /api/Quizzes/{quizId}/attempts/{attemptId}/result | Student + O; nested quiz; Submitted only | verified — foreign ID handler + SQL |
| GET /api/Quizzes/{quizId}/attempts/{attemptId}/answers | Student + O; nested quiz; Submitted only | code-reviewed only — answer/selection SQL tested |
| POST /api/Quizzes/{quizId}/attempts/start | L; Active; limits/config; quiz lock | verified — foreign/active-attempt/nonactive enrollment handlers + SQL |
| POST /api/Quizzes/{quizId}/attempts/{attemptId}/submit | L + O; nested IDs; deadline/scoring; quiz lock | verified — foreign quiz/question/option/owner + successful submission |

There are no Course delete, Section publish, standalone option-detail, or standalone
CourseProgress controller routes to audit. Categories remain a global catalog with
platform-only mutation; authentication/profile/organization administration are separate
from the learning route inventory. This report does not claim all APIs are tenant data.

## Remaining limits

SQL Server concurrency integration tests were not run because Docker is unavailable.
Actual SQL execution, constraints, isolation/locks/deadlocks, rollback, migrations,
backfill, full HTTP routing/Swagger and serialization remain unverified. EF predicates
are application controls, not SQL Server row-level security. Manual SQL/external tools
and newly introduced filter bypasses would require a separate review.

Membership/active-organization authorization is checked at request entry; revocation
during an already authorized request is not synchronized with the learning transaction.
Future background jobs must initialize a verified scope. Legacy null-owned records are
preserved but invisible until explicit approved ownership/membership mapping. Existing
Identity/global roles are not proof of membership. See organization-foundation.md and
the read-only inventory / opt-in backfill templates for deployment assumptions.

## One consolidated Swagger checklist

User-reported verification: Organization migration and DEMO backfill applied;
DEMO ID `07c81b67-b0b3-43bd-842b-f51b3ac55df9`, two owned courses, four
memberships, student2 global SuperAdmin only. Enrollment/progress/attempt counts
remain 2/1/1. Ravi has **two DEMO enrollments**. These are reported inventory
results, not database verification performed during this documentation update.

Current scope is read-only. Use valid account tokens and switch Swagger's Bearer
identity when changing accounts. Swagger now declares one required
`X-Organization-Id` header (string/uuid) only for OrganizationScope endpoints,
including attributes on controllers or individual actions. Enter DEMO's ID in
that operation's header field; no default organization is supplied. Login,
organization discovery/creation and other unscoped operations have no added
header. This is documentation/UI behavior; runtime authorization is unchanged.

In the ordered table, **D** means the exact header value
`07c81b67-b0b3-43bd-842b-f51b3ac55df9`. Ravi is the approved Student
`2452b8c2-799c-48fa-854c-36ccf00e5bf8`; John is the approved Instructor
`0cf651c4-e112-4111-955a-0f44153700cb`. DEMO Admin means either approved
Admin account (`40002941-c2cc-4575-8ef7-afa68bc73929` or
`e8b90eab-54ac-48e7-b71b-bf778449fb7e`). student2 is
`60a4c3be-0e91-47be-a3ac-def38822b6ec`.

| Order | Account | Method | Route | X-Organization-Id | Expected status/result |
| --- | --- | --- | --- | --- | --- |
| 1 | Ravi | GET | `/api/Courses` | D | 200; published DEMO courses only. |
| 2 | Ravi | GET | `/api/Enrollments/me` | D | 200; his two existing DEMO enrollments. |
| 3 | John | GET | `/api/Courses` | D | 200; both DEMO courses, including drafts. List is organization-wide, not filtered by instructor. |
| 4 | DEMO Admin | GET | `/api/Courses` | D | 200; both DEMO courses. |
| 5 | DEMO Admin | GET | `/api/organizations/07c81b67-b0b3-43bd-842b-f51b3ac55df9/members` | D | 200; four approved memberships, no student2. |
| 6 | student2 | GET | `/api/Courses` | D | 200; both DEMO courses without a DEMO membership. |
| 7 | student2 | GET | `/api/organizations/07c81b67-b0b3-43bd-842b-f51b3ac55df9/members` | D | 200; four memberships. |
| 8 | Ravi | GET | `/api/Courses` | Omit entirely | 400 from middleware. |
| 9 | student2 | GET | `/api/Courses` | Omit entirely | 400; SuperAdmin also needs selection. |
| 10 | Ravi | GET | `/api/Courses` | `a54ffb19-167e-4989-9cc8-66b845304082` | 403, assuming this random ID is nonexistent or inaccessible to Ravi. Do not create it. |
| 11 | DEMO Admin | GET | `/api/Lessons/c7cd39de-0e20-4b44-805f-0479c2c90c8b` | D | 200; confirm existing lesson has isPublished=false. |
| 12 | Ravi | GET | `/api/Lessons/c7cd39de-0e20-4b44-805f-0479c2c90c8b` | D | 404; unpublished lesson is hidden. |
| 13 | DEMO Admin | GET | `/c7cd39de-0e20-4b44-805f-0479c2c90c8b/quiz` | D | 200; confirm its existing quiz has isPublished=true. |
| 14 | Ravi | GET | `/c7cd39de-0e20-4b44-805f-0479c2c90c8b/quiz` | D | 404 caused by the unpublished parent lesson, not an unpublished quiz. |

The quiz route is at the application root, without `/api/Lessons`. No fixture
creation or publication changes are needed. Status expectations assume the
reported inventory, active DEMO, valid tokens and working API/database remain
unchanged. Course publication determines Ravi's catalog count. The random ID's
actual existence/memberships cannot be established from source code alone.
Swagger may block submission when a required field is empty: for steps 8-9,
use its generated request in a client with the organization header removed;
UI validation alone does not verify the middleware's 400.

### Deferred disposable integration coverage (not authorized by this checklist)

The broader integration rehearsal remains deferred to a separately authorized
disposable SQL Server and isolated API configuration, never the existing DEMO
database. Preserve the following coverage requirements for that future work.

Rehearse migrations with representative legacy data; compare IDs/counts/history,
nullable ownership, unique code/membership/enrollment constraints and Down refusal.
Review explicit mappings and backfill dry-run, commit and retry behavior without
inferring memberships from global roles.

Use organizations A/B with distinct Admins, a user Admin in A/Student in B,
A-only/B-only users, owner/other Instructors and SuperAdmin without membership.
Verify explicit selection for staff writes, unauthenticated 401, invalid headers
400, missing/inactive/nonmember organizations 403, and rejection of forged role
claims. All matrix lists/details must exclude foreign and NULL-owned resources;
check wrong parent relationships for sections, questions, options and attempts.

Exercise staff writes under all roles and selected organizations, instructor
membership validation, catalog/publication, Active/Completed reads, Active-only
learner writes, own progress, cancellation/completion timestamps and repeated
completion. Verify attempts, results and answer snapshots against foreign IDs,
limits, deadlines, unpublished content, cancelled enrollments and repeated
submission. Students must not read staff correctness fields; platform-only users
must not gain Student quiz privileges.

Race enrollment, cancellation/completion and attempt submission/content editing.
Force save failure and verify rollback, SQL tenant predicates, unique constraints
and deadlock responses. Inspect actual Swagger routes, serialization, progress
fields, generic 500/logging and category/profile behavior before deployment.

Offline commands used:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-restore
dotnet build EnterpriseLMS.slnx --no-restore -p:OutputPath=bin/Verification/
# From LMS.Infrastructure; design-time factory, no API host or database connection:
dotnet ef migrations has-pending-model-changes --project . --startup-project . --context LMSDbContext --no-build
# From repository root:
git diff --check
```
