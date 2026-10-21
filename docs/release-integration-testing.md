# Release integration infrastructure and evidence

## Disposable SQL verification, 2026-10-15

This section supersedes the historical execution-blocker statements below. Current
test discovery confirmed 19 `Category=SqlServerIntegration` cases; the offline suite
has grown to 1,139 cases. Docker Engine 29.8.2, context `desktop-linux`, linux/amd64
was verified. The explicitly authorized official image pull cached
`mcr.microsoft.com/mssql/server:2022-latest` at digest
`sha256:cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718`.

Windows PowerShell was blocked by local execution policy. Running the unchanged
wrapper with a child-process-only `-ExecutionPolicy Bypass` exposed a native argument
quoting defect: the trailing output-directory separator escaped the closing quote
for the workspace path containing spaces and swallowed the filter/logger options.
`scripts/run-sql-integration.ps1` now omits that trailing separator. All explicit
opt-in, generated credentials, ownership checks and loopback restrictions remain.
No business-code, authorization, assertion, migration or package changes were needed.

The initial unfiltered opted-in invocation passed all 1,158 cases. The corrected
wrapper then independently passed the complete SQL-only suite: **19 passed, 0 failed,
0 skipped**, including actual migrations, concurrency, constraints and rollback.
The fixture completed ownership-verified cleanup; a subsequent labeled-container
inventory was empty, and the observed run's private temporary directory was absent.
The cached image is retained. No existing database or application connection setting
was used and neither normal application host was started.

Offline regression evidence: **1,139 passed, 0 failed, 0 skipped**; solution build
with `--no-restore` succeeded with three existing NU1510 warnings and no errors;
all five Web JavaScript files passed syntax checks and both Node behavior tests
passed. Offline EF `has-pending-model-changes` reported no changes, using the guarded
`--offline` design-time factory. Tracked and untracked text whitespace checks passed.

Full API HTTP authorization/race responses, real HTTP media/attachment/range/HEAD
transport, browser cookies/TLS/proxies, deployment storage behavior, real email and
multi-instance/network-failure recovery remain unverified. The successful SQL run
does not close those gaps. The sections below retain the earlier batch's historical
scope and evidence.

This batch preserves the existing `LMS.Tests` project and all prior tests. No new
package, Docker installation or image download was performed. Docker is unavailable
on PATH in this environment. Cached packages do not include ASP.NET TestHost or
Mvc.Testing. SQL integration is blocked, not verified or passed. Normal application
hosts, existing databases and production startup seeding were never used.

## Independent offline runs

From the repository root, with existing cached/restored dependencies:

```powershell
dotnet test .\LMS.Tests\LMS.Tests.csproj --no-restore "-p:OutDir=$PWD/artifacts/release/bin/" --filter 'Category!=SqlServerIntegration'
dotnet build .\EnterpriseLMS.slnx --no-restore "-p:OutDir=$PWD/artifacts/release/bin/"
Get-ChildItem .\LMS.Web\wwwroot\js\*.js | ForEach-Object { node --check $_.FullName; if ($LASTEXITCODE -ne 0) { throw 'JavaScript syntax failed' } }
node --test .\LMS.Tests\assignments-ui.test.cjs .\LMS.Tests\quiz-ui.test.cjs
.\scripts\generate-release-sql.ps1
git diff --check
```

The SQL-generation script also runs offline EF `has-pending-model-changes`, using
the guarded design-time factory and Infrastructure startup assembly. It never runs
the normal API entry point or connects to SQL. No model change is not evidence that
the three known pending migrations are applied.

An ordinary unfiltered test run leaves SQL tests discovered but explicitly skipped
unless `LMS_SQL_INTEGRATION` is exactly `1`. The exclusion filter above ensures an
offline run even if that opt-in variable is already set. Test categories distinguish:

- Existing unit, policy, service, Razor and offline SQL-generation checks: no SQL.
- `Category=HostIntegration`: real in-process MVC/authentication/antiforgery pipeline,
  fake API transport, ephemeral test Data Protection, memory sessions and no sockets.
- `Category=SqlServerIntegration`: actual EF migrations, SQL Server storage and real
  transactions/constraints in a newly owned disposable Docker container, explicit opt-in.

The in-process server uses the public ASP.NET `IServer`/`IHttpApplication` contract
instead of downloading TestHost. It executes real Web controllers, compiled Razor,
global antiforgery, cookie authentication and `LearnerSecurity` via existing
`AddLearnerWeb`. Its request/response features model HTTP callbacks and byte streams;
it does not exercise Kestrel transport, TLS, reverse proxies, browser cookie rules,
range transport or real API HTTP authorization. Its configuration is created empty,
the API transport is an in-memory handler, keys are ephemeral and logs are disabled.
No `Program`, application appsettings, runtime capture or production seeder executes.

## Isolated SQL setup and explicit run

Prerequisites, to be provided explicitly by the operator:

1. Docker with a running Linux-container daemon; adequate SQL Server memory/CPU and
   loopback networking, plus SQL Server image/platform support. Docker is not installed
   by these scripts. Developer SQL Server is for non-production tests.
2. A locally cached SQL Server 2022 image. Obtaining the image requires a download and
   accepting its license. No image is pulled automatically. If separately authorized,
   the operator can obtain it explicitly with `docker pull
   mcr.microsoft.com/mssql/server:2022-latest`. Prefer a reviewed cached digest for
   repeatable CI. Do not run this download here without approval.
3. Cached .NET 10 dependencies and the existing test assets. No external SQL instance,
   LocalDB or application connection setting is an acceptable substitute.

Inspect prerequisites without connecting to any database:

```powershell
Get-Command docker
docker info --format '{{.OSType}}'
docker image inspect mcr.microsoft.com/mssql/server:2022-latest --format '{{.Id}}'
```

Only after those prerequisites and explicit disposable-test authorization:

```powershell
.\scripts\run-sql-integration.ps1 -AllowDisposableSql
# Optional reviewed image already cached locally:
.\scripts\run-sql-integration.ps1 -AllowDisposableSql -Image 'mcr.microsoft.com/mssql/server:2022-latest'
```

Direct invocation is also available, with opt-in restored/removed afterwards:

```powershell
$env:LMS_SQL_INTEGRATION = '1'
try {
  dotnet test .\LMS.Tests\LMS.Tests.csproj --no-restore "-p:OutDir=$PWD/artifacts/release/bin/" --filter 'Category=SqlServerIntegration'
  if ($LASTEXITCODE -ne 0) { throw 'Blocked or failed SQL integration; not a passing run' }
} finally { Remove-Item Env:LMS_SQL_INTEGRATION -ErrorAction SilentlyContinue }
```

The wrapper preserves previous environment values. Missing daemon/image is an error
on opted-in runs, not a pass. Without opt-in tests are skipped with an explicit reason.
Neither entry point accepts a connection string or reads application appsettings.

### Target verification and cleanup

The fixture inspects the cached image, then runs `docker run --pull=never` with a
unique name/ownership label, generated strong credentials, no mounted application
volumes and `127.0.0.1::1433`. Before opening even a master connection it verifies the
returned container ID, owned label, exactly one loopback binding, port bounds and the
exact generated `LMS_Integration_<GUID>` database name. Safety checks reject localhost
aliases, foreign names/labels and public bindings; an offline safety test exercises
these denials. There is no LocalDB/external SQL fallback.

The generated password is passed through a private temporary environment file, not
command arguments, logs or checked-in configuration. The test directory is owner-only
on Windows/Unix and the environment file is deleted after launch. Docker administrators
can inspect container environment credentials; use only synthetic data and trusted
test runners. No secrets/SQL exception text or connection strings are printed by the
fixture. The fixture waits for SQL readiness with bounded timeouts, then calls real
`MigrateAsync` only against that verified new database. It seeds synthetic users and
memberships directly in test setup; it never calls the production role seeder.

Every competing operation uses a separate DbContext/service provider. Test collection
parallelism is disabled while explicit contenders run concurrently inside race tests.
Barriers/start gates have bounded waits; no automatic mutation retry is used. Synthetic
assignment storage lives under the owned private temporary directory; Identity uses
test-only ephemeral keys and in-memory fake emails. No real provider is contacted.

Cleanup verifies the original container's label again before removing exactly that
container and verifies the generated temporary path remains under the expected temp
root before recursive deletion. It does not delete other databases/containers/volumes.
If the process/daemon dies or a create acknowledgement is lost, cleanup can be unknown
and leave an owned test container. An operator must inspect names/labels and confirm
the specific run's ownership before removing it. Do not prune Docker globally or
clean up an external SQL instance. Do not dump container environment/log secrets.

## Implemented SQL cases — all unexecuted here

There are 19 real-SQL test cases in the partial `CrossFeatureSqlTests` class. They
compile without EF InMemory, mocked repositories or offline ADO stubs. Fake email
transport and injected audit failure are deliberate boundary controls; the database,
Identity EF store, repositories and transaction operations are real.

| Area | Assertions implemented |
| --- | --- |
| Two organizations | Distinct stored Admin/Instructor/Student memberships, learners/courses/lessons; scoped lists/counts/detail/nested parent IDs; foreign attached mutation rejected and stored state unchanged |
| Audit reads/authority | Scoped total and pagination; action/outcome/actor/resource filters; foreign event IDs; Admin selection mismatch; Instructor/Student and forged platform privilege denial |
| Stored SuperAdmin | Stored global role lookup; platform excludes organization events; explicit organization selection required |
| Duplicate enrollment | Both contenders observe no prior row before release; one stored row and one exact UnitOfWork duplicate conflict |
| Complete versus cancel | Repository's real update/hold lock; one terminal transition, stable EnrolledAt, CompletedAt only for completion and one audit |
| Membership races | Role change versus instructor assignment and role change versus Active enrollment; valid serialized stored membership/assignment/enrollment outcome |
| Assignment | Concurrent final submission has one row; handler freezes instructions/deadline/max marks; one grade; correct separate submission/grading audits without submitted text/feedback |
| Assignment boundaries | Due-at equality rejects submission; unassigned Instructor rejected; no false success event |
| Audit atomicity | Injected audit command failure after an earlier uncommitted SQL business write; fresh context verifies rollback; outer transaction rollback removes business and event; repeated saves emit once |
| Invitation lifecycle | Creation/reissue invalidate old token; revocation denies reuse; token hashing persisted; delivery audit uses separate operation ID |
| Invitation acceptance | Concurrent acceptance creates one account, confirmed email, membership and acceptance audit; replay fails |
| Existing invitation identity | Wrong existing account denied; conflicting role preserves membership and pending invite; matching stored membership consumes without duplication |
| Invitation authority/expiry | Preview does not consume/create; one-tick-before 48-hour expiry succeeds, exact boundary fails; revoked inviter authority prevents account/acceptance success |
| Invitation creation race | One pending row, one success and one conflict for concurrent create |
| Delivery failure | Explicit unconfigured-sender refusal leaves pending, undelivered row; creation succeeded audit and separate failed delivery audit |
| Reset | Real Identity validation, weak password denial, expired/reused token rejection, password hash/stored stamp rotation and account-only audit |
| Reset and Web | Signed pre-reset JWT rejected by shared production stamp validator after SQL reset; actual MVC cookie validation rejects/removes server session on next use via a fake API transport backed by the real SQL store |

Completion/cancellation tests focus on transaction serialization at the repository
boundary, not the full completion-prerequisite HTTP workflow. Failure injection splits
EF command batches and deliberately performs an earlier synthetic SQL write inside
the same outer transaction to avoid relying on EF insert/update ordering. No observed
commit acknowledgement failure or true SQL Server deadlock has been simulated here.

## Offline host cases and remaining verification

Four independently runnable MVC host cases cover actual rendered GET forms, no GET
mutation, bound antiforgery POST, secure cookies with server-only JWTs, revoked-session
cookie replay denial, anonymous audit/media/attachment routes, authorized private PDF
download with fake API bytes, tenant/nested-ID denial and learner-role rejection.
Media returns 401 for anonymous requests; page/attachment login challenges redirect.
Existing unit/service tests cover additional safe rendering and authorization cases.

Still **unverified**, and some supplemental automation still unimplemented:

- All 19 implemented SQL tests, including migration execution and lock/rollback/index
  behavior. Docker/image availability is the precise execution blocker here.
- A full production-equivalent API HTTP host connected to disposable SQL, numeric
  HTTP race responses and tenant checks across every reporting/quiz/gradebook endpoint.
  The new suite calls actual services/repositories and the shared JWT stamp validator;
  it does not start API Program. The existing API startup registrations remain inline
  and include role seeding; future API host composition must isolate registrations from
  seeding rather than invoke that entry point. No unsupported package was downloaded.
- Controlled completion-prerequisite changes, quiz-start/scoring races, adversarial
  exact-email assignment across two organization transactions, and observed SQL deadlock
  handling. The additional design in `LMS.Tests/README.md` records these cases.
- Actual authorized assignment attachment transfer end-to-end through API storage/SQL,
  private media range/HEAD behavior through real HTTP and SQL, deployed browser cookies,
  TLS/proxy behavior, private directory ACL behavior on deployment hosts, production
  email acknowledgement/failure and multi-instance session/key/storage failover.
- Unknown database commit/email outcomes under real network failures and recovery;
  passing unit tests cannot establish those outcomes.

No full SQL/HTTP integration coverage or production readiness is claimed. Run the
implemented opt-in suite and the one consolidated acceptance checklist in
[the deployment guide](release-deployment-guide.md) before approving an environment.

## Batch changes and verification report

Changed production files: `.gitignore`, `LMS.Api/LMS.Api.csproj` (private assignment
publish exclusion), `LMS.Api/Program.cs` and new
`LMS.Infrastructure/Identity/JwtSecurityStampValidation.cs` (same stamp-validation
behavior extracted for isolated reuse). No product feature, migration or package was added.

New test files: `LMS.Tests/Integration/DisposableSqlServer.cs`,
`SqlTargetSafetyTests.cs`, `CrossFeatureSqlTests.cs`, `CrossFeatureAccountSqlTests.cs`,
`CrossFeatureLearningSqlTests.cs`, `InProcessMvcTests.cs`, `ReleasePackagingTests.cs`.
`JwtStoredStampTests.cs` adds offline stored-subject/legacy/stale-stamp regression cases.
The existing project and test files remain; `LMS.Tests/README.md` now links this guide.

New scripts: `scripts/run-sql-integration.ps1`, `scripts/generate-release-sql.ps1`.
New documentation: this file, `docs/release-deployment-guide.md`, and four
`docs/sql/release-*` SQL artifacts. Existing uncommitted feature changes are preserved.
No account seed/global role assignments, business rules or migration sources changed.

Release routes remain the existing implemented routes; no test endpoint is added to
either product host. Account routes include API `GET /api/account/antiforgery`,
`GET /api/account/session`, `POST /api/account/forgot-password`,
`POST /api/account/reset-password`, `POST /api/account/invitations/preview`,
`POST /api/account/invitations/accept`; MVC GET/POST `/account/forgot-password`,
`/account/reset-password`, `/account/invitation`. Administrative invitation routes are
`GET/POST /api/account/organizations/{organizationId}/invitations` and
`POST /api/account/organizations/{organizationId}/invitations/{id}/revoke`, with MVC
`GET /organizations/{organizationId}/invitations`, GET/POST
`/organizations/{organizationId}/invitations/create` and GET confirmation/POST
`/organizations/{organizationId}/invitations/{id}/revoke`.
Audit API GET-only lists/details are `/api/audit/organizations/{organizationId}`,
`/api/audit/organizations/{organizationId}/{eventId}`, `/api/audit/platform`,
`/api/audit/platform/{eventId}`; MVC GET-only `/admin/{organizationId}/audit[/{eventId}]`
and `/platform/audit[/{eventId}]`. Refer to linked feature documentation for complete
assignment/learning/publication route inventories.

PowerShell script execution must be permitted by the operator's local execution
policy; no persistent execution-policy change is made by this batch. Both scripts
pass parser checks. Direct dotnet commands remain available when local script execution
is disabled. The generation wrapper was blocked by this machine's script execution
policy; its same cached EF commands were run directly and succeeded without changing
that policy. The SQL runner was not invoked or opted in. A successful offline build
or SQL artifact review is not deployment approval. Production blockers remain explicit
email provider configuration, shared multi-instance sessions, durable protected keys
and retained private storage, authorized pending migrations/backfill and real
SQL/browser/provider/concurrency verification.

Final verification on 2026-10-06:

| Check | Result |
| --- | --- |
| Entire existing/expanded .NET suite, offline default, separate output | **919 passed, 0 failed**, including 4 HostIntegration cases |
| SQL Server integration | **19 skipped; execution blocked** by missing Docker/executable/service and absent explicit opt-in; 0 SQL passes |
| Full solution build `--no-restore`, separate output | Passed; 3 pre-existing NU1510 redundant-package warnings, 0 errors |
| JavaScript | All 5 Web JS files passed `node --check`; both existing Node behavior tests passed |
| Offline EF model/snapshot | No pending model changes; no database connection |
| Offline migration SQL | Pending Up, full-chain Up and guarded Down review artifacts generated/reviewed; never executed |
| PowerShell | Both scripts pass parser checks; wrapper runtime limits stated above |
| Working-tree review | Tracked diff plus all untracked paths inventoried; 307 text files checked for conflict/NUL markers; untracked whitespace and `git diff --check` passed |

No restore, install, download, staging, commit, push, branch switch, existing-database
access, normal application startup or manual browser/Swagger test was performed. The existing
uncommitted changes and `LMS.Tests` project remain in the current working tree.
