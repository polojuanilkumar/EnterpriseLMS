# EnterpriseLMS release deployment and verification

This guide consolidates the assignment, invitation/recovery and audit batches as of
2026-10-06. No existing database was accessed, no migrations were applied, and no
normal API/Web application or manual browser/Swagger session was started for this
batch. Builds, generated SQL and offline tests do not establish deployment readiness.

The project is ready to **attempt isolated deployment testing once its prerequisites
are provided**. It is not approved for production deployment. Manual acceptance
testing must follow an authorized disposable deployment with the migrations and
configuration below; it has not been completed here.

## Migration inventory and evidence

All 22 migrations below exist as generated source and match the current EF snapshot.
“Unknown” means there is no current user confirmation of application, not that the
migration is unapplied. No live migration history was queried. Do not infer that an
earlier migration is applied merely because a later file exists.

| Migration, in required order | Application evidence |
| --- | --- |
| 20260820115608_InitialCreate | Unknown |
| 20260821103457_AddIdentity | Unknown |
| 20260822074254_AddUserProfile | Unknown |
| 20260827172959_AddCourse | Unknown |
| 20260903093444_AddCategory | Unknown |
| 20260907165900_AddCourseEnrollment | Unknown |
| 20260909192412_AddCourseSection | Unknown |
| 20260910093647_AddLesson | Unknown |
| 20260911074628_AddLessonProgress | Unknown |
| 20260917133814_AddQuiz | Unknown |
| 20260920125610_AddQuizQuestion | Unknown |
| 20260923060208_AddQuizOption | Unknown |
| 20260924134637_AddQuizAttempts | Unknown |
| 20260924145638_EnforceSingleStartedQuizAttempt | Unknown |
| 20260927040119_EnforceUniqueCourseEnrollment | Unknown |
| 20260928081231_AddOrganizationFoundation | Earlier documentation records a user report of application and DEMO backfill; not reverified here |
| 20260929151614_AddLessonContentItems | Unknown |
| 20260929160637_AddPrivateLessonMedia | Unknown |
| 20260929175839_AddPdfPresentations | Unknown |
| 20261004061629_AddLessonAssignments | **Known pending, user instruction** |
| 20261006122454_AddOrganizationInvitations | **Known pending, user instruction** |
| 20261006135202_AddAuditEvents | **Known pending, user instruction** |

The earlier user-reported organization/backfill evidence is recorded in
[tenant-isolation-audit.md](tenant-isolation-audit.md). It does not prove the current
environment's schema, data mapping or integration correctness. The assignment,
invitation and audit migrations remain pending until the user provides evidence.

## Offline SQL artifacts and review

Generated with cached EF Core 10.0.11 through `LMSDbContextFactory --offline`, using
the Infrastructure assembly as the startup assembly. The factory ignores application
configuration and rejects connection opening. The API deps/runtime files resolve
dependencies; the API entry point and role seeder are never executed.

| Artifact | Intended review scope |
| --- | --- |
| [release-pending-up.sql](sql/release-pending-up.sql) | From AddPdfPresentations to AddAuditEvents: the three known pending migrations only |
| [release-full-chain-up.sql](sql/release-full-chain-up.sql) | Empty disposable database, all 22 migrations; not an existing-database deployment script |
| [release-pending-down-review-only.sql](sql/release-pending-down-review-only.sql) | Downgrade risk review only; not a recommended rollback procedure |
| [release-verification-read-only.sql](sql/release-verification-read-only.sql) | Operator-run history, aggregates, internal IDs, duplicates, membership consistency and index verification |

The pending Up creates only assignment/submission/grade, invitation and audit tables
and their constraints/indexes plus migration-history entries. It does not modify
existing accounts, global roles, memberships or learning history and does not backfill
fabricated audit events. Assignment relationships use `NO ACTION` to retain submitted
work and grades. Invitations reference organization/inviter with restricted deletion,
have rowversion concurrency, a unique token-hash index and a filtered unique pending
organization/normalized-email index. Audit records deliberately have no actor,
organization or resource foreign keys: historical IDs survive resource/account
deletion. Audit scope has a check constraint, bounded columns, paging indexes and
unique `(OperationId, Action, ResourceType, ResourceId)` protection.

Each migration has its own transaction/history insert and `GO` batches. The three
migrations are **not one all-or-nothing deployment**. Scripts are not idempotent and
assume the exact stated starting migration. Review actual history before choosing a
range. Use tooling that handles `GO` and stops on errors; never blindly execute the
full chain on an existing database. SQL generation has not verified SQL Server syntax,
locking, permissions, transaction behavior or compatibility in a real environment.

Regenerate after an offline build, from the repository root:

```powershell
dotnet build .\EnterpriseLMS.slnx --no-restore "-p:OutDir=$PWD/artifacts/release/bin/"
.\scripts\generate-release-sql.ps1
```

No script here executes deployment SQL or runs `database update` against an existing
environment. Schema application requires separate operator authorization.

## Deployment sequence and rollback limits

1. Identify the exact target environment independently. Obtain an authorized,
   read-only migration/data inventory and compare the actual `__EFMigrationsHistory`
   with this chain. Verify database engine compatibility and all prerequisites.
2. Take and test a restorable database backup and coordinated private file/key backups.
   Record aggregate learning counts before deployment. Database backups alone cannot
   restore uploaded media, assignment PDFs or protected sessions/tokens.
3. Schedule a maintenance window. Drain requests and stop writers, background work and
   every instance that could mutate memberships, invitations, assignments or learning
   state. Avoid running old and new API versions against partial schema upgrades.
4. Review legacy ownership/membership mappings before making legacy data visible.
   Foundation leaves `Courses.OrganizationId` nullable; null-owned courses are hidden.
   Existing global roles do not establish organization membership. Use the existing
   [read-only inventory](sql/inventory-organization-transition.sql) and
   [explicit reviewed backfill template](sql/reviewed-organization-backfill.template.sql)
   only with approved organization/course/user mappings. Preserve global SuperAdmin
   and all learning history. Resolve duplicate enrollments/Started quiz attempts through
   a reviewed history-preserving plan before their unique indexes; do not auto-delete.
   Ensure assigned instructors have Instructor membership and Active learners have
   Student membership in the correct organization. Do not rerun a reported backfill
   without checking stored state. The three new migrations require no learning backfill.
5. After separate approval, apply only missing migrations in the order above. If
   AddPdfPresentations is not the confirmed current migration, the pending-range
   script is insufficient; review/regenerate the exact missing predecessor range offline.
6. On any failure or uncertain commit, stop. Use read-only history/schema/data checks
   to determine what committed; do not automatically replay the deployment or edit
   migration history. A lost acknowledgement is not proof of rollback.
7. Verify history, indexes, constraints and before/after learning counts using the
   read-only verification script, running table-specific queries only once their
   migrations are present. Investigate any mismatch; empty duplicate queries alone
   do not establish authorization or transaction correctness.
8. Configure and validate storage, keys, email delivery, HTTPS and sessions before
   opening traffic. API startup calls `IdentitySeeder.SeedRolesAsync`; it creates
   missing named roles and does not assign accounts/global privileges or auto-migrate.
   Plan role initialization/permissions explicitly. Neither integration composition
   invokes that seeder. Preserve existing users and role assignments.
9. Require fresh login, then run the consolidated checklist below in the disposable
   deployment first. Resume production traffic only after separate acceptance and
   production-blocker resolution.

Prefer a forward correction or a tested backup restore with an explicit recovery
point. Restoring loses writes after that point and must restore compatible files and
keys. Do not interpret generated Down SQL as safe history-preserving rollback:

- Audit Down throws while any audit history exists. Application append-only guards do
  not make the table tamper-proof against database administrators.
- Assignment Down throws while submissions or grades exist. If empty, it still drops
  assignment definitions. Retain learning history; do not delete rows to bypass guards.
- Invitation Down has **no populated-table guard** and drops invitation history. A
  downgrade after use requires a separately reviewed preservation/recovery plan.
- Organization foundation Down refuses populated ownership/membership/organization
  data. Returning to a globally unique course code can also conflict across tenants.
- PDF Down narrows constraints and fails when retained PDF records violate them.
  Earlier media/content/learning-table drops can discard retained history; do not
  chain downgrades without reviewing every Down and file dependency.

## Configuration and durable infrastructure

Required key names below contain no secret values or connection strings. Supply
environment-specific secrets through the deployment secret store, not source control.

| Key | Purpose and requirement |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | Authorized application SQL Server connection, configured only for deployment; never read by integration tests |
| `Jwt:Key` | High-entropy HS256 signing secret, at least 32 bytes; identical across the intended API instances |
| `Jwt:Issuer`, `Jwt:Audience` | Exact trusted validation values |
| `Jwt:AccessTokenMinutes` | Positive bounded access lifetime selected for the environment |
| `AccountLinks:PublicUrl` | Trusted absolute HTTPS public **Web** origin, no credentials/query/fragment; invitation/reset links never use Host |
| `LmsApi:BaseUrl` | Web-to-API trusted HTTPS origin, trailing root path, no credentials/query/fragment |
| `ASPNETCORE_ENVIRONMENT` | `Production` for deployment; Development alone enables private email capture |
| `ASPNETCORE_URLS` / hosting bindings | Explicit HTTPS hosting endpoints/certificate configuration |
| `AllowedHosts` | Approved deployed host names, with proxy routing verified |
| `Logging:LogLevel:Default`, `Logging:LogLevel:Microsoft.AspNetCore` | Restrict diagnostics; upstream systems must also redact sensitive URLs/headers/bodies |

HTTPS launch profiles are API `https://localhost:7196` and Web
`https://localhost:7207`; their HTTP companion URLs are not account-link targets.
These are development bindings, not production URLs or certificate validation evidence.
Web outbound redirects/cookie forwarding and HttpClient loggers are disabled. Configure
proxy TLS/trusted forwarding deliberately: the current startup does not supply an
environment-specific trusted-proxy policy. Do not trust arbitrary forwarded headers.

Both hosts call `AddDataProtection()` with default persistence behavior. There are
**no implemented `DataProtection:KeyPath`, distributed-session, private-storage-path,
or production-email-provider configuration keys**. Adding fictional settings will not
configure these facilities. Production must arrange an explicitly durable, protected
key ring with stable application discrimination and tested restore/rotation; each
API replica must share its API ring and each Web replica its Web ring. Their purposes
remain separate. Default machine/profile storage is not proof of durable container
or multi-instance configuration. Hosting composition must configure persistence and
at-rest key protection or demonstrate a supported durable host arrangement first.

Web JWTs stay in encrypted `LearnerSessions` **IMemoryCache**, never browser tickets,
local storage or HTML. Opaque session IDs are carried in Secure/HttpOnly/Strict cookies.
Cookie validation calls the API every authenticated request and rejects/removes sessions
when the JWT is revoked or the API cannot validate it. Process restarts lose sessions.
Account-link acceptance/reset sessions also use encrypted process-local memory, last
20 minutes and use purpose-specific protected cookies. Multiple Web instances require
shared session/link storage implementation and verification; shared keys or sticky
routing alone do not provide shared sessions. Current code has no such implementation.

API JWT validation checks signature, issuer, audience, expiry with zero clock skew,
and `lms-stamp` against the stored Identity security stamp on every validated token.
The stamp check is shared in `JwtSecurityStampValidation` for isolated testing; normal
startup behavior is unchanged. Legacy tokens without this claim are rejected. Password
reset rotates the stamp and invalidates prior JWTs and web sessions on next use;
existing cookies are not remotely erased from a browser. Fresh login is mandatory.
Reset does not automatically log in. Synchronize host UTC clocks. Reset tokens use
Identity/Data Protection and expire in one hour; invitations expire in 48 hours and
store only hashes. No raw tokens/hashes/stamps/passwords belong in audit records.

Private paths are fixed under API content root:

- `App_Data/PrivateMedia`: synthetic-tested private audio/video/PDF storage.
- `App_Data/PrivateAssignments`: private submitted PDF attachments.
- `App_Data/PrivateEmails`: Development-only captured sensitive email links.

Provision durable mounts at the actual runtime content root for retained media and
assignments, service-owner permissions, sufficient quota, backups and restore tests.
Never expose these through static files, proxies or public endpoints. All three paths
are Git-ignored and explicitly removed from API publish Content/None items; publishing
must not transport private runtime data. Staging/publishing files and SQL are not one
atomic transaction; uncertain commits may leave retained/orphan files. Do not add
automatic deletion based on uncertain outcomes. Capture uses generated filenames,
owner-only ACL/modes and has no public endpoint. Access capture only as the local
service owner, treat it as credential material, and manually remove files after test
use according to an approved short retention period. No retention job was added.

Production registers `UnconfiguredAccountEmailSender`, which refuses delivery. There
is no built-in production provider or key that silently enables one. An explicit
`IAccountEmailSender` implementation/registration and provider operational verification
are a **production blocker**, not supplied in this batch. Tests use an in-memory fake
and never send real messages or use development capture.

## Transaction guarantees and recovery

Critical database success audit events are staged and saved in the same business
transaction. Explicit multi-save invitation/Identity transactions retain that ownership;
audited hooks do not independently commit an event. Operation IDs prevent duplicate
success records. Audit persistence errors propagate and business owners must not commit
after failure. SQL Server lock scheduling, deadlocks and rollback remain unverified here.
Selected administrative failure-event auditing is deferred; no generic middleware
infers successful mutation from HTTP status. Existing rules/locks remain unchanged.

Invitation persistence precedes email delivery. Delivery success/failure/unknown is
recorded separately after observation through a fresh context; delivered timestamp is
not set when sending fails. Provider return is an acknowledgement, not proof of inbox
receipt. Missing-provider refusal is failed; arbitrary transport exceptions are unknown.
Observation persistence or post-send acknowledgement can also fail. Database/email
atomicity is not claimed. Inspect the scoped invitation list and delivery audits, then
explicitly confirm reissue to rotate/invalidate the old token. There is no automatic
replay or durable retry worker; this is a recoverable workflow, not an outbox.

After ambiguous acceptance inspect the authenticated organization list/invitation state;
after ambiguous reset try fresh sign-in or request a new recovery email. GET links
never consume/reset; mutations require antiforgery-protected POST. Matching verified
existing membership consumes an invite; conflicting roles require the established
role-change workflow. New-user account, membership, email confirmation, consumption
and acceptance audit are one transaction. Inviter authority/active organization and
exact identity are revalidated. Account creation outside invitations remains closed.

Audit viewer is read-only: organization Admin reads selected organization only;
Instructor/Student denied; stored SuperAdmin reads platform/account only at platform
routes and must explicitly select an organization for organization history. Platform
views do not aggregate every tenant. Metadata allows only old/new organization role,
old/new publication boolean and internal target/instructor/related-resource GUIDs,
bounded to 512 characters. No audit deletion/export/retention/chaining was added.

## One consolidated smoke/acceptance checklist

Execute only after explicit approval in the isolated deployment. Use synthetic users,
two organizations A/B, distinct instructors/learners, synthetic text and synthetic files.
Keep results with the migration/configuration inventory; no manual step was run here.

- [ ] Confirm backup restore, actual migration history, constraints/indexes, reviewed
  backfill and unchanged learning counts; confirm all three pending migrations applied
  only to the authorized test deployment.
- [ ] Verify HTTPS Web/API/certificates, trusted link origin, proxy redaction, private
  paths/publish exclusions, key persistence/rotation/restore and session restart behavior.
- [ ] Verify login/logout, registration closure, generic known/unknown forgot responses,
  bounded rate limits and expired/weak/reused reset denial. Successful reset requires
  fresh login and rejects old/legacy JWTs and every prior web session on next use.
- [ ] Verify SuperAdmin platform navigation and stored privilege, explicit organization
  targeting; Admin A cannot list/count/view/mutate B, including guessed nested IDs.
  Instructor ownership and Student-only learner access remain enforced.
- [ ] Verify organization creation, initial Admin, exact-email membership assignment,
  role changes, last-Admin/self-demotion protections, assigned Instructor and Active
  learner safeguards. Race role changes against instructor assignment and enrollment.
- [ ] Verify invite creation/reissue/revocation confirmation, one pending per email/org,
  48-hour boundary, current inviter authority, active organization, GET non-consumption,
  clean token-free redirect, no external resources, no-store/referrer protections and
  antiforgery. New accounts start confirmed only after committed acceptance. Matching
  verified existing email accepts; wrong/unverified/ambiguous email or conflicting role
  denies safely; concurrency creates one account/membership/consumption, never SuperAdmin.
- [ ] Verify fake/provider delivery success, explicit failure, unknown transport outcome
  and audit persistence failure separately. Inspect state before confirmed reissue;
  verify no token/password/body logging and no automatic replay after uncertain results.
- [ ] Verify course/section/lesson authoring, instructor reassignment, publication,
  private audio/video/PDF upload/play/download, MIME/signature/size limits, range/HEAD
  handling and foreign/unpublished/no-enrollment download denial.
- [ ] Verify catalog/enrollment lists, duplicate-enrollment race, quiz authoring/attempt
  scoring/single Started attempt, progress prerequisites, cancellation/completion race,
  valid final state and preserved timestamps. Reports/gradebook/history stay tenant-scoped.
- [ ] Verify assignment publication, final text/PDF submission, deadline boundary,
  immutable snapshots/frozen fields, duplicate submission, single confirmed grade,
  history and authorized attachment download. Force storage/SQL/audit failures and
  inspect rollback/retained-file outcomes without replaying mutations.
- [ ] Verify audit action/actor/resource/outcome/approved metadata after each covered
  mutation; rollback leaves neither mutation nor success event. Multi-save operations
  emit once. Delivery audits remain separate. Viewer filters, UTC max range, deterministic
  paging/scoped totals/foreign detail denial, empty results, expired sessions, encoding
  and navigation work; Instructor/Student denied and no audit mutation route exists.
- [ ] Verify real SQL concurrency/locking/deadlocks, email provider behavior, deployed
  browser cookie/antiforgery flows and multi-instance failover; automated offline checks
  and generated scripts are not substitutes. Resolve all blockers before production.

Feature route inventories and detailed historical decisions remain in
[assignment documentation](lesson-assignments-v1.md),
[invitation/recovery documentation](invitation-onboarding-password-recovery.md),
[audit documentation](audit-trail-viewer.md) and
[integration test instructions/results](release-integration-testing.md). This checklist
is the consolidated release checklist; older feature checklists are background evidence.
