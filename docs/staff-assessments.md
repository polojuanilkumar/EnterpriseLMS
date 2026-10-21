# Angular staff quiz, assignment authoring and grading

Implemented 2026-10-15, preserving the existing working trees. Inspected the actual quiz/assignment controllers, handlers, repositories, server-side clients and MVC forms, plus `quiz-ui-batch.md`, `lesson-assignments-v1.md` and `staff-authoring.md` before implementation. Existing scoring, publication, attempt limits, deadlines, field freezes, grade-once rules and completion policies remain authoritative in the business APIs.

## Navigation and Angular contracts

Let `L=/workspace/staff/courses/{C}/sections/{S}/lessons/{L}`. All IDs are GUIDs.

| Route | Behavior |
| --- | --- |
| `L/quiz` | Create/edit quiz settings, ordered questions/options, confirmed delete and publish/unpublish |
| `L/assignments` | Stored assignment list, inline draft creation/editor, confirmed publish/unpublish |
| `L/assignments/{A}` | Assignment details and paginated submissions |
| `L/assignments/{A}/submissions/{W}` | Submitted text/snapshots, authorized PDF download, stored feedback or one final confirmed grade |

The existing staff lesson page links both assessment pages. All four pages load lazily inside the existing role shell, retain nested breadcrumbs and use the same selected organization. Instructors, selected-organization Admins and explicitly selected SuperAdmins can enter; browser routing remains advisory. Student guards and learner-safe response contracts are unchanged.

Forms use labelled controls, responsive Tailwind, native keyboard controls, loading/empty/error states and plain-text interpolation. No HTML trust bypass, new package, browser-storage persistence, quiz auto-scoring or completion write was added. Safe entered values remain after validation/conflicts. Unsaved navigation/beforeunload warns; context changes clear state instead of retaining it across tenants.

## Narrow BFF routes

Let `B=/bff/staff/courses/{C}/sections/{S}/lessons/{L}`, `Q=B/quiz/{Q}`, `A=B/assignments/{A}`, `W=A/submissions/{W}`. Responses use the existing sanitized BFF error envelope; successful writes return204. Reads return the page objects below, without upstream API envelopes. Request bodies are capped at1 MiB.

| Method / route | Request and response / existing upstream |
| --- | --- |
| `GET B/quiz` | `{ quiz, questions:[{ question, options }], form }`; existing `GET /{lessonId}/quiz`, `GET /api/Quizzes/{quizId}/questions`, staff `/questions/{questionId}/options` |
| `POST B/quiz` | `QuizForm`; explicit creation only; conflicts if a quiz already exists; existing `POST /{lessonId}/quiz` |
| `PUT Q` | `QuizForm`; stored quiz ID must match; existing `PUT /{lessonId}/quiz` |
| `POST Q/questions` | `QuestionForm`; existing questions POST |
| `PUT Q/questions/{questionId}` | `QuestionForm`; existing question PUT |
| `POST Q/questions/{questionId}/options` | `OptionForm`; existing options POST |
| `PUT Q/questions/{questionId}/options/{optionId}` | `OptionForm`; existing option PUT |
| `POST Q/questions/{questionId}/delete` | `{ confirmed:true }`; existing question DELETE |
| `POST Q/questions/{questionId}/options/{optionId}/delete` | `{ confirmed:true }`; existing option DELETE |
| `POST Q/delete` | `{ confirmed:true }`; existing `DELETE /{lessonId}/quiz` |
| `POST Q/publication` | `{ confirmed:true, action:"publish"|"unpublish" }`; existing lesson-root quiz PATCH actions |
| `GET B/assignments` | `{ assignments, assignment:null, submission:null }`; existing assignment context/list |
| `GET A` | Same page object with authorized `assignment`; existing assignment detail |
| `GET W` | Same page object with authorized `assignment` and `submission`; existing submission review |
| `POST B/assignments`, `PUT A` | `StaffAssignmentForm` below; existing assignment POST/PUT |
| `POST A/publication` | `{ isPublished:bool, confirmed:true }`; existing assignment publication PATCH |
| `GET A/submissions?page=1&pageSize=20` | `{ page, pageSize, total, items }`; new authorized bounded business API read below |
| `POST W/grade` | `{ marks:decimal-string, feedback:string, confirmed:true }`; existing final-grade POST |
| `GET/HEAD W/attachment` | Separately authorized streamed PDF download; existing assignment attachment GET/HEAD |

Quiz field names and business rules are reused. Staff BFF quiz settings/form `passingPercentage` and question `marks` are invariant decimal strings on reads and in Angular inputs, preserving exact decimal precision. The existing typed server clients send decimal JSON numbers to the business API; other numeric fields remain integers:

- `QuizForm`: required `title`, optional `description`, `passingPercentage`0–100, positive integer `timeLimitInMinutes` and `maxAttempts`.
- `QuestionForm`: required `questionText`, `questionType`1 SingleChoice/2 MultipleChoice/3 TrueFalse, positive integer `displayOrder`, positive decimal `marks`.
- `OptionForm`: required `optionText`, positive integer `displayOrder`, boolean `isCorrect`. Correctness appears only in authorized staff responses; learner options/results were not changed.
- The server validates unique ordering, positive marks and publish configuration. SingleChoice requires exactly one correct option; MultipleChoice at least two; TrueFalse exactly two options and one correct option. No automatic question/option generation or new scoring rule was introduced.

Assignment reads contain `id`, `lessonId`, `title`, `instructions`, exact offset-aware `dueAtUtc` or null, `maximumMarks` as an invariant decimal string, `isPublished` and `frozen`. Submission reads contain `id`, `assignmentId`, authorized `userId`, `text`, `hasAttachment`, `submittedAt`, `instructionsSnapshot`, exact decimal-string `maximumMarksSnapshot`, and nullable `grade { marks:decimal-string, feedback, gradedAt }`. Storage IDs, paths, upstream URLs and tokens are absent. Decimal strings avoid JavaScript rounding of the existing decimal(18,2) bounds; JSON decimal strings are accepted by the existing ASP.NET JSON settings and tested at the BFF boundary.

`StaffAssignmentForm` contains `title` (required/max200), optional `instructions` (max20000), nullable decimal `maximumMarks`, nullable offset-aware `dueAtUtc`, `protectedFieldsChanged`, `dueAtChanged`. New assignments require instructions and positive maximum marks; the existing API validates all business bounds. For an edit with `protectedFieldsChanged=false`, BFF restores stored instructions and maximum marks, preserving exact line endings/decimal values. With `dueAtChanged=false`, it restores the exact stored deadline independently of other edits. A deliberate deadline edit is converted from explicitly labelled UTC wall-clock input to a UTC timestamp; clearing it sends null. Both `Z` and `+00:00` stored UTC spellings are supported without initial conversion through JavaScript Date.

Frozen fields are disabled in Angular, but the server does not rely on that display state. Deliberately changed protected fields still reach the existing serialized API freeze validation and return409 after submissions. Title-only edits preserve exact stored fields. Assignment title/instructions/deadline/max, submission snapshots, UTC timestamps and separately labelled device-local date displays remain distinct. Local formatting never rewrites the stored deadline. Final marks can be zero, must be within the submission snapshot maximum with at most two meaningful decimal places, and feedback is plain text/max20000; the API revalidates those rules. An existing grade is displayed even when marks are zero and has no override form.

## Necessary pagination addition

The inspected staff `GET /api/Lessons/{L}/assignments/{A}/submissions` returns an unbounded array and remains unchanged for MVC compatibility. Angular never calls that route. Added only:

`GET /api/Lessons/{lessonId:guid}/assignments/{assignmentId:guid}/submissions/page?page=1&pageSize=20`

It requires the existing verified organization middleware and staff role/assigned-course ownership. The handler validates stored lesson/assignment context before querying. Bounds: page1–10000, pageSize1–50. Response is the existing API success envelope with `data { page, pageSize, total, items:SubmissionResponse[] }`. Ordering is descending `SubmittedAt`, then ascending `Id`. The production repository applies tenant filters, `CountAsync`, SQL `Skip/Take`, and maps grades only for returned rows. BFF validates echoed bounds, count, page length, duplicate IDs and each submission's assignment parent. Angular requests20 per page.

Assignment frozen-state reads and the existing edit freeze check now use repository `AnyAsync` through `HasSubmissions` instead of loading every submission. This changes query shape, not freeze policy. The existing MVC array route, Student history, API business writes, SQL serialization/grade uniqueness and schema remain intact. Count and page reads are not an atomic snapshot; concurrent inserts can shift pages. The existing assignment-per-lesson and quiz question/option APIs remain unpaginated; no unrelated list API was invented.

## Security, errors and recovery

Every BFF request resolves the protected server session; its selected organization is mandatory, including for SuperAdmin. Existing clients fetch live context, verify actor identity/role and Instructor course ownership or Admin scope, then validate stored course/section/lesson and assessment children. Quiz/question/option IDs and assignment/submission IDs are checked against those records on every operation. The API independently authorizes all reads/writes/downloads through stored tenant parents. Cached/global roles, browser organization headers, body actor IDs and query-selected tenants provide no authority. Optional BFF child IDs are route-bound.

All mutations use the existing antiforgery cookie/header infrastructure. Publication/deletion/grading require explicit confirmation. Duplicate clicks are blocked. Grade/publication requests are never automatically retried. A409 refreshes stored state while retaining entered values, allowing a concurrent stored grade/freeze to be shown. An uncertain submitted mutation locks subsequent writes and performs GET-only recovery. For uncertain quiz/assignment/question/option creation, those GETs include the stored parent collection; they never create another resource. The user must inspect state before leaving/reopening. A write already in flight may still finish after navigation/context switching; late responses cannot populate a different context.

Context identity, epochs and read sequences reject stale responses. Organization switch, logout, expiry, session verification/uncertainty and destruction clear quiz keys, editor values, submissions, feedback, decimal/deadline state and selections.401/403/404 remove sensitive displayed state, with distinct safe messages. No assessment/file content is saved to browser storage.

Publication errors and attempt edit-lock/frozen-field/already-graded errors are displayed clearly using an allowlist of existing fixed business messages and narrowly matched numeric question-order configuration errors. Arbitrary upstream diagnostics never cross this boundary. Other validation/conflict errors use fixed safe messages; automatic model validation and CSRF errors retain existing sanitized contracts. Angular renders all messages as text. No learner BFF endpoint gained correctness fields.

Staff review/download/grading does not require the learner to remain enrolled or content to remain published. Existing cancelled-learner restrictions remain on Student APIs, while permitted staff retain historical access. Downloads authorize the entire parent chain and submission, verify upstream PDF MIME, stream server-side and use generated `assignment-{submissionId:N}.pdf` attachment disposition, private/no-store, nosniff and sandbox CSP. HEAD returns headers without a body. There is no generic URL proxy or unrestricted asset endpoint. Security/CSP headers were not relaxed, and existing private-storage retention/cleanup is unchanged.

## Existing API limits and remaining gaps

- Staff quiz reads have no `hasAttempts`/editability metadata. Angular explains the lock rules and shows authoritative server errors; it does not query learner histories or invent an attempt count. Published editors are disabled. Existing policy allows title/description/max-attempt edits only after unpublishing and preserves passing/time/question/option/delete locks after attempts.
- Quiz settings/publication/deletion APIs address a lesson, whereas question/option APIs address quiz IDs. BFF checks the current quiz ID before forwarding, but there is no expected-version contract spanning separate HTTP requests. No atomic read/check/write guarantee, concurrency token, automatic replay or new locking policy is claimed.
- Staff submission rows contain user IDs; there is no display-name contract in this workflow. The UI labels those IDs and does not call member administration or reporting to expand them.
- Grade and deadline comparisons, real transactions, concurrent requests/deadlocks and commit acknowledgement remain API/database responsibilities. Current tests do not prove SQL locking or live storage availability.
- Browser rendering, UTC date-input precision/locale behavior, keyboard/screen-reader experience and actual private PDF downloads remain unverified in a live deployment. Existing lesson audio/video/PDF behavior is unchanged and remains subject to its earlier live verification gaps.

## Changed files

Frontend additions: `src/app/features/staff-assessments/staff-assessments.models.ts`, `staff-assessments.component.ts`, `staff-assessments.component.html`, `staff-assessments.component.spec.ts`.

Frontend integration: `src/app/features/staff-authoring/staff-authoring.routes.ts`, `staff-authoring.component.html`, `src/app/core/auth/workspace-routing.ts`, `src/app/app.routes.spec.ts`, `README.md`, `docs/staff-authoring.md`, this report.

Backend additions: `LMS.Web/Models/BffStaffAssessmentModels.cs`, `LMS.Web/Services/BffStaffAssessmentApi.cs`, `LMS.Web/Controllers/BffStaffAssessmentsController.cs`, `LMS.Tests/Integration/BffStaffAssessmentTests.cs`, `LMS.Tests/AssignmentStaffPaginationTests.cs`.

Backend pagination/query changes: `LMS.Application/Features/Assignments/AssignmentContracts.cs`, `AssignmentHandlers.cs`, `LMS.Infrastructure/Repositories/AssignmentRepository.cs`, `LMS.Api/Controllers/AssignmentsController.cs`, `LMS.Tests/AssignmentPolicyTests.cs` (in-memory repository implements new queries). Backend mirrors this report; existing quiz/assignment/staff reports link it. No MVC controller/view or learner response contract was changed.

## Verification

Final verification on 2026-10-15:

- `npm.cmd run build -- --configuration production`: passed, including a separate lazy staff-assessments chunk.
- `npm.cmd test -- --watch=false --browsers=ChromeHeadless --progress=false`: **168 passed**, including17 new assessment/routing cases. Coverage includes explicit payloads, all question types/options, CSRF/duplicate prevention, retained validation values, publication/deletion/grading confirmations, server errors, frozen fields, exact UTC/offset spelling, decimal strings, graded zero/already-graded rendering, bounded pages, safe text, context cleanup, stale responses and read-only recovery.
- `dotnet test LMS.Tests/LMS.Tests.csproj --no-restore --filter 'Category!=SqlServerIntegration&(FullyQualifiedName~Bff|FullyQualifiedName~Learner|FullyQualifiedName~Authoring|FullyQualifiedName~Staff|FullyQualifiedName~LessonContent|FullyQualifiedName~Media|FullyQualifiedName~LearningAccess|FullyQualifiedName~Enrollment|FullyQualifiedName~Organization|FullyQualifiedName~Quiz|FullyQualifiedName~Assignment|FullyQualifiedName~Reporting)' -m:1 -p:UseSharedCompilation=false --logger 'console;verbosity=minimal'`: **855 passed**, including30 new assessment/pagination cases and existing learner/MVC/authorization/business regressions. New tests cover live role differences, selected platform access, tenant/owner/nested denial, CSRF/confirmation, safe publication errors, exact decimal/deadline/newline payloads, frozen edits, repeated grading, historical access, private downloads, no replay and actual production SQL pagination translation.
- `dotnet build EnterpriseLMS.slnx --no-restore --no-incremental -m:1 -p:UseSharedCompilation=false`: passed,0 warnings/errors.
- `node --test LMS.Tests/assignments-ui.test.cjs LMS.Tests/quiz-ui.test.cjs`:2 passed. `node --check` on both tests and every existing `LMS.Web/wwwroot/js/*.js`: passed.
- Both `git -c core.safecrlf=false diff --check` and direct whitespace checks of new assessment/pagination source/tests/report: passed.

Test fixtures use Angular HTTP fakes, in-process MVC and real business handlers with in-memory repositories. Production pagination SQL is generated offline to assert stable ordering, tenant filters and OFFSET/FETCH without opening a connection. No live database or normal host is involved. The first Angular test compile found a fixture type mismatch; it was corrected before the passing run. Existing Karma/Webpack deprecation and backend test-build NU1510 pruning warnings remain; no dependency changes were made. Fixture iframe404 warnings from prior media tests are not live browser verification.

Branches remain `chore/angular-dependency-upgrade` and `feature/learning-flow-stability`. Existing work was preserved. No staging, commits, pushes, branch switches, packages, schema changes, database access, migration application or normal hosts occurred. Real browser downloads/rendering, deployed migration state, SQL concurrency/rollback and durable storage behavior remain unverified.

## Migration/deployment prerequisites and manual checklist

No schema changes, new packages or migrations were added/applied. Do not assume pending migrations are deployed. Review the existing full chain, especially `20261004061629_AddLessonAssignments`, existing quiz-attempt/organization/private-media migrations, and pending `20261006122454_AddOrganizationInvitations` and `20261006135202_AddAuditEvents`. Existing durable private-assignment storage permissions/shared-store configuration, coordinated file/database backups, HTTPS same-origin BFF cookies/CSRF, proxy limits/timeouts and API/BFF deployment compatibility remain prerequisites. This batch accessed no database and ran no migration, seeder or storage cleanup.

One later manual checklist in a separately authorized disposable environment:

1. Review actual migration/deployment state and durable private storage without assuming pending migrations were applied.
2. As assigned Instructor, organization Admin and selected SuperAdmin, navigate to quiz/assignment management. Try Student, unassigned Instructor, foreign organization and mismatched quiz/question/option/assignment/submission parents. Revoke/reassign access and repeat reads/writes/downloads. Test unselected SuperAdmin and missing/forged CSRF.
3. Create/edit all quiz question types/settings/options, exercise marks and order validation, retain values after errors, cancel/confirm deletion/publication, publish invalid configurations, then correct them. Create attempts and verify existing edit locks, with no learner answer-key disclosure.
4. Create/edit/publish/unpublish assignments with optional UTC deadlines and explicit local display. Use a stored fractional-second deadline, CRLF instructions and large decimal maximum; make title-only edits and compare exact stored values. Submit once and verify protected fields freeze, including forged direct requests and a freeze occurring between read/save.
5. Page through staff submissions; review plain text/snapshots and download only authorized PDFs. Unpublish the hierarchy/cancel the learner and confirm permitted staff historical review remains while existing learner restrictions hold.
6. Cancel/confirm one final grade, including zero, upper bound, excessive precision/marks and feedback length. Race/repeat grading and verify the original grade is retained and no override appears.
7. Interrupt create/save/publication/delete/grade responses; confirm GET-only recovery and no mutation replay. Double-click actions. Switch organization, logout, expire/revoke access and navigate during in-flight requests; verify old keys/work/feedback clear and late responses are ignored.
8. Verify real browser layouts, keyboard/screen-reader forms, date input/locale conversion, authenticated PDF headers/downloads, and existing Student quiz/assignment/grades plus staff content/media flows. Confirm assessment navigation/management never adds a completion requirement or progress write. Real database concurrency/rollback and storage behavior remain unverified until separately tested.
