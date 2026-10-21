# Organization foundation and reviewed transition

Membership pagination, exact-email assignment, confirmed role safeguards, and platform organization creation/listing now extend this foundation. See [organization administration](organization-member-platform-administration.md) for current contracts and verification; its route contracts supersede the original request shapes below.

## Schema and identity decisions

One application/database serves all organizations. An organization owns each course;
InstructorId remains a separate assignment. Membership has one explicit role per
(OrganizationId, UserId): Student=1, Instructor=2, Admin=3. A user may hold different
roles in different organizations. Combined learner/staff roles in one organization
are not represented by this model and require a product decision if needed for legacy
users. No course sharing or ownership-transfer API is provided.

Organization code is trimmed/uppercased by the domain and limited to 50 characters;
name is trimmed and limited to 200. SQL uniqueness uses the database collation. The
membership Role check constraint permits only 1/2/3. Membership foreign keys to
Organizations and AspNetUsers, and course ownership foreign keys, use NO ACTION on
SQL Server (EF Restrict), avoiding cascade removal of historical ownership.

Global Identity Admin/Instructor/Student roles are not organization membership.
The existing Identity schema and IdentitySeeder were inspected: role names are
Student, Instructor, Admin, SuperAdmin; the seeder creates role definitions, not
Organization memberships. Actual database assignments were NOT inspected. The
read-only inventory script lists stored role assignments for an authorized reviewer.
Existing global SuperAdmin assignment is the platform trust root and is checked
against stored AspNetUserRoles/AspNetRoles per request, not accepted from JWT alone.
The reviewed backfill below explicitly promotes the approved student2 user to the existing
SuperAdmin role; it creates no role definitions.

## Migration review

`20260928081231_AddOrganizationFoundation` follows the existing unapplied
`20260927040119_EnforceUniqueCourseEnrollment` migration.

Up creates Organizations and OrganizationMemberships, their primary keys, membership
foreign keys/role check, unique IX_Organizations_Code, unique
IX_OrganizationMemberships_OrganizationId_UserId, and a UserId lookup index. It adds
nullable Courses.OrganizationId with no default, and its organization foreign key.
It replaces global IX_Courses_Code with two unique indexes:

- IX_Courses_Code on Code WHERE OrganizationId IS NULL preserves the old restriction
  among legacy/unassigned rows.
- IX_Courses_OrganizationId_Code on (OrganizationId, Code) WHERE OrganizationId IS
  NOT NULL permits code reuse across organizations, not inside one organization.

Up does not INSERT/UPDATE/DELETE data, drop/recreate Courses, seed an organization,
assign memberships, or touch existing enrollment/progress/attempt/answer rows. Every
existing course retains its ID, instructor, fields, and history; its new column is NULL.
Application query filters are runtime behavior, not database row-level security.

Down refuses to proceed if any organization, membership, or non-null course ownership
exists. On an unused foundation schema it removes the new tables/column/indexes and
restores the original global unique course-code index. A populated deployment needs
a separately reviewed export/rollback plan; do not bypass the guard or discard data.
OrganizationId remains nullable. No required-column migration is included.

## API selection and authorization

Send `X-Organization-Id: <organization GUID>` on every scoped learning request,
including SuperAdmin reads/writes. Middleware runs after authentication/routing and
before authorization. It requires one valid nonempty GUID and verifies the active
organization plus stored membership, or stored platform SuperAdmin privilege.
It replaces incoming role claims with the selected membership role and verified
SuperAdmin role. The selection is immutable for the request.

- Unauthenticated/invalid user: 401. Missing/malformed/duplicate organization header: 400.
- Unknown/inactive organization or no membership: 403. Hidden foreign resource IDs
  are filtered from learning queries; endpoint-specific not-found/empty outcomes remain.
- Course public reads now require authentication and selected organization. Null-owned
  courses are hidden. Student course catalog queries return only published courses.
- Existing ownership/publication/enrollment prerequisites still apply to learner
  operations. Organization membership is additional authorization, not enrollment.
- Staff checks use the selected role. Instructor content writes retain course-owner
  checks. Organization Admin cannot select another organization without membership.
- SuperAdmin can select an existing active organization without membership, but is
  not automatically Student. Learner-only operations still require a Student membership.

| Capability | Student | Instructor | Organization Admin | Platform SuperAdmin |
| --- | --- | --- | --- | --- |
| Discover organizations | Own memberships | Own memberships | Own memberships | All active organizations |
| Create organization + initial Admin | No | No | No | Yes, existing user required |
| List/assign members | No | No | Selected organization | Explicit selected organization |
| Assign course Instructor | No | Self, per existing handler | Instructor member of selected organization | Instructor member of selected organization |
| Content writes | No | Owned course in selected organization | Selected organization | Explicit selected organization |
| Enrollment/progress/attempt writes | Existing learner rules | No automatic learner privilege | No automatic learner privilege | No automatic learner privilege |

Organization endpoints:

- GET /api/organizations/me: authenticated discovery; no selection header required.
- POST /api/organizations: platform only; body `{ "code": "ACME", "name": "Acme",
  "initialAdminEmail": "existing@example.test", "confirmed": true }`. Organization and initial Admin
  membership save together. No new Identity user or global role is created.
- GET /api/organizations/{organizationId}/members: Admin/SuperAdmin, matching header, bounded paginated display-field response (see current administration report).
- PUT /api/organizations/{organizationId}/members/{userId}: same scope, body
  `{ "role": 1, "confirmed": true }` (1/2/3). Adds membership or changes its role with serialized safeguards. Self-demotion of an
  acting organization Admin is rejected; no membership deletion/deactivation endpoint
  is included. Last-Admin, assigned-Instructor-course, and Active-Student-enrollment safeguards apply.

Categories remain a global catalog, with writes restricted to platform SuperAdmin.
Auth/user profiles/diagnostic endpoints are not tenant-owned learning resources.
Their broader compatibility with role replacement still needs audit. The global
exception envelope and earlier stability work are retained. Existing Swagger response
attributes have not yet been comprehensively updated for organization-selection errors.

## Reviewed DEMO transition (not executed)

The approved destination is **Enterprise LMS Demo**, code **DEMO**. The
[reviewed SQL template](sql/reviewed-organization-backfill.template.sql) resolves
an existing active DEMO by code and requires its exact approved name. If absent,
it creates DEMO with a new GUID inside the transaction. No LEGACY organization is
created or renamed. The generated GUID is reported; a rolled-back rehearsal does
not reserve it. A committed rerun resolves the same DEMO and preserves its ID.

Approved course ownership (both courses -> DEMO):

- `8d37acf3-2fb2-4eff-915e-a63dbd495ca3`
- `0dea66b2-f17b-42a7-afb7-6ddef2ef8e5a`

Approved DEMO memberships:

| User ID | Organization role |
| --- | --- |
| `0cf651c4-e112-4111-955a-0f44153700cb` | Instructor (2) |
| `2452b8c2-799c-48fa-854c-36ccf00e5bf8` | Student (1) |
| `40002941-c2cc-4575-8ef7-afa68bc73929` | Admin (3) |
| `e8b90eab-54ac-48e7-b71b-bf778449fb7e` | Admin (3) |

For `60a4c3be-0e91-47be-a3ac-def38822b6ec` (student2), delete only the
existing global Student assignment and insert the existing global SuperAdmin
assignment. Create no DEMO membership for this user. The initial accepted state
is exactly Student-only; exactly SuperAdmin-only is accepted for an idempotent
rerun. Missing roles, both assignments together, or any other global role on
student2 cause failure. Other users' global assignments remain untouched and do
not determine their organization memberships. Other organizations/memberships
are untouched.

### Fail-closed checks and remaining assumptions

- All five approved user IDs must exist. IDs are the approved identity keys;
  usernames/emails for four users and the exact stored username for student2
  were not supplied or inspected. Safe usernames are included in verification
  output; the descriptive label student2 is not guessed into a SQL predicate.
- The complete Courses table must contain exactly the two approved IDs. Both
  must still have the approved Instructor user as InstructorId and ownership
  must be NULL or DEMO. This instructor relationship is an explicit assumption
  enforced by the SQL; a different instructor requires further review, not an
  automatic reassignment. Course codes/titles were not supplied, so they are
  reported before/after rather than compared with invented expected values.
- The complete global role catalog must contain exactly Student, Instructor,
  Admin and SuperAdmin with their matching uppercase NormalizedName values.
  Role IDs are resolved from these existing definitions; none are fabricated.
  Additional/missing/renamed definitions abort the transaction.
- Existing DEMO must be active with the approved code/name. Existing DEMO
  memberships may be any subset of the four approved mappings, but unexpected
  members (including student2) or conflicting roles abort. Missing approved
  memberships are inserted; existing matching ones retain their IDs.
- Foundation schema, constraints and the prerequisite unique-enrollment migration
  must already be deployed through a separately authorized process. There is no
  schema deployment in this backfill. Use a reviewed target, tested backup and
  stopped application writes; UPDLOCK/HOLDLOCK retain validation locks through
  transaction completion. Review custom database triggers separately: this
  repository cannot establish whether the target has additional side effects.

### Execution and verification procedure (future authorization required)

1. Obtain target/execution authorization and review a fresh
   [read-only inventory](sql/inventory-organization-transition.sql), including
   migration history, role assignments, instructors, indexes and history counts.
   Rehearse on a disposable SQL Server copy after separately authorized schema
   deployment. No database was connected to for this plan update.
2. Run the entire template as one batch in a dedicated session without an open
   transaction. `@Commit = 0` is the default: it performs tentative writes and
   verification inside the transaction, then explicitly **ROLLBACKs** everything.
   It is not a read-only query and still requires execution authorization.
   Mapping approval is already recorded above; there are no unfilled mapping
   placeholders or additional mapping-approval switches.
3. Review the BEFORE and AFTER result sets for DEMO, both courses, the five users'
   global roles and DEMO memberships. AFTER assertions require two DEMO courses,
   the four exact memberships, and only global SuperAdmin for student2 with no
   DEMO membership. The access-gap query reports enrolled users without a Student
   membership; student2 may appear intentionally and retains stored history but
   has no Student learner privileges. SuperAdmin can select active DEMO for
   platform/staff access without membership.
4. Only with explicit authorization for persistent execution change `@Commit`
   to `1` and rerun the whole batch. Matching committed state is a no-op; partial
   compatible ownership/membership state is completed. No ownership transfers,
   course/instructor ID changes, updates to existing memberships or changes to
   other global assignments occur.
5. `SET XACT_ABORT ON` and TRY/CATCH protect the single transaction. A failed
   guard or runtime statement enters CATCH, rolls back any active transaction
   using `XACT_STATE()`, and rethrows the original error. Thus a failure after
   Student removal also reverses that removal and every earlier write. Compile
   errors or client cancellation can bypass CATCH: use the same dedicated session
   to execute `IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;` or disconnect it. Never
   commit a failed/open transaction. The script rejects an outer transaction so
   its rollback cannot discard unrelated caller work.
6. Compare a fresh read-only inventory after completion with the pre-run report.
   A dry run must show the original state; a commit must show the approved maps.
   AFTER output inside the transaction alone does not prove persistence. Compare
   enrollment, lesson-progress and quiz attempt/answer counts and timestamps.
   The SQL does not write those tables and does not alter historical access data.
   SQL execution, actual rollback, triggers and concurrency remain unverified
   until an authorized rehearsal. Retain approval evidence and result sets.

Before schema deployment, new code is incompatible with the old database. After
schema deployment but before backfill, NULL-owned courses/history remain hidden
and users without membership or verified SuperAdmin cannot select DEMO. Resume
traffic only after the endpoint checklist passes. Any future NOT NULL ownership
migration needs a separate review; do not rewrite history to satisfy it.

## Offline validation and remaining limitations

From the repository root, tests/build need no new packages:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-restore
dotnet build EnterpriseLMS.slnx --no-restore -p:OutputPath=bin/Verification/
git diff --check
```

Build the Infrastructure default output before EF --no-build commands; its design-time
factory is used without starting the API or opening a database connection:

```powershell
dotnet build LMS.Infrastructure/LMS.Infrastructure.csproj --no-restore
Set-Location LMS.Infrastructure
dotnet ef migrations has-pending-model-changes --project . --startup-project . --context LMSDbContext --no-build
dotnet ef migrations script 20260927040119_EnforceUniqueCourseEnrollment 20260928081231_AddOrganizationFoundation --project . --startup-project . --context LMSDbContext --no-build --output ../docs/sql/organization-foundation-up.sql
dotnet ef migrations script 20260928081231_AddOrganizationFoundation 20260927040119_EnforceUniqueCourseEnrollment --project . --startup-project . --context LMSDbContext --no-build --output ../docs/sql/organization-foundation-down.sql
```

Those SQL artifacts are offline review outputs, NOT applied scripts. The foundation
and learning isolation tests use middleware, strict mocks, actual EF filter expressions
and offline SQL Server command generation; no EF InMemory dependency or database is used.
See [the learning API audit](tenant-isolation-audit.md) for the current endpoint-by-endpoint
evidence matrix, limitations, and the single consolidated future Swagger/disposable SQL
verification checklist. Actual SQL execution, concurrent revocation, uniqueness/locking,
rollback and migration/backfill rehearsal remain unverified. Docker is unavailable.
