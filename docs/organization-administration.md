# Angular organization administration, reports and audit

Implemented 2026-10-15. Inspected MVC `AdminController`, `InvitationsController`, `ReportsController`, `AuditController`, actual organization/account/report/audit APIs and policies, Angular staff/shell/session handling, and the existing member/platform, onboarding, reporting/gradebook, audit and staff implementation reports before editing. Existing branches and earlier working-tree changes were preserved.

## Scope and navigation

Admin or explicitly selected SuperAdmin receives shell links to Organization members, Invitations and Organization audit. Instructor/Admin/selected SuperAdmin receives Course reports. The existing staff course page links its learner report and, for Admin/platform scope, Instructor reassignment. Student has neither administration nor staff reports. Instructor has reports only for assigned courses and has no membership/invitation/audit administration. Navigation and route guards are UX; every BFF request independently revalidates server authorization.

| Angular route | Screen |
| --- | --- |
| `/workspace/organization/members` | Bounded roster, search/role filter, exact-email assignment and confirmed role change |
| `/workspace/organization/invitations` | Bounded invitation list, delivery/lifecycle state, confirmed create/reissue/revoke |
| `/workspace/organization/courses/{courseId}/instructor` | Bounded searchable Instructor membership choices and confirmed reassignment |
| `/workspace/staff/reports` | Bounded course summaries with existing report filters |
| `/workspace/staff/reports/courses/{courseId}/learners` | Bounded learner report and validated assignment-review navigation |
| `/workspace/organization/audit` | Bounded UTC-window organization audit list and filters |
| `/workspace/organization/audit/{eventId}` | Read-only safe event detail/metadata |

These routes and the component load lazily in the responsive shell. Forms have implicit accessible labels, table headers/captions, pagination, confirmations, plain-text rendering and loading/empty/error states. There are no audit edit/delete/export controls, fabricated analytics, account-directory queries, member deletion, account deactivation, global-role edits, platform organization management, anonymous invitation acceptance or password-recovery pages in this batch.

Pagination/refresh/recovery retain the applied filters; editing draft filter fields does not alter paging until Apply resets to page1. Pagination buttons use the returned page/size, including after failed reads. Safe draft filter/editor values remain in memory.

## Exact BFF routes and contracts

Organization routes derive the actor and selected organization from the protected verified session, not a browser organization parameter. Missing organization selection is403, including for SuperAdmin. JSON bodies are capped at16 KiB. Reads return typed page DTOs without API envelopes. Successful mutations return204 with no upstream response body.

| Method / route | Contract and existing server client |
| --- | --- |
| `GET /bff/organization/members` | `MembersPage { organizationId, members, page, pageSize, total, search, role, form:null }`; existing `Members` client |
| `POST /bff/organization/members` | `{ email, role, confirmed:true }`; exact-email existing-account assignment via `SetMember` |
| `PUT /bff/organization/members/{userId}` | `{ role, confirmed:true }`; `OrganizationMember` verifies an existing selected-org member before `SetMember`; no arbitrary ID-based account assignment |
| `GET /bff/organization/courses/{courseId}/instructor` | Existing `InstructorPage { organizationId, course, instructors, choices }`; bounded membership choices with server-fixed Instructor role |
| `PUT /bff/organization/courses/{courseId}/instructor` | `{ instructorId, confirmed:true }`; re-fetch/revalidate stored target membership as Instructor, then existing `AssignInstructor` |
| `GET /bff/organization/invitations` | `WebInvitationPage { page, pageSize, total, items }`; typed existing account client |
| `POST /bff/organization/invitations` | `{ email, role, reissue, confirmed:true }`; existing confirmed create/reissue operation; response body discarded |
| `POST /bff/organization/invitations/{invitationId}/revoke` | `{ confirmed:true }`; existing selected-org revoke operation |
| `GET /bff/organization/audit` | `{ page, pageSize, total, filter, items:BffAuditRow[], actions, outcomes }`; existing `AuditPage`, with safe projection below |
| `GET /bff/organization/audit/{eventId}` | `BffAuditRow`; existing `AuditDetail`, selected organization only |
| `GET /bff/staff/reports/courses` | Existing `StaffReportsPage { organizationId, filter, courses }` |
| `GET /bff/staff/reports/courses/{courseId}/learners` | Existing `StaffLearnersReportPage { organizationId, filter, report:{ course, learners } }` |
| `GET /bff/staff/reports/courses/{courseId}/learners/{userId}/assignment-review` | Query `sectionId`, `lessonId`, `assignmentId`, `submissionId`; authorized `ReportSubmissionLink { courseId, sectionId, lessonId, assignmentId, submissionId }` only, after stored parent and submission-owner checks |

All path IDs are GUID constrained; optional membership target IDs are route-bound. Member input email is limited to256 characters and must be an exact address, without a display name; roles are Student1, Instructor2, Admin3. The response never supplies a resolved global account ID for email assignment or an account-availability directory. Known public safeguard messages are reused from the existing allowlisted administration client. Unknown/ambiguous/unavailable account errors remain generic.

Member/Instructor query fields reuse `OrganizationFilterForm`: `page`1–10000, `pageSize`1–50, `search` up to100 characters, optional `role`1–3 for the roster. Instructor choices fix role2 server-side regardless of a valid supplied role value. Selection survives paging and is revalidated independently of the displayed page on submission.

Invitation query bounds match the actual account API: `page`1–100000 and `pageSize`1–100, default1/20. BFF rejects invalid bounds rather than relying on upstream clamping. Invitation items contain only `id`, `organizationId`, `email`, `role`, `createdAt`, `expiresAt`, `acceptedAt`, `revokedAt`, `deliveredAt`. Tenant IDs, roles, IDs and page metadata are checked. Unknown upstream token/hash/link properties cannot enter the typed response. Mutation replies are discarded, even if the upstream body has additional fields. No raw token, invitation URL, token hash, email body, filesystem capture or capture endpoint is exposed.

Reports reuse `ReportFilterView`: `page`1–10000, `pageSize`1–50, `search` up to100 characters, optional `published` true/false and `enrollmentStatus`1 Active/2 Completed/3 Cancelled. Course search uses course-title substring; learner search uses first-name or last-name substring. Course publication filters the course, not historical assessments. Course enrollment-status filtering selects courses with a matching enrollment but does not narrow each course's summary counts; learner-status filtering selects displayed enrollment rows. Server ordering and scoped total/page contracts are preserved.

The course summary BFF represents its64-bit `progressDenominator` as an invariant decimal string, preserving values beyond JavaScript's safe integer range. All other existing count/percentage fields and page metadata are retained. Angular displays the server denominator/percentage without recomputing them.

## Existing upstream routes reused

- `GET /api/organizations/{org}/context`: live actor, stored role and selected scope.
- `GET /api/organizations/{org}/members`, `GET /api/organizations/{org}/members/{user}`; `POST /api/organizations/{org}/members`; `PUT /api/organizations/{org}/members/{user}`.
- Existing scoped course/section reads and `PUT /api/Courses/{course}/instructor`.
- `GET /api/account/antiforgery`: server-only upstream antiforgery pair for invitation writes.
- `GET/POST /api/account/organizations/{org}/invitations`; `POST /api/account/organizations/{org}/invitations/{id}/revoke`.
- `GET /api/Reports/courses`; `GET /api/Reports/courses/{course}/learners`.
- `GET /api/audit/organizations/{org}`; `GET /api/audit/organizations/{org}/{event}`.
- Existing staff assignment context/list/detail/submission review reads for the on-demand review-link validation. The `StaffAssignment` client avoids the legacy unbounded submission-list request.

No business API endpoint, repository, policy, schema or existing MVC behavior was changed. No generic URL proxy was introduced.

## Authorization, safeguards and rate limits

Existing session-cookie verification and live API context validate actor identity, current stored role, active selected organization, Admin scope or Instructor ownership on every operation. Admin targets and member choices are organization scoped. Instructor reassignment resolves stored course parents and target Instructor membership again; the guarded API coordinates assignment with role changes at save. Membership writes retain last-Admin, Admin self-demotion, assigned-Instructor and Active-enrollment safeguards. No UI count is treated as proof a role change is permitted.

Every mutation requires the existing browser antiforgery cookie and `X-CSRF-TOKEN`. Invitation writes additionally obtain and forward the API's distinct antiforgery cookie/token entirely server-side using the same bearer identity. JWTs, server API-CSRF tokens/cookies and API diagnostics are never returned to Angular. The existing `organization-administration` limiter is attached to BFF mutations and invitation listing, matching the MVC invitation listing policy. It remains10 requests/minute per authenticated actor, zero queue; existing API mutation limits remain independent. Roster/report/audit reads do not acquire that BFF mutation limit. Invitation refreshes consume the existing shared administration budget, so frequent refreshes can return429. No automatic rate-limit retry was added.

429 remains sanitized JSON `rate_limited`, whether produced locally or by the API. Existing safe401/403/404/400/409 handling is retained. Upstream503 delivery failure maps through the existing client to sanitized502; its outcome may include an already committed invitation. Angular locks writes and reads the invitation list without automatically creating/reissuing anything. Sender acceptance is not inbox receipt; null DeliveredAt is Unconfirmed, not proof that no email was sent. Accepted/revoked/expired/pending states are displayed independently from delivery timestamps.

Confirmed actions disable duplicate submissions. Validation/conflict/rate-limit errors preserve safe entered values. A known409 refreshes stored rows while retaining editor values. After an uncertain submitted mutation, further writes are locked, a GET-only refresh checks the member/invitation/course state, and the user must inspect it before leaving/reopening for another explicit action. Filtered or paginated absence is not proof of failure; filters can still be changed through read-only controls. No refresh, navigation or recovery path replays membership, reissue, revocation, reassignment or delivery. A write already in flight may still finish after context changes.

Context identity, epochs and read sequences reject stale responses. Organization changes, logout, expiry, session verification/uncertainty and destruction clear rosters, invitations, editor values, member/Instructor selections, report counts, audit metadata/date filters and prepared review paths.401/403/404 remove displayed sensitive records. No organization display data is persisted to browser storage. No GET changes LastAccessedAt, progress, enrollment, attempts, grades or audit records.

## Report meanings and assessment links

Existing server counters are displayed, without computing new analytics or an overall grade:

- Enrollment totals are stored enrollment rows; Active/Completed/Cancelled are their known-state subtotals.
- Published lessons are currently published lessons under the stored course/sections, regardless of current course publication.
- Learner completed published lessons count each published lesson once if that learner has completed progress. Current percentage is the server's rounded two-decimal percentage; zero published lessons gives0% and incomplete current content.
- Course current-progress numerator is distinct completed enrolled-user/published-lesson pairs across all stored enrollment states, including Cancelled. Denominator is the existing64-bit product of enrollment count and current published lessons. The server's percentage is displayed separately from historical completion.
- Historical enrollment status, completion timestamp and stored percentage are separate fields; current progress does not rewrite or infer them.
- Started/Submitted/Expired quiz counts use stored attempt statuses, including unpublished historical work. Pass/fail uses stored earned marks/passing outcome, not current thresholds. Submitted rows missing a result count as Unavailable, neither passed nor failed.
- Assignment/submission totals include unpublished historical work; grade-row presence distinguishes Graded from Ungraded, including graded zero. Course totals need not equal sums of a filtered/paginated learner page.

The report's recent assignment link is a navigation shortcut, not an official selected/latest grade. Angular initially renders a validation button, not an assessment link. On demand, BFF resolves the selected course/section/lesson/assignment/submission and verifies that the submission belongs to that report learner. It returns only checked IDs. Angular verifies canonical GUIDs and equality with the report link before constructing the fixed existing staff review route. The destination independently reauthorizes review, grading and download. List loading introduces no per-learner API requests; the extra validation requests happen only when the user asks to enable a link. Staff quiz detailed results remain owner-only; no report quiz-result bypass was added.

## Audit contract and safe metadata

Audit query fields are `page`1–10000, `pageSize`1–50, optional `fromUtc`, `toUtc`, `action`, `outcome`, `actorId`, `resourceId`. Dates require zero UTC offset. Existing API defaults to the last7 days ending now, with a maximum31-day positive window. Start is inclusive, end exclusive. Actor/resource filters use nonempty GUIDs, not email search. Exact action/outcome allowlists come from existing `AuditDisplay`; there is no arbitrary payload search. Angular retains the normalized server range while paging. API ordering remains timestamp descending then event ID descending. A known detail can be read outside the current list window if still authorized.

`BffAuditRow` contains `id`, `timestampUtc`, nullable `actorUserId`, `action`, `resourceType`, `resourceId`, `outcome`, validated GUID `correlationId`, and a typed `metadata` dictionary. The upstream raw metadata JSON string is never serialized into the BFF response. Existing API/client scope/action/outcome checks are followed by a presentation-boundary allowlist matching the documented domain contract: bounded512-character object, unique string keys, valid roles/publication booleans/nonempty GUIDs and correct per-action keys. Invalid/unknown/duplicate/secret-bearing metadata is rejected with a sanitized error before any list rows/counts serialize.

Allowed metadata fields are only `oldRole`, `newRole` (Student/Instructor/Admin), `oldPublished`, `newPublished` (`true`/`false`), and GUID fields `targetUserId`, `oldInstructorId`, `newInstructorId`, `relatedResourceId`. Membership actions use role/target keys; invitation creation/reissue uses newRole/relatedResourceId; Instructor change uses old/new Instructor IDs; publication actions use old/new booleans; accepted invitation/submission/grade/enrollment transitions use relatedResourceId; other actions have empty metadata. Organization audit cannot return account/platform events. No email, password/hash, token/hash, answer, submission text, feedback, marks, filename, path, request body or diagnostic is accepted as metadata. Rendering uses text interpolation/KeyValuePipe, with no HTML bypass or relaxed CSP.

This is an immutable-history viewer. Existing audit coverage, transactional success-event behavior, deferred denial/conflict auditing and delivery-observation limitations remain as documented in `audit-trail-viewer.md`; this batch does not fabricate missing events or outcomes.

## API gaps and remaining limits

- No necessary business API is missing for this batch. No new business endpoint or pagination addition was needed.
- Invitations have no detail read API. Listing is bounded; revocation authorizes ID plus selected organization inside the existing upstream write lock rather than scanning every list page. Tokens cannot be recovered. Reissue remains an explicit email/role operation.
- Production email remains blocked by the existing unconfigured sender until an explicit provider is supplied separately. Configuring a public origin alone does not enable delivery. This batch added no provider/package or production fallback/capture exposure.
- Existing per-instance limiter/session architecture is unchanged. Multi-instance distribution, sticky routing/Data Protection, abuse controls and delivery behavior require deployment verification.
- Report counts and page/header reads are not an atomic snapshot and are not reconciled/clamped by Angular. Existing ordering may shift under edits/inserts. Wide-course query cost and real index plans/load are unverified.
- Review validation and destination requests are separate; no atomic read/authorize/navigate guarantee is claimed. Membership/course state may change between reads and guarded writes, where the existing API revalidates it.
- Audit metadata validation is a display trust boundary, not a new audit producer/business rule. Raw payload export, generic resource links and platform/account audit are outside scope.

## Changed files

Frontend additions: `src/app/features/organization-admin/organization-admin.models.ts`, `organization-admin.routes.ts`, `organization-admin.component.ts`, `organization-admin.component.html`, `organization-admin.component.spec.ts`.

Frontend integration: `src/app/app.routes.ts`, `src/app/app.routes.spec.ts`, `src/app/core/auth/auth.guard.ts`, `workspace-routing.ts`, `src/app/layout/application-shell.component.ts`, `src/app/features/staff-authoring/staff-authoring.component.ts`, `staff-authoring.component.html`, `src/app/features/workspace/workspace.component.ts`, `workspace.component.spec.ts`, `README.md`, this report. Workspace descriptions now reflect available screens instead of earlier placeholder copy.

Backend additions: `LMS.Web/Services/BffOrganizationApi.cs`, `LMS.Web/Controllers/BffOrganizationController.cs` (includes the separate typed staff-report controller), `LMS.Tests/Integration/BffOrganizationTests.cs`, mirrored `docs/organization-administration.md`. Existing member/platform, invitation/onboarding, reports/gradebook and audit reports link the new report. No existing MVC/business handler/repository/entity/migration/package was edited by this batch.

## Verification

Final verification on 2026-10-15:

- `npm.cmd run build -- --configuration production`: passed; organization pages remain a lazy component/route chunk.
- `npm.cmd test -- --watch=false --browsers=ChromeHeadless --progress=false`: **189 passed**, including21 new organization/report/audit/routing cases and updated workspace descriptions. Coverage includes payloads, confirmation/CSRF/duplicate prevention, validation retention, safe text, rate/delivery errors, GET-only recovery, draft vs applied filters, pinned audit dates, current/historical count rendering, checked review links, stale responses, logout/expiry and role navigation.
- `dotnet test LMS.Tests/LMS.Tests.csproj --no-restore --filter 'Category!=SqlServerIntegration&(FullyQualifiedName~Bff|FullyQualifiedName~Learner|FullyQualifiedName~Authoring|FullyQualifiedName~Staff|FullyQualifiedName~LessonContent|FullyQualifiedName~Media|FullyQualifiedName~LearningAccess|FullyQualifiedName~Enrollment|FullyQualifiedName~Organization|FullyQualifiedName~Quiz|FullyQualifiedName~Assignment|FullyQualifiedName~Reporting|FullyQualifiedName~Audit|FullyQualifiedName~AccountOnboarding)' -m:1 -p:UseSharedCompilation=false --logger 'console;verbosity=minimal'`: **970 passed**, including22 new BFF cases. Coverage includes live roles/scope, generic account safeguards, selected organization, existing member/Instructor verification, both CSRF boundaries, local/upstream429, invitation body omission, metadata allowlists, UTC bounds, unchanged report counters and a denominator beyond JavaScript's safe integer range, review parents/owner, no mutation replay and existing business/MVC/onboarding/audit/learning regressions.
- `dotnet build EnterpriseLMS.slnx --no-restore --no-incremental -m:1 -p:UseSharedCompilation=false`: passed,0 warnings/errors.
- `node --test LMS.Tests/assignments-ui.test.cjs LMS.Tests/quiz-ui.test.cjs`:2 passed. `node --check` on both tests and every existing `LMS.Web/wwwroot/js/*.js`: passed.
- `git -c core.safecrlf=false diff --check` in both trees and direct whitespace checks of new organization source/tests/report: passed.

New BFF tests use the real in-process MVC/auth/CSRF/rate-limit pipeline with fake upstream HTTP. Angular tests use HttpTestingController and real lazy-route/shell rendering. Existing business tests verify safeguards, report/audit SQL translation and onboarding/audit policies offline. They do not establish live database locks, deployed storage/email, real query plans or browser end-to-end behavior. Existing Karma/Webpack deprecation and backend test-build NU1510 package-pruning warnings remain. Media-fixture404 warnings in prior Angular tests are not live browser validation. No package changes were made to address unrelated warnings.

Branches remain `chore/angular-dependency-upgrade` and `feature/learning-flow-stability`; earlier changes remain present. Nothing was staged, committed or pushed. No branch switching, database access, migration application, schema change, package installation or normal hosts occurred. Browser end-to-end behavior, actual email delivery/provider configuration, deployed migration state and SQL transaction/query behavior remain unverified.

## Migration/deployment prerequisites and later manual checklist

No package, schema or migration change was added or applied. Do not assume pending migrations were applied. Review the full existing chain, especially `20261004061629_AddLessonAssignments`, `20261006122454_AddOrganizationInvitations`, `20261006135202_AddAuditEvents`, and their reviewed predecessors. Reporting reads depend on assessment tables; audited critical business writes depend on audit persistence. A passing offline build does not prove those tables exist in a deployment. No database was accessed, inspected, seeded or migrated.

Keep the existing trusted HTTPS API/BFF origin, protected server session and Data Protection configuration, durable private storage/backups, production email-provider prerequisites and proxy/logging exclusions. Invitation/account tokens, request bodies and Authorization headers must stay out of deployment logs/traces. Existing Development private captures remain private and are not an Angular administration resource.

One manual checklist for a separately authorized disposable deployment:

1. Review actual migration state, backups, session/Data Protection/HTTPS and email-provider prerequisites without assuming pending migrations are deployed.
2. As Admin/selected SuperAdmin, test member pagination/search/roles and exact-email assignment, including unavailable/ambiguous/global-platform targets. Cancel/confirm changes; exercise last-Admin/self-demotion, assigned-Instructor and Active-enrollment safeguards. Test direct foreign member/course/Instructor IDs and role changes during reassignment.
3. Create/reissue/revoke invitations with confirmation, inspect lifecycle and delivery timestamps, simulate provider/delivery acknowledgement failures and recover by reads plus deliberate reissue only. Verify no tokens/hashes/links in BFF responses, UI, redirects or logs. Exercise existing10/minute local/upstream rate limits and sanitized429 responses.
4. As Instructor, verify assigned-course reports only; deny member/invitation/audit administration. As Student deny all staff pages. As SuperAdmin require explicit organization selection; changing query/header organization authority must not select another scope.
5. Verify report filters/order/counts against authorized stored records: all enrollment states and unpublished history, zero lessons, current denominator/percentage vs historical completion, stored pass/fail/unavailable and graded-zero/ungraded counts. Validate review parents/owner before enabling links; revoke access before opening the destination. Do not infer an official course grade.
6. Verify audit UTC defaults, inclusive/exclusive bounds,31-day ceiling, filters/IDs, equal-timestamp deterministic paging, empty results, known details outside the list window and rejection of foreign/platform records. Confirm only allowlisted metadata renders and no edit/delete/export exists.
7. Enter hostile text fixtures, interrupt mutations and delayed reads, double-click actions, change organizations, logout, expire/revoke access and navigate during requests. Confirm safe value retention, GET-only recovery, no automatic replay and no stale member/invitation/count/audit/review-link state in a new context.
8. Verify real responsive/keyboard/screen-reader browser behavior and existing Student catalog/enrollment/viewer/progress/quiz/assignment/grades, staff authoring/grading and MVC flows. Record real SQL concurrency/rollback/query load, delivery and browser behavior separately; all remain unverified here.
