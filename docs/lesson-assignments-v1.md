# Lesson assignments, final submissions, and grading (v1)

The Angular staff quiz/assignment authoring and grading extension is documented in [staff-assessments.md](staff-assessments.md). This earlier record remains as implementation history.

The Angular Student assignments/history/My Grades and BFF extension is documented in [student-assignments.md](student-assignments.md). This earlier record remains as policy and implementation history.

Implemented in the current working tree, preserving the existing quiz, enrollment/progress, media, staff flows, and `LMS.Tests`. No branch switch, staging, commit, push, database access, migration application, browser test, or Swagger test was performed.

## Routes and contracts

All API routes require JWT authentication and the verified selected organization via `X-Organization-Id`. They use the existing organization middleware, `OrganizationContext`, and `ApiResponse<T>` envelope. `lessonId → section → course → organization` is resolved from stored records. Assignment and submission IDs must match their stored parents. The course and section IDs in MVC URLs are checked against this authorized stored context on every request.

Let `A = /api/Lessons/{lessonId:guid}/assignments`.

| Method | Exact route | Purpose / request |
| --- | --- | --- |
| GET | `A` | Staff list; Student published assignments or assignments with their own historical work |
| GET | `A/context` | Authorized parent IDs/publication flags and own enrollment state; no lesson content or student work |
| GET | `A/{assignmentId:guid}` | Staff detail or eligible Student detail; unpublished detail requires own submission |
| POST | `A` | Create draft: `title`, `instructions`, `dueAtUtc` (offset-aware timestamp or null), `maximumMarks` |
| PUT | `A/{assignmentId:guid}` | Edit the same fields; frozen-field changes return 409 |
| PATCH | `A/{assignmentId:guid}/publication` | `{ isPublished, confirmed }`; confirmation required |
| POST | `A/{assignmentId:guid}/submissions` | Multipart `Text`, optional single `File`, `Confirmed=true`; final submission |
| GET | `A/{assignmentId:guid}/submissions/me` | Only the authenticated Student's submission |
| GET | `A/{assignmentId:guid}/submissions` | Authorized staff list |
| GET | `A/{assignmentId:guid}/submissions/{submissionId:guid}` | Owner or authorized staff review, snapshots, and feedback |
| POST | `A/{assignmentId:guid}/submissions/{submissionId:guid}/grade` | `{ marks, feedback, confirmed }`; one final grade |
| GET, HEAD | `A/{assignmentId:guid}/submissions/{submissionId:guid}/attachment` | Separately authorized private PDF download |
| GET | `/api/Assignments/submissions/me` | Owner-only history with stored navigation IDs; Active/Completed courses only |

Mutation success is 201 for creation/submission and 200 for edits/publication/grading. Invalid values or missing confirmation return 400; publication/resource absence or mismatched parents return 404; ownership, organization, membership, or enrollment failures return 403; duplicates, deadline violations, frozen edits, repeated grading, and translated concurrency conflicts return 409. Authentication middleware returns 401 for expired/invalid JWTs. Request-size rejection may return 413 before controller execution. Unexpected errors use the existing generic error envelope.

Submission responses expose `hasAttachment` and a display filename, never attachment IDs, storage paths, or direct URLs. Student lists contain only their own records. History contains navigation IDs, title, server submission time, and graded status, not other students' identities/work. Grading responses include the authorized record's grade, feedback, grader ID, and server timestamp.

MVC roots:

- Staff `S = /staff/{organizationId:guid}/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/{lessonId:guid}/assignments`.
- Learner `L = /learn/{organizationId:guid}/courses/{courseId:guid}/sections/{sectionId:guid}/units/{lessonId:guid}/assignments`.

| Method | MVC route | Behavior |
| --- | --- | --- |
| GET | `S`, `L` | Assignment navigation/list |
| GET, POST | `S/new` | Create draft |
| GET, POST | `S/{assignmentId}/edit` | Staff edit; title only after submissions exist |
| GET | `S/{assignmentId}`, `L/{assignmentId}` | Instructions, publication/due/max marks, submission/status links |
| POST | `S/{assignmentId}/publication` | Explicit publish/unpublish checkbox |
| POST | `L/{assignmentId}/submit` | Confirmed final text/PDF submission |
| GET | `S/{assignmentId}/submissions/{submissionId}`, `L/{assignmentId}/submissions/{submissionId}` | Staff review/grading or owner status/feedback |
| POST | `S/{assignmentId}/submissions/{submissionId}/grade` | Confirmed final grade |
| GET | `S/{assignmentId}/submissions/{submissionId}/attachment`, `L/{assignmentId}/submissions/{submissionId}/attachment` | Authenticated server-side download proxy |
| GET | `/learn/{organizationId:guid}/assignments/history` | History linked from My Courses, including unpublished historical parents |

The shared MVC controller also matches equivalent suffixes under both roots; role checks reject learner authoring/publication/grading and staff final-submission actions. These alternate URLs grant no extra permission. All modifying MVC actions have global and controller antiforgery validation, and all forms issue tokens. Success uses POST/Redirect/GET. GETs only read; they never update progress, enrollment, or `LastAccessedAt`.

JWTs stay in the existing protected server-side sessions and outbound Authorization headers. The URL organization must match the selected session organization, and the API verifies current stored organization access. Existing expired-session and access-denied handling remains in use. No tokens or work are persisted in browser storage. Outbound HTTP logging remains disabled.

## Authorization matrix

| Operation | Student | Instructor | Admin | SuperAdmin |
| --- | --- | --- | --- | --- |
| Create/edit/publish/unpublish | Denied | Assigned stored parent course only | Selected organization | Explicitly selected organization |
| New final submission | Own Active enrollment; verified Student; published course, lesson, assignment; before due | Denied | Denied | Denied |
| Submission/history/feedback/download | Own work only, with own Active or Completed enrollment | Assigned course | Selected organization | Explicitly selected organization |
| Staff submission list/review/grade/download | Denied | Assigned course | Selected organization | Explicitly selected organization |

Learner historical work, snapshots, and feedback remain readable after unpublishing the assignment, lesson, or course. Cancelled enrollment loses learner access, without deleting work or grades; authorized staff retain review, downloads, and grading. Existing explicitly supported enrollment behavior is unchanged. Organization membership revocation and reassignment of the course away from an Instructor revoke access through fresh checks.

## Product and web policies

- A Lesson can contain multiple assignments. Creation always creates a draft. No assignment/submission/grade deletion endpoint is provided.
- Title: required, up to 200 characters. Plain-text instructions: required, up to 20,000 characters. Due date: optional, persisted as UTC. Maximum marks: positive `decimal(18,2)` with at most two meaningful decimal places.
- The web date input explicitly says **UTC**. Its wall-clock value is interpreted as UTC, without device-local timezone conversion. Offset-aware API timestamps are converted with `UtcDateTime`. Frozen protected fields are disabled in the UI and restored from authorized stored values for title edits, preserving exact due timestamps and instruction newlines.
- First successful submission freezes instructions, due date, and maximum marks. Direct prohibited API edits return a clear 409. Title changes and confirmed publication changes remain available.
- Work requires non-whitespace plain text (up to 100,000 characters), one optional PDF, or both. PDF limit is exactly 26,214,400 bytes (25 MiB), using existing extension/MIME/signature validation and actual-versus-declared length checking. Invalid/control-character filenames are rejected; display names are stripped of directory components and limited to 200 characters.
- Server time is checked after staging and inside the serialized transaction. `now >= dueAtUtc` rejects new work. Publication, enrollment, parent access, and duplicate state are freshly checked there too. No drafts, resubmission, or late override.
- Snapshots preserve the instructions and maximum marks at submission; the submitted text, attachment reference, user, and server submission timestamp are immutable. EF write guards reject modified/deleted submission and grade records.
- Grade once, from zero through the submission's maximum-marks snapshot, with at most two meaningful decimal places. Feedback is plain text up to 20,000 characters. The grader ID and grade timestamp come from authenticated context/server time. Repeated grading returns 409 and retains the original grade. The upper bound is enforced in the serialized handler; the SQL grade check enforces nonnegative marks.
- Assignment grades do not affect course or lesson completion. Existing zero-lesson, enrollment, progress, quiz, completion, and media policies were not changed.
- Razor encodes instructions, titles, submitted text, feedback, filenames, and validation messages. Safe text/numeric/date form values are retained on validation errors; a file must be selected again. Final-submission and grade buttons block accidental duplicate clicks; the server remains authoritative.
- A transport timeout, network error, malformed response, or gateway failure after a mutation opens a read-only recovery view with a GET link to stored state. It never automatically replays publication, uploads, submissions, grading, or other mutations. Known validation/conflict errors are shown safely without retries. Browser refresh/back actions remain under the user's control.

## Persistence, migration, and serialization

Migration: **`20261004061629_AddLessonAssignments`**, generated offline, reviewed, and **not applied**.

`Up` creates only `LessonAssignments`, `AssignmentSubmissions`, and `AssignmentGrades`. It does not alter existing enrollment/progress/quiz/media tables or records. All five new foreign keys use restrictive deletion. Checks enforce positive assignment/snapshot maximum marks, work presence, consistent attachment metadata with a 25 MiB bound, and nonnegative grades. Indexes include:

- Unique `(AssignmentId, UserId)`: `IX_AssignmentSubmissions_AssignmentId_UserId`.
- Unique `SubmissionId`: `IX_AssignmentGrades_SubmissionId`.
- Unique non-null `AttachmentId` (filtered), plus LessonId, UserId, and GraderId lookup indexes.

`Down` first throws if submitted work or grades exist, then drops only the three new tables in dependency order. This protects history against a normal migration rollback. The guard is SQL-generation/unit tested, not executed on SQL Server.

Assignment edits, publication changes, submissions, and grades use the same assignment-level SQL Server `UPDLOCK,HOLDLOCK` within a Serializable transaction. Stored lesson/course/section reads are held stable; fresh enrollment reads use that transaction's Serializable isolation. This is intended to serialize competing work and keep publication, assignment fields, course ownership, and eligibility stable through commit. There are no automatic retry loops or SQL execution retries configured for these operations. Deadlocks return a state conflict; the caller must inspect stored state.

SQL 2601/2627 translation recognizes only the exact quoted named final-submission or grade uniqueness constraint, not arbitrary uniqueness errors. SQL 1205 is separately treated as a concurrency conflict. Tests exercise exact-name matching and generate the actual production lock-query SQL with tenant filters and parameters. **They do not verify real lock acquisition, concurrent request outcomes, rollback, deadlocks, or commit acknowledgement behavior. SQL Server integration tests are still required.**

The design-time `--offline` factory uses no application configuration/host/seeder and installs an interceptor that rejects physical connection opening. EF migration generation and pending-model checks use this factory.

## Private storage and recovery limitations

The API stores assignment files under `App_Data/PrivateAssignments`, outside `wwwroot`, separately from lesson media. Generated GUID filenames are storage keys; the original name is display metadata only. Retrieval authorizes the submission owner or permitted staff through stored course parents. The application never substitutes the learner lesson-media endpoint for that authorization.

API and web downloads use generated download filenames, `application/pdf`, `Content-Disposition: attachment`, `Cache-Control: private, no-store`, `X-Content-Type-Options: nosniff`, and sandbox CSP. The web proxy sends the JWT server-side, checks the upstream PDF MIME, and does not forward cookies or storage URLs.

Validation precedes metadata persistence. Staged files and published-but-uncommitted files are removed on handled pre-commit failures. Immediately before committing the transaction, the published file is conservatively retained so an uncertain commit cannot delete a potentially committed attachment. Committed attachments are never replaced or automatically removed.

Filesystem and database writes are not one atomic distributed transaction. A crash can leave `.upload` files or published orphan files; a failed or ambiguous commit can retain an orphan intentionally. Disk loss or interrupted persistence can leave metadata whose file is unavailable. No automatic orphan collector/reconciliation or malware/PDF sanitization is introduced. The inherited signature validator is not a full PDF parser or malware scanner. Deployment needs durable private storage and coordinated file/database backups; multiple API instances need a shared durable assignment store. A later operator recovery must compare database references to files before removing any candidate orphan. These crash, backup, and multi-instance behaviors remain unverified.

Restrictive FKs also prevent deletion of an assignment-bearing lesson/course chain. Existing parent-deletion routes remain in place; their generic database-error presentation is not customized by this batch. Direct database administration is outside the application immutability guards.

## Changed files in this batch

New files:

- `LMS.Domain/Entities/LessonAssignment.cs`
- `LMS.Domain/Entities/AssignmentSubmission.cs`
- `LMS.Domain/Entities/AssignmentGrade.cs`
- `LMS.Application/Features/Assignments/AssignmentContracts.cs`
- `LMS.Application/Features/Assignments/AssignmentHandlers.cs`
- `LMS.Infrastructure/Persistence/Configurations/AssignmentConfigurations.cs`
- `LMS.Infrastructure/Repositories/AssignmentRepository.cs`
- `LMS.Infrastructure/Storage/AssignmentStorage.cs`
- `LMS.Infrastructure/Persistence/Migrations/20261004061629_AddLessonAssignments.cs`
- `LMS.Infrastructure/Persistence/Migrations/20261004061629_AddLessonAssignments.Designer.cs`
- `LMS.Api/Controllers/AssignmentsController.cs`
- `LMS.Web/Controllers/AssignmentsController.cs`
- `LMS.Web/Models/AssignmentModels.cs`
- `LMS.Web/Services/AssignmentApi.cs`
- `LMS.Web/Views/Assignments/Index.cshtml`
- `LMS.Web/Views/Assignments/Edit.cshtml`
- `LMS.Web/Views/Assignments/Detail.cshtml`
- `LMS.Web/Views/Assignments/Review.cshtml`
- `LMS.Web/Views/Assignments/Recovery.cshtml`
- `LMS.Web/Views/Assignments/History.cshtml`
- `LMS.Web/wwwroot/js/assignments.js`
- `LMS.Tests/AssignmentPolicyTests.cs`
- `LMS.Tests/AssignmentApiTests.cs`
- `LMS.Tests/AssignmentPersistenceTests.cs`
- `LMS.Tests/AssignmentWebTests.cs`
- `LMS.Tests/assignments-ui.test.cjs`
- `docs/lesson-assignments-v1.md`

Updated existing files (some already contained earlier uncommitted work):

- `LMS.Api/Program.cs`: handler/repository/private storage registration.
- `LMS.Infrastructure/Persistence/LMSDbContext.Organizations.cs`: DbSets, parent-scoped filters, immutable-write guards.
- `LMS.Infrastructure/Persistence/LMSDbContextFactory.cs`: strictly offline design-time mode.
- `LMS.Infrastructure/Persistence/Migrations/LMSDbContextModelSnapshot.cs`: only the three new entity mappings/FKs added relative to the pre-batch snapshot.
- `LMS.Infrastructure/LMS.Infrastructure.csproj`: internal production SQL-query visibility to the existing tests.
- `LMS.Web/Views/Staff/Lesson.cshtml`: assignment authoring/grading navigation.
- `LMS.Web/Views/Learn/Unit.cshtml`: assignment/submission/feedback navigation.
- `LMS.Web/Views/Learn/Courses.cshtml`: owner history navigation.
- `LMS.Tests/LMS.Tests.csproj`: compile the actual assignment API controller for isolated tests; existing project preserved.

No assignment infrastructure/API existed beforehand. The necessary new operations are the feature's authoring/submission/grading/private-download routes above; the small parent-context/history reads allow authorized historical navigation without duplicating or weakening existing published lesson/content APIs.

## Automated verification

Using existing cached dependencies and a separate build output directory:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/assignments/bin/'
dotnet build EnterpriseLMS.slnx --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/assignments/bin/'
node --test LMS.Tests/quiz-ui.test.cjs LMS.Tests/assignments-ui.test.cjs
git diff --check
```

- Full .NET suite: **693 passed, 0 failed, 0 skipped** (79 new assignment tests; existing 614 tests retained).
- Full solution build: **0 warnings, 0 errors**.
- `node --check` passed for all five application JS files, including `assignments.js`; both Node behavior tests passed.
- EF CLI `migrations has-pending-model-changes` with the offline factory: **no pending model changes**.
- `git diff --check`, plus CRLF-aware no-index whitespace checks for all 36 new/updated assignment files (since many existing working-tree files are untracked): passed.
- Offline migration Up/Down operations, generated SQL, FK/index checks, tenant SQL generation, policy/owner/enrollment tests, actual compiled Razor encoding/forms, MVC request payload/confirmation/recovery tests, antiforgery service validation, and private filesystem staging/cleanup tests passed. Tests prohibit physical database connections and do not start the API seeding host.

The CLI uses the cached EF tool's `ef.dll`, the separate API runtime/deps files, and **the Infrastructure assembly as startup assembly** to reach only the design-time factory:

```powershell
dotnet exec --depsfile 'artifacts/assignments/bin/LMS.Api.deps.json' --runtimeconfig 'artifacts/assignments/bin/LMS.Api.runtimeconfig.json' 'C:/Users/poloj/.dotnet/tools/.store/dotnet-ef/10.0.11/dotnet-ef/10.0.11/tools/net8.0/any/tools/net8.0/any/ef.dll' migrations has-pending-model-changes --assembly 'artifacts/assignments/bin/LMS.Infrastructure.dll' --startup-assembly 'artifacts/assignments/bin/LMS.Infrastructure.dll' --context LMSDbContext -- --offline
```

Migration generation used the same runtime/deps/assemblies, `migrations add AddLessonAssignments`, Infrastructure project/project-dir, output `Persistence/Migrations`, and `-- --offline`. No migration was applied. Browser rendering/interaction, live JWT/API/session behavior, real database constraints/transactions/concurrency, and operational crash recovery are **unverified**.

## Consolidated manual checklist for later execution

1. In a disposable authorized SQL Server environment, review/apply the migration separately; check all restrictive FKs/checks/unique indexes. Verify rollback refuses to erase submitted work and touches no existing history. Back up database and private files together before any production migration.
2. With two organizations and two Instructors, verify stored course assignment ownership, Admin organization boundaries, explicit SuperAdmin selection, revoked membership, foreign IDs, mismatched course/section/lesson/assignment/submission parents, and expired sessions across all pages/downloads.
3. From Course → Section → Lesson, create several assignments, edit fields, enter a known UTC due time, publish/unpublish with confirmation, and provoke validation errors. Check safe retained values and that missing/tampered antiforgery tokens block every modifying form.
4. As Students, test text-only, PDF-only, and combined work; whitespace-only, MIME/signature mismatches, oversized and empty files, multiple PDFs, unsafe/long filenames, and signature-valid PDF download behavior. Verify storage is outside public roots and attachment IDs/paths/tokens never appear in pages, URLs, browser storage, or logs.
5. Verify exact deadline boundary rejection using server UTC, publication gates at course/lesson/assignment levels, Active-only submissions, Completed historical owner reads, Cancelled access denial, and staff review/grading retained after cancellation/unpublishing. Check history navigation from My Courses even when course and lesson are unpublished.
6. Race submissions from two tabs/clients, submit against edit/unpublish/deadline/enrollment cancellation/course reassignment, and race two graders. With SQL Server integration instrumentation, verify one final row/grade, frozen snapshots, correct locking/rollback, no overwrite, exact 409 translation, and deadlock behavior. Unit tests alone do not establish these results.
7. Grade zero/full marks, invalid bounds/precision, repeated grades, and encoded feedback. Confirm learner lists/reviews/downloads never reveal another student's work and staff access is limited to permitted courses.
8. Interrupt upload, submission, grading, and commit acknowledgements. Verify read-only recovery and no automatic mutation replay. Confirm file cleanup on handled failures, conservative retention after ambiguous commits, and an operator reconciliation procedure for crash-created orphans/missing files.
9. Check browser confirmation, duplicate-click protection, Back/refresh behavior, encoded multiline content, UTC date input precision, file re-selection after errors, and existing learner/staff/quiz/media navigation. Verify assignment grades never change progress or course-completion eligibility, including zero published lessons and Completed enrollment history.
