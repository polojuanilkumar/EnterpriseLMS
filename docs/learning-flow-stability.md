# Learning-flow stability verification

## Current status (2026-09-27)

- `LMS.Tests` is included in `EnterpriseLMS.slnx`. Its 57 unit tests passed after
  a one-time approved restore outside the sandbox. No NuGet sources were changed.
- The tests cover Active/Completed reads, Active-only lesson writes, publication
  and ownership checks, progress filtering, completion prerequisites, and repeated
  completion calls. They do not establish database concurrency or HTTP behavior.
- Migration `20260927040119_EnforceUniqueCourseEnrollment` creates the unfiltered
  unique index `IX_CourseEnrollments_CourseId_UserId`; Down only drops that index.
  The model configuration and snapshot include it. The migration is not applied.
- UnitOfWork translates only SQL errors 2601/2627 naming that exact quoted index
  into the existing enrollment conflict response. Other database errors propagate.
- The user reported zero duplicate CourseId/UserId pairs in a read-only development
  database query. No automatic record repair or deletion is included.
- SQL Server concurrency integration tests were not run because Docker is
  unavailable: `docker` is not recognized, standard executable paths are absent,
  and no Docker service was found. No existing database was used as a substitute.
  Concurrent enrollment/completion and rollback after failed saves remain unverified.
- See [the test README](../LMS.Tests/README.md) for current run commands and the
  disposable-container integration design. That design supersedes the older
  environment-variable fixture proposal below.

## Historical audit and proposed coverage

Update (2026-09-28): cancellation and completion now share the Serializable
`ExecuteEnrollmentTransactionAsync` row lock before loading enrollment state.
Cancellation validates Active status within that transaction, and the domain
`Cancel()` method also rejects non-Active status. Completed enrollment fields,
including CompletedAt, are preserved on rejection. Existing API responses and
ownership rules are unchanged. The earlier cancellation/completion race finding
below is addressed in code; actual SQL serialization and rollback still require
the disposable integration tests described in the test README. Reactivation and
other direct domain callers are not made transactional by this change.

The remainder records the earlier audit before test dependencies became available
and before the uniqueness migration was added. Statements about blocked tests,
missing projects, and absent migrations describe that earlier state. The case
matrix remains a broader coverage backlog, not a claim that all cases now pass.
The old setup commands are retained for history; use the current test README.

Branch: `feature/learning-flow-stability`.

## Status and boundaries

The three course-progress code changes are preserved. Existing response fields
describe currently published lessons; `enrollmentStatus` and
`enrollmentCompletedAt` describe the authenticated learner's stored enrollment.
The enum follows existing numeric JSON conventions (Active = 1, Completed = 2,
Cancelled = 3). No record is repaired or rewritten by the GET.

Automated tests were **not run**. Items 2 and 3 (automated access and completion
coverage) remain blocked, not complete. The configured NuGet global package
cache and HTTP cache contain no usable test SDK, xUnit/NUnit/MSTest, or mocking
packages. There is no existing test project. No test project was recreated,
no package restore was retried, and the five-project solution is unchanged.
The code audit below is not a substitute for executable tests.

No database was contacted, no API was started, and no migration was created or
applied. API startup calls `IdentitySeeder.SeedRolesAsync`; a future test host
must replace database services and disable seeding before starting the app.

## Access and progress findings

- `EnsureLessonReadable.CheckAsync` and `CheckCourseAsync` permit Active or
  Completed enrollment after checking course publication. Lesson detail also
  checks lesson publication; lesson lists filter unpublished lessons.
- Section detail validates the stored `CourseId` against the route and checks
  that parent. The course-progress controller takes `NameIdentifier` from the
  authenticated Student, not a client-provided user ID. Its handler returns
  enrollment metadata from the same enrollment used to authorize the read.
- `EnsureQuizReadable.CheckAsync` checks quiz publication, resolves its stored
  lesson, and delegates parent publication/enrollment checks. Staff read bypass
  remains. Historical attempt list/result/answer handlers retain owner-scoped
  access independently of current publication/enrollment.
- Lesson start/update/complete and quiz attempt start/submit call explicit
  `CheckForMutationAsync` methods. Completed enrollment does not authorize those
  operations. No staff bypass is accepted by these mutation methods.
- Lesson progress GET has no `Access()` or `SaveChangesAsync` call. Course
  progress GET is also read-only. Both must preserve `LastAccessedAt` and stored
  percentages. Repository progress queries filter on the caller's user ID;
  lesson progress handlers additionally check returned ownership and lesson ID.
- Quiz submit checks attempt ownership, status, and deadline before access.
  An expired attempt can therefore be marked Expired and return 409 even after
  enrollment becomes Completed or Cancelled. This is the existing deadline
  precedence, not permission to submit answers or receive a score. A timely
  submission rechecks Active enrollment and publication inside its transaction.
- Zero currently published lessons still means zero totals, 0%, and
  `isCompleted = false`, even for a historically Completed enrollment. The two
  new fields explain that history; a missing stored timestamp remains null.

## Completion and concurrency findings

`CompleteEnrollmentHandler` resolves the enrollment, verifies its owner, checks
the published course and Active status, requires a nonempty set of published
lessons, and requires the caller's completed lesson IDs to cover that set.
Unpublished lessons, other users' progress, duplicate progress rows, and rows
for other lessons cannot substitute for a missing published lesson. There is
no quiz-pass requirement.

All prerequisite checks precede `enrollment.Complete()` and the save. Repeated
calls encounter non-Active status and return 409 without changing `CompletedAt`.
The repository wraps completion in a Serializable transaction and acquires an
`UPDLOCK, HOLDLOCK` on the enrollment before the handler loads tracked state.
With separate request-scoped DbContexts, concurrent calls for the same ID
should serialize: the first completes; the next observes Completed and fails.
SQL Server deadlocks are translated to 409. These are code findings, not a
verified database concurrency result.

Remaining risks requiring tests or a later scoped change:

- SQL Server locking and rollback behavior cannot be validated with mocked
  repositories, EF InMemory, or SQLite. Use an explicitly authorized disposable
  SQL Server database with separate DbContexts/connections for concurrency tests.
- A save/commit failure may leave the request's tracked object modified even
  though the transaction rolls back. Verify persisted timestamps using a fresh
  context; never reuse a failed request context as evidence of persisted state.
- `CourseEnrollment.Complete()` itself is not idempotent; the handler's Active
  guard and transaction protect this endpoint. Direct future callers could
  bypass that protection. `UpdateProgress(100)` also calls `Complete()`.
- Cancel/reactivate flows do not use the completion transaction or a concurrency
  token. A cancellation that read Active before completion may later overwrite
  status. Audit/test that race separately before claiming all enrollment
  transitions are serialized. No transition policy was changed here.
- Without pair uniqueness, the completion lock protects one enrollment ID,
  not multiple duplicate enrollment rows for the same course and user.
- Ordinary progress GET queries are not one transactionally consistent snapshot
  across concurrent content edits. The response does not promise that guarantee.

## Exact deferred automated cases

Implement these in a future test project only after dependencies are available
and project creation is authorized. Use real handlers and shared access checks
with strict repository fakes/mocks for unit tests. Verify returned IDs, arguments
sent to repositories, and absence of Add/Save calls on rejected operations.
Use two distinct users and two courses to catch accidentally unscoped lookups.
Mark unit classes `[Trait("Category", "LearningFlowUnit")]`; mark real SQL Server
tests `[Trait("Category", "SqlServerConcurrency")]`. Do not classify semaphore
fakes as verification of SQL Server locks.

| Suggested class / cases | Setup and required assertions |
| --- | --- |
| `LearnerReadAccessTests.ActiveAndCompletedReadPublishedContent` | Parameterize Active and Completed across section list/detail, lesson list/detail, quiz GET, question list/detail, student options GET, lesson progress GET, and course progress GET. All valid fixtures return the same content and caller-owned IDs. |
| `LearnerReadAccessTests.CancelledMissingOrOtherUserEnrollmentDenied` | For each read, use Cancelled, no enrollment, or enrollment belonging only to user B. User A receives UnauthorizedAccessException / API 403, no content. Verify lookup uses A and the actual parent course. |
| `LearnerReadAccessTests.PublicationAndMissingParents` | Parameterize missing/unpublished course; missing section; missing/unpublished lesson; missing/unpublished quiz where relevant. Expect KeyNotFoundException / 404. Lesson list excludes unpublished items. Accessible section with only unpublished lessons returns an empty list. |
| `LearnerReadAccessTests.RouteMismatchAndStaffBypass` | Section/course and question/quiz or option/question mismatches return 404. Instructor/Admin/SuperAdmin retain existing read access without enrollment, including unpublished content. Missing resources still fail. |
| `ProgressReadTests.ReadsDoNotWrite` | For Active and Completed, capture lesson progress fields including LastAccessedAt and enrollment fields. Call GET handlers twice; assert identical stored fields and zero Save/Add calls. Missing lesson progress remains 404. |
| `ProgressReadTests.HistoricalCompletionIsSeparate` | Completed enrollment, stored 100%, fixed completion timestamp, no published lessons: totals 0, current progress 0%, IsCompleted false, EnrollmentStatus Completed, exact stored timestamp. Repeat with null legacy timestamp, and with 1/2 published lessons completed (50%). No writes. |
| `ProgressReadTests.CountsOnlyCallerAndPublishedLessonIds` | Mix published/unpublished lessons, completed/incomplete progress, foreign-user records, unrelated lesson IDs, and duplicate completion rows. Only distinct completed published IDs for caller count. Preserve existing two-decimal rounding and nonempty completion rule. |
| `LearnerWriteAccessTests.ActiveOnlyLessonWrites` | Parameterize start/update/complete with Active, Completed, Cancelled, missing enrollment, unpublished lesson/course. Only valid Active proceeds. Others fail 403/404 before modifying any progress fields or saving. Verify existing progress owned by another user is rejected; no caller-supplied user ID can override the claim. |
| `QuizAttemptAccessTests.ActiveOnlyStartAndSubmit` | Valid published quiz/lesson/course plus Active enrollment permits start and timely submit. Completed/Cancelled/missing enrollment returns 403 before adding attempts, answers, or scores. Freeze TimeProvider so deadline does not mask access checks. |
| `QuizAttemptAccessTests.RecheckAtSubmission` | Start under Active, then change enrollment to Completed/Cancelled or unpublish quiz/lesson/course before a timely submission. Expect 403/404, no scored answers, no SubmittedAt/EarnedMarks changes. |
| `QuizAttemptAccessTests.OwnerDeadlineAndScoringRegression` | Other user's attempt or wrong quiz route returns 404. Repeat submit returns 409. Late submit expires and returns 409 without answers, including after access revocation. Successful exact-set scoring, threshold, max-attempt and transaction behavior stay unchanged. |
| `HistoricalAttemptAccessTests.OwnerOnlySnapshots` | Existing submitted attempt remains readable by its owner after enrollment completion/cancellation and unpublication. Another user cannot obtain its list entries, results, or answer snapshots. |
| `CompletionGuardTests.Prerequisites` | Active owner, published course, >=1 published lesson, every published lesson completed: exactly one save, Completed status, non-null timestamp. Unfinished unpublished lessons do not block. No published lessons, missing/incomplete required progress, or only another user's completed progress: 409 and zero saves. |
| `CompletionGuardTests.OwnershipResourcesAndStatus` | Other user: 403; missing enrollment/course or unpublished course: 404; Cancelled/Completed status on published course: 409. In every failure assert the original Status, ProgressPercentage, and CompletedAt are unchanged. |
| `CompletionGuardTests.RepeatPreservesTimestamp` | Complete once, capture T, call again: 409, no second save, CompletedAt == T. Seed Completed with null timestamp too: repeat must not manufacture a timestamp. |
| `CompletionTransactionTests.ConcurrentCalls` | In disposable SQL Server, use two fresh request contexts and a barrier before both calls. Hold the first inside its acquired transaction, ensure the second is pending, release the first. Assert one success, one conflict, and persisted CompletedAt equals the first committed value. Use bounded timeouts, not sleeps; repeat with several parallel calls. |
| `CompletionTransactionTests.FailureRollback` | In a disposable database, force a deterministic save failure before commit after Complete() is called (e.g. test SaveChanges interceptor throws). Dispose context; fresh query shows original status, percentage, and CompletedAt. Then retry successfully and verify a later repeat preserves the new timestamp. |
| `ApiAccessContractTests.ClaimsRolesAndErrors` | Instantiate controllers with ClaimsPrincipal or use a fully isolated test host with seeding disabled. Missing/invalid/empty NameIdentifier: 401. Non-Student mutations: role 403. Fake query/body user IDs cannot change ownership. Check 403/404/409 response envelopes and numeric enrollmentStatus JSON; existing response field names remain unchanged. |

### Deferred commands (not executed)

These are a future setup/run recipe, not commands to run while packages are
unavailable. No files at these proposed paths exist yet. First implement the
cases above; an empty template test passing does not complete this work.
One suitable explicit dependency set is Microsoft.NET.Test.Sdk 17.14.1,
xunit 2.9.3, xunit.runner.visualstudio 3.1.1, and Moq 4.20.72.

After dependency access and test-project creation are authorized:

```powershell
dotnet new xunit --name LMS.LearningFlow.Tests --output tests/LMS.LearningFlow.Tests --framework net10.0 --no-restore
dotnet add tests/LMS.LearningFlow.Tests/LMS.LearningFlow.Tests.csproj reference LMS.Api/LMS.Api.csproj
dotnet add tests/LMS.LearningFlow.Tests/LMS.LearningFlow.Tests.csproj package Microsoft.NET.Test.Sdk --version 17.14.1 --no-restore
dotnet add tests/LMS.LearningFlow.Tests/LMS.LearningFlow.Tests.csproj package xunit --version 2.9.3 --no-restore
dotnet add tests/LMS.LearningFlow.Tests/LMS.LearningFlow.Tests.csproj package xunit.runner.visualstudio --version 3.1.1 --no-restore
dotnet add tests/LMS.LearningFlow.Tests/LMS.LearningFlow.Tests.csproj package Moq --version 4.20.72 --no-restore
dotnet restore tests/LMS.LearningFlow.Tests/LMS.LearningFlow.Tests.csproj
dotnet test tests/LMS.LearningFlow.Tests/LMS.LearningFlow.Tests.csproj --no-restore --filter 'Category=LearningFlowUnit' --logger 'trx;LogFileName=learning-flow-unit.trx'
```

After separate authorization for a disposable SQL Server database, implement
the concurrency fixture to require an explicit `LMS_TEST_SQLSERVER` environment
variable, reject application connection strings, and fail rather than silently
skip when that variable is missing. Then run:

```powershell
dotnet test tests/LMS.LearningFlow.Tests/LMS.LearningFlow.Tests.csproj --no-restore --filter 'Category=SqlServerConcurrency' --logger 'trx;LogFileName=learning-flow-concurrency.trx'
```

Do not run either the application startup or these database tests against the
user's database. Repository fakes can test handler ordering but cannot satisfy
the deferred concurrent/rollback persistence assertions.

## Duplicate enrollment audit and safe uniqueness plan

The runtime model has no CourseEnrollment pair index; the initial
`20260907165900_AddCourseEnrollment` migration and current model snapshot define
only its Id primary key. No subsequent migration adds pair uniqueness.
`EnrollCourseHandler` checks for an existing pair before inserting, without a
pair lock/constraint. Concurrent requests can both insert. Existing data has
not been inspected, so duplicates are a risk, not a confirmed data finding.
`GetByCourseAndUserAsync` uses unordered `FirstOrDefaultAsync`; duplicate statuses
could cause inconsistent read/write access and historical-progress metadata.

The read-only script is [sql/find-duplicate-enrollments.sql](sql/find-duplicate-enrollments.sql).
It lists duplicate pairs, all their rows, and incoming database foreign keys.
It contains no DML or DDL and was not executed. It may scan the table; an
authorized operator should choose an appropriate quiet window or restored copy.

Do not generate or deploy a uniqueness migration until these gates are met:

1. Confirm the target database, schema, migration history and actual indexes.
   Obtain an approved backup/export and a read-only duplicate report. Inventory
   foreign keys plus external/application references, not just the EF model.
2. Stop concurrent enrollment writes for the final audit and migration window.
   A clean report while writes continue does not establish a safe precondition.
3. If duplicates exist, have the owner approve a per-pair canonical record and
   reference-remapping plan. Conflicting status, timestamp, or percentages are
   a product/data decision: retain an approved historical completion or an
   approved current enrollment; do not arbitrarily take the newest row, highest
   percentage, earliest timestamp, or Completed status. Do not combine rows into
   synthetic completion evidence. Export every original row before any manual
   remediation. Cancelled duplicates still violate an unfiltered unique index.
4. Execute any separately approved remediation in a reviewed transaction with
   rollback instructions and reference reconciliation. This batch supplies no
   deletion/update script and performs no record changes. Re-run detection and
   require zero duplicate pairs; preserve the before/after reports.
5. Only then add EF mapping
   `HasIndex(x => new { x.CourseId, x.UserId }).IsUnique()` and generate a reviewed
   migration with a unique, unfiltered index named
   `IX_CourseEnrollments_CourseId_UserId`. Cover every status; reactivation already
   reuses the existing enrollment. Update the model snapshot through EF tooling.
   Include a migration precondition that aborts on any duplicate pair instead
   of deleting data. The unique index is the final protection against races.
6. Prepare the enrollment-create error path to translate SQL Server duplicate
   key errors (2601/2627) into a controlled 409, rather than a generic 500. Test
   simultaneous enrollment requests: one inserted row, one success, one conflict.
   Do not treat unrelated integrity failures as duplicate enrollments.
7. Rehearse migration and concurrency tests on an explicitly authorized
   disposable/restored database. Estimate index build blocking and disk/log
   needs; do not assume online index support. Generate and review a deployment
   SQL script, then obtain separate deployment authorization. Apply nothing here.
8. Following an authorized deployment, verify the unique index and absence of
   duplicates, run the enrollment/read/completion smoke checks, then resume
   writes. A Down migration may drop the index but cannot undo manual data
   reconciliation; backup/restore planning is separate.

No migration, model-index change, or data repair is included in this batch.
Actual duplicate status and SQL Server concurrency correctness remain unverified.

## Single ordered Swagger verification checklist (not executed)

Use an explicitly authorized disposable environment with prepared fixtures;
publication/enrollment changes and completion checks are mutations. Do not use
the user's database for this checklist.

1. With Active learner A, GET section list/detail, lesson list/detail, quiz,
   question list/detail, student options, lesson progress, and course progress.
   Assert published content and A's IDs. GET paths include
   `/api/Courses/{courseId}/sections[/{id}]`, `/api/Sections/{sectionId}/lessons`,
   `/api/Lessons/{id}`, `/api/Lessons/{id}/progress`,
   `/api/Courses/{id}/progress`, the existing `/{lessonId}/quiz` route, and
   `/api/Quizzes/{quizId}/questions[/{questionId}]` with
   `/{questionId}/options/student` under the questions route.
2. Repeat with Completed learner B. Reads succeed; cancelled/missing enrollment
   returns 403. A historically Completed course with zero published lessons
   reports current 0/0, 0%, false plus enrollmentStatus 2 and its stored timestamp.
3. Read progress twice and compare LastAccessedAt, completion timestamps, and
   percentages; GET must not update stored values. Verify null legacy timestamp
   remains null rather than being inferred.
4. Use fixtures with unpublished course/lesson/quiz and missing resources:
   applicable detail reads return 404; lesson lists omit unpublished lessons.
   Staff retain their existing read behavior.
5. Use A's token with B's IDs and mismatched parent routes. Section mismatches
   and foreign attempts return 404; completing B's enrollment returns 403.
   Client-supplied user IDs must not replace authenticated ownership.
6. On disposable Active fixtures, lesson start/update/complete and quiz attempt
   start/timely submit succeed. Completed/Cancelled fixtures return 403 for
   these writes. Revoke enrollment or unpublish a parent after start and before
   timely submit: 403/404, no scored answers. Late submissions retain the
   documented expiration/409 precedence.
7. Complete enrollment with no published lessons, missing progress, or an
   unfinished published lesson: 409 and unchanged CompletedAt. Unfinished
   unpublished lessons do not block otherwise eligible completion.
8. Complete an eligible enrollment once: 200. Capture CompletedAt using the
   enrollment GET, repeat completion: 409 and identical timestamp. Concurrent
   calls and failed-save rollback require the deferred SQL Server tests;
   sequential Swagger calls do not establish concurrency correctness.
9. Read the caller's historical attempt list/results/answers after completion
   and unpublication; prior owner-only behavior is preserved. Check quiz scores
   and response field names for regressions. Do not infer passing concurrency
   tests or applied uniqueness from these manual checks.
