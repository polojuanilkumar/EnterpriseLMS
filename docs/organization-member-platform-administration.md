# Organization members and platform organization administration

The Angular organization administration, reports and audit extension is documented in [organization-administration.md](organization-administration.md). This earlier record remains as policy and implementation history.

This batch extends the existing organization controller, member page, protected sessions, stored-role middleware, and Instructor assignment flow in the current working tree. Prior learner, quiz, assignment, reporting, progress, and media changes and the existing test project remain present. No application was started, database accessed, migration applied, or Git staging/commit/push performed.

## API routes and contract changes

All routes require an authenticated API user. Platform roles are resolved from stored `AspNetUserRoles`/`AspNetRoles`, not a JWT SuperAdmin claim. Organization roles come from stored membership after middleware replaces global JWT role claims. Scoped routes require a single `X-Organization-Id` matching the route; an explicit route alone does not select the organization.

| Method | Exact route | Contract |
| --- | --- | --- |
| GET | `/api/organizations/me` | Existing active organization discovery for the organization chooser; unchanged |
| GET | `/api/organizations/{organizationId:guid}/context` | Existing selected-org authorization; unchanged |
| GET | `/api/organizations/platform/context` | Verified stored platform authorization: user ID and `isSuperAdmin`; no selected organization required |
| GET | `/api/organizations` | Platform-only paginated organizations, including inactive organizations; `page`, `pageSize`, `search` |
| POST | `/api/organizations` | Existing creation route extended to `{code,name,initialAdminEmail,confirmed:true}`; returns 201 with organization, not account details |
| GET | `/api/organizations/{organizationId:guid}/members` | Existing route now returns a bounded page of member display fields; `page`, `pageSize`, `search`, `role` |
| GET | `/api/organizations/{organizationId:guid}/members/{userId:guid}` | Scoped existing-member display data; 404 if user is not a member of this organization; used to validate Instructor choices beyond the first page |
| POST | `/api/organizations/{organizationId:guid}/members` | `{email,role,confirmed:true}`; add an existing account by exact email; 201, no resolved user ID in creation response |
| PUT | `/api/organizations/{organizationId:guid}/members/{userId:guid}` | Existing assignment/upsert route retained with `{role,confirmed:true}`; now uses the same serialized safeguards as email creation; 200 |
| PUT | `/api/Courses/{courseId:guid}/instructor` | Existing scoped assignment route; stored parent course and target Instructor checks retained and coordinated with role changes at save |

The member GET response changes from an array of membership entities to `ApiResponse<OrganizationListPage<OrganizationMemberRow>>`: `organizationId`, `page`, `pageSize`, `total`, `items`, `pages`. Member items contain only organization/user IDs, first/last names, email, and the membership role. Platform pages use the same page shape with `organizationId=null` and only organization ID/code/name/active state. These existing-route contract changes require external callers to update: organization creation replaces `initialAdminUserId` with exact email; mutations now require explicit confirmation; member callers consume the page envelope. There is no alternate directory API or unguarded legacy HTTP route.

Role values remain Student=1, Instructor=2, Admin=3. SuperAdmin is never an organization role, assigned or revoked by these endpoints. Platform SuperAdmin accounts are unavailable as membership/initial-Admin targets, including through the retained ID-based assignment endpoint. Creation does not grant the platform actor a membership or change their selected organization.

Invalid fields/filter/role/confirmation return 400; verified privilege failures 403; scoped missing member/course 404; unavailable/ambiguous account, duplicate membership/code, and role safeguards 409. Authentication expiry is 401. Privileged rate limit rejection is 429. Other SQL/infrastructure failures remain unexpected generic errors; the MVC flow treats an uncertain outcome as requiring read-only recovery.

## MVC routes and behavior

| Methods | Exact route | Behavior |
| --- | --- | --- |
| GET | `/platform` | Stored-SuperAdmin-verified platform home; independent of selected org |
| GET | `/platform/organizations` | Paginated organization listing/search; explicit organization selection buttons |
| GET, POST | `/platform/organizations/create` | Existing initial Admin email, validated code/name, and required confirmation |
| GET | `/admin/{organizationId:guid}` | Existing selected-org home, with verified SuperAdmin platform navigation |
| GET, POST | `/admin/{organizationId:guid}/members` | Paginated/searchable/role-filtered roster; confirmed add/change forms |
| GET, POST | `/admin/{organizationId:guid}/courses/{courseId:guid}/instructor` | Existing assignment form now has bounded Instructor search/paging; target membership remains API-authoritative |
| GET, POST | `/staff/{organizationId:guid}/courses/new` | Existing authoring; bounded Instructor chooser for Admin/SuperAdmin, current stored target validation |
| GET | `/account/home` | Existing selected-org refresh; an unselected verified SuperAdmin opens `/platform`, other unselected users open the chooser |
| GET, POST | `/account/organizations` | Existing explicit, verified selection and session rotation; platform link added |

All mutations use antiforgery and existing server-side API token headers. Platform listing uses no organization header even when the session currently has a selected org. Management of an existing organization always uses explicit selection through the existing antiforgery-protected selection action. No GET rotates selection, creates memberships, changes roles, or updates learning/access timestamps. The existing selected-home authorization/session refresh remains; new pages/actions revalidate stored API access rather than trusting cached cookie/session role metadata.

Creation and membership success use POST/Redirect/GET with a safe notice. Timeout, network, malformed-response, and 5xx failures redirect to the organization list/member roster with an unknown-outcome notice. The operator must inspect stored state before deliberately making another request. There are no retries, mutation replay, uploads, or automatic organization selection. A canceled client connection cannot receive the recovery redirect; a subsequent independent GET is the recovery path.

Validation/conflict errors retain safe code/name/email/role values; confirmation must be checked again. Member errors retain the submitted roster page/search/role filter. Success/recovery returns to the unfiltered roster/list so the operator can inspect stored state. Instructor chooser GET filters reload unsaved course form values; the page labels this behavior and tells the operator to choose/filter before entering course details. Selection validation uses the scoped member-detail API, not membership in a truncated page. Razor encodes names, emails, code, search, notices, and form errors. Only allowlisted public administration messages may be reflected from API errors; other diagnostics are replaced with a generic message.

## Authorization matrix

| Operation | Student / Instructor membership | Organization Admin membership | Stored platform SuperAdmin |
| --- | --- | --- | --- |
| Member roster/detail/create/role assignment | Denied | Only selected, matching organization | Only explicitly selected, matching organization |
| Platform list/home/create | Denied | Denied even with a global Admin JWT claim | Permitted without selection; stored privilege verified on each request |
| Course Instructor reassignment | Existing Admin-only restriction retained | Stored course must belong to selected org; target must be its Instructor member | Same stored-parent/target checks after explicit org selection |
| Global SuperAdmin grant/revoke | No endpoint | No endpoint | No endpoint in this batch |

The same user may be Admin in A and Student in B; A's authority never grants B's administration. Lists and filtered totals are built from repository-scoped membership joins. A member-detail query uses both organization and user IDs; no user record is returned for a nonmember. Explicit organization predicates replace query filters only in safeguard queries, which resolve enrollment ownership through stored courses. No credentials, security/concurrency stamps, tokens, submitted work, grades, attachments, or another organization's memberships are projected into roster responses.

## Role changes, atomic writes, and concurrency

1. A different role for the last organization Admin is rejected; another Admin must first exist. The earlier restriction against an acting Admin demoting their own Admin membership is retained even if another Admin exists.
2. Changing an Instructor to Student/Admin requires **no assigned course** in that organization. Draft, unpublished, archived, and published courses all count. Course ownership comes from the stored `Courses.OrganizationId`; assignments in other organizations do not block this organization.
3. Changing a Student to Instructor/Admin requires **no Active enrollment** in this organization. Completed and Cancelled enrollment history is retained and does not block a change. Resolving enrollments uses existing guarded actions; this batch adds no cancellation, reactivation, or deletion policy.
4. Keeping the same role is allowed; no learning records or memberships elsewhere are modified. Role changes may remove present access to historical learning records under the existing role-based read policies, but do not delete or rewrite those records.
5. Both retained ID assignment and email addition pass through `ChangeMembership`. It opens a Serializable transaction, takes `UPDLOCK,HOLDLOCK` on the stored organization row, rechecks active organization and actor's stored Admin/SuperAdmin authorization, rereads the target membership without tracked middleware state, runs scoped prerequisite queries, saves, and commits. Missing/ambiguous/platform accounts and email duplicate memberships use a generic conflict. The ID endpoint retains its earlier explicit-ID upsert behavior, with confirmation, unavailable-account handling, and the same safeguards; the web add flow uses email exclusively.
6. `LMSDbContext` coordinates course creation/Instructor changes and enrollment insertion/transition to Active with the **same organization row lock**. A save owns a Serializable transaction if no outer transaction exists; otherwise its lock lasts through the outer commit. It rechecks actual target membership and course actor authorization under the lock, then performs the existing parent/write validation and save. A stale pre-lock role lookup therefore cannot assign a demoted Instructor or create/reactivate an enrollment for a non-Student after the role change commits. Existing completion, publication, enrollment, scoring, and grading rules are unchanged.
7. Organization creation validates the initial membership parent/role and saves the organization and initial Admin membership in one explicit Serializable transaction/EF save. It rechecks the initial user and excludes stored platform accounts. No account or global role is provisioned. SQL uniqueness translation targets only `IX_Organizations_Code` and `IX_OrganizationMemberships_OrganizationId_UserId` with 2601/2627; similarly numbered errors on unrelated indexes are not treated as expected conflicts. Other errors are not automatically retried.

These are implemented SQL Server locking protocols, **not proven SQL Server concurrency behavior**. Offline tests generate parameterized lock/query SQL and test the production prerequisite rules/projections. They do not execute transactions, demonstrate rollback, test deadlock behavior, or verify commit acknowledgement. Organization-wide serialization intentionally reduces concurrent write throughput; lock waits/deadlocks/timeouts and database plans require later integration/load testing. Direct SQL/other future code that bypasses these repository/DbContext paths must independently honor the protocol. EF/database configuration must not add automatic retries of these mutations without explicit ambiguity/idempotency design.

## Exact email, discovery limitations, pagination, and counting

- Exact email input is trimmed, syntax-checked, Unicode-normalized, and uppercased consistently with the default Identity normalizer; lookup compares `NormalizedEmail` for equality, not prefix/search. Display-name syntax is rejected. Identity's EmailIndex is not unique: a bounded lookup takes at most two IDs and rejects missing/ambiguous matches instead of selecting an arbitrary user. No directory, suggestions, email lookup GET, or cross-organization membership response is added.
- This remains a **privileged account-discovery capability**: successful assignment necessarily demonstrates an assignable account exists, roster display reveals its necessary identity fields after assignment, and timing/error differences cannot guarantee account-existence secrecy. Generic unavailable/conflict responses reduce detail but do not eliminate discovery. The retained privileged ID assignment has the same inherent limitation. Limit administration to trusted operators and monitor abuse through appropriate infrastructure without logging request bodies/tokens.
- The existing web rate-limiting framework is reused with `organization-administration`; the API adds the same named policy. Each application limits member POST/PUT and organization POST to **10 requests/minute per authenticated user**, no queue, HTTP 429. Partitioning falls back to remote address only when there is no identifier. Limits span target organizations within the process, but are instance-local, reset on restart, and are not a distributed anti-abuse guarantee. GET roster search is organization-scoped; it does not search global accounts.
- Default page=1/pageSize=20; page range **1–10000**, page size **1–50**; search max **100 characters**. Search is trimmed substring matching on scoped member first name/last name/email or platform organization code/name. SQL collation determines case/accent semantics. Member `role` accepts 1/2/3; the platform list rejects any role filter. Empty/past-end pages return an empty array and the filtered total.
- Member ordering is LastName, FirstName, UserId; platform ordering Code, Id. Instructor pickers use only role=2, default page size 50, with `page`/`search` on the assignment page and `instructorPage`/`instructorSearch` on new-course authoring. They retain explicit API target validation beyond any one page.
- `total` counts only the same scoped/filter-matching query as `items` before Skip/Take; it is not a global account count. User joins rely on restrictive membership foreign keys. Pages use minimal SQL projections/aggregations, two bounded count/page queries, no per-row API requests and no whole-organization user/history materialization. Count/page queries are separate ordinary reads, not a consistent snapshot; concurrent edits can change counts/items between queries/pages. Stable tie-breakers do not promise snapshot pagination.
- The earlier `/api/organizations/me` organization chooser remains unchanged for compatibility and may return all active organizations available to a SuperAdmin; the new platform administration list is bounded. This legacy discovery path exposes organization metadata only, never a global user directory. Very large chooser datasets remain an existing limitation outside this change.

## Schema and migration prerequisites

No entity, mapping, index, model snapshot, or migration change was needed. Existing foundation unique constraints, membership role check, and restrictive foreign keys are reused. Offline pending-model checking reports no model changes. The migration chain still includes pending `20261004061629_AddLessonAssignments`; it has **not** been applied. This combined working tree requires the full schema including assignments and cannot be deployed to a database lacking those tables. No database state or migration application is inferred from a successful build/model check. Safeguard queries may need future indexes based on actual plans; none are added speculatively.

## Exact changed files in this batch

New:

- `LMS.Application/Features/Organizations/OrganizationAdministration.cs`
- `LMS.Infrastructure/Repositories/OrganizationAdministrationQueries.cs`
- `LMS.Infrastructure/Repositories/OrganizationAdministrationRepository.cs`
- `LMS.Infrastructure/Persistence/LMSDbContext.MembershipWrites.cs`
- `LMS.Web/Models/OrganizationAdministrationModels.cs`
- `LMS.Web/Services/OrganizationAdministrationApi.cs`
- `LMS.Web/Controllers/PlatformController.cs`
- `LMS.Web/Views/Platform/Index.cshtml`
- `LMS.Web/Views/Platform/Organizations.cshtml`
- `LMS.Web/Views/Platform/Create.cshtml`
- `LMS.Tests/OrganizationAdministrationTests.cs`
- `LMS.Tests/OrganizationAdministrationWebTests.cs`
- `docs/organization-member-platform-administration.md`

Updated (preserving earlier changes):

- `LMS.Application/Features/Organizations/OrganizationHandlers.cs`
- `LMS.Application/Interfaces/Organizations/IOrganizationRepository.cs`
- `LMS.Infrastructure/Repositories/OrganizationRepository.cs`
- `LMS.Infrastructure/Persistence/LMSDbContext.Organizations.cs`
- `LMS.Api/Controllers/OrganizationsController.cs`
- `LMS.Api/Program.cs`
- `LMS.Web/Models/StaffModels.cs`
- `LMS.Web/Models/LearnerModels.cs`
- `LMS.Web/Models/AuthoringModels.cs`
- `LMS.Web/Services/LearnerApi.cs`
- `LMS.Web/Services/StaffApi.cs`
- `LMS.Web/Services/AuthoringApi.cs`
- `LMS.Web/Services/LearnerWebRegistration.cs`
- `LMS.Web/Services/LearnerSecurity.cs`
- `LMS.Web/Controllers/AdminController.cs`
- `LMS.Web/Controllers/AuthoringController.cs`
- `LMS.Web/Controllers/AccountController.cs`
- `LMS.Web/Views/Admin/Members.cshtml`
- `LMS.Web/Views/Admin/Instructor.cshtml`
- `LMS.Web/Views/Admin/Index.cshtml`
- `LMS.Web/Views/Authoring/Course.cshtml`
- `LMS.Web/Views/Account/Organizations.cshtml`
- `LMS.Tests/StaffWebTests.cs`
- `LMS.Tests/LearnerWebTests.cs`
- `LMS.Tests/OrganizationFoundationTests.cs`
- `docs/organization-foundation.md`
- `docs/staff-web.md`

The existing tests were retained; fixtures were adapted to the paginated response and required confirmation, the foundation denial test uses the paginated handler, and the existing assignment test now models API-authoritative rejection rather than rejection from a truncated web list. The obsolete unbounded member-list handler/repository path was removed instead of keeping a parallel roster feature. The test project was not recreated or changed.

## Automated verification

Cached dependencies and separate build output `artifacts/membership/bin` were used:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/membership/bin/'
dotnet build EnterpriseLMS.slnx --no-restore -p:OutputPath='C:/Users/poloj/Anil Documents/EnterpriseLMS/artifacts/membership/bin/'
node --test LMS.Tests/quiz-ui.test.cjs LMS.Tests/assignments-ui.test.cjs
git diff --check
```

Verification results:

- Full existing/new .NET suite: **799 passed, 0 failed, 0 skipped** (67 new tests; previous 732 retained).
- Full solution build with `--no-restore`: **0 warnings, 0 errors**.
- JavaScript syntax: all five existing application scripts passed; both Node behavior tests passed. No new JavaScript was necessary.
- Offline EF pending-model check: no model changes since the last migration; pending assignments remain unapplied.
- `git diff --check` and supplementary CRLF-aware no-index whitespace checks across all 40 batch files: passed.

Unit tests use the real production prerequisite functions/projection expressions and compiled Razor, fake HTTP/session services, real antiforgery services, framework query binding, and the SQL Server provider with the existing strictly offline connection stub/interceptors. They cover two-organization role/count isolation, assignment conflicts, resolved/history states, exact email vs prefix/ambiguous lookup, pagination, platform privilege and explicit target scope, atomic write bundling, targeted unique-error classification, guarded API routes, stale authorization, PRG/ambiguity, and encoded forms. They do not connect to SQL Server or start a web/API host.

Offline EF pending-model checking uses the existing design-time factory and Infrastructure startup assembly, avoiding API startup/seeding:

```powershell
dotnet exec --depsfile 'artifacts/membership/bin/LMS.Api.deps.json' --runtimeconfig 'artifacts/membership/bin/LMS.Api.runtimeconfig.json' 'C:/Users/poloj/.dotnet/tools/.store/dotnet-ef/10.0.11/dotnet-ef/10.0.11/tools/net8.0/any/tools/net8.0/any/ef.dll' migrations has-pending-model-changes --assembly 'artifacts/membership/bin/LMS.Infrastructure.dll' --startup-assembly 'artifacts/membership/bin/LMS.Infrastructure.dll' --context LMSDbContext -- --offline
```

Browser behavior, live HTTP/JWT/session/rate-limit deployment behavior, SQL Server execution/rollback/concurrency/query plans/lock waits/performance, and migration deployment are **unverified**.

## Consolidated manual checklist for later authorized execution

1. Review and deploy the full required migration chain in an isolated SQL Server environment, including pending assignments. Verify unique organization code/membership indexes, role constraint, restrictive foreign keys, and unchanged history tables. Exercise atomic creation failure paths/rollback using actual SQL Server; offline SQL generation is not evidence of this.
2. Create two organizations with one user Admin in A and Student/Instructor in B. Verify scoped roster/search/role-filter totals and member detail; foreign IDs, mismatch between header/route, revoked role, forged global claims, inactive org and expired sessions must deny. Ensure none of A's responses expose B's membership/work/security fields.
3. As stored SuperAdmin with no selected organization, verify platform home/list/search/empty/past-end pages and confirmed initial-Admin email creation. Verify code collision, missing/ambiguous/platform account, non-SuperAdmin denial and no automatic selection/membership for the actor. Explicit selection must rotate the existing protected session before member/course administration.
4. Exercise missing antiforgery and missing confirmation for every create/role-change form/API operation. Check safe rendering and retained values using script-like names, code, member names/search, and errors. Verify success notices/PRG and that unknown error diagnostics/tokens never reach HTML or browser storage.
5. Attempt last-Admin and self demotion; resolve by adding another Admin and using another authorized actor. Check Instructor demotion with courses in every publication/status state, then explicitly reassign courses. Check Student change with Active enrollment, then resolve through existing actions; Completed/Cancelled history and all grades/submissions/attempts/progress must remain untouched. Other-organization assignments/enrollments/roles must not change or incorrectly block.
6. Run concurrent role changes, role change versus course create/reassignment, and role change versus enrollment create/reactivation. Verify actual lock protocol, stored-role revalidation, last-Admin preservation, transactional rollback and targeted 2601/2627 handling. Measure organization-lock contention/deadlocks and plans under load; unrelated SQL errors must remain unexpected and must not trigger automatic retries.
7. Disconnect or interrupt acknowledgement after membership/organization saves. Check read-only roster/code recovery, no mutation replay, and fresh confirmation before any deliberate subsequent request. Exercise 429 across multiple organizations for one actor; assess instance-local/distributed deployment limits and privileged account-discovery risk without recording sensitive bodies/tokens.
8. Check bounded Instructor choosers beyond 50 members, scoped target validation on submission, retained form selection on errors, and navigation across account/platform/Admin/staff pages. Recheck existing learner enrollment/progress, quiz, assignment/grading, reports/gradebook and private-media behavior, especially Completed history and authorization after a membership role changes. No new overall grade or completion policy should appear.
