# Learning-flow tests

The release batch adds fail-closed, opt-in real SQL Server tests and independently
runnable in-process MVC tests to this existing project. See
[release integration instructions and the coverage/remaining-case matrix](../docs/release-integration-testing.md)
and [the consolidated deployment guide/checklist](../docs/release-deployment-guide.md).
No new test package was added. Default discovery skips SQL tests without
`LMS_SQL_INTEGRATION=1`; use `--filter 'Category!=SqlServerIntegration'` to force an
offline run even when the opt-in variable is present.

Run after restoring this project:

```powershell
dotnet test .\LMS.Tests\LMS.Tests.csproj --no-restore
dotnet build .\EnterpriseLMS.slnx --no-restore
```

These unit tests use real domain entities and application handlers with strict repository mocks.
They do not read appsettings, connect to SQL Server, or apply migrations. The completion
transaction mock invokes the callback; it does not emulate SQL transactions or concurrency.

Organization tests cover stored membership, per-organization roles, request selection,
and platform access. Tenant tests cover all 50 controller authorization policies, actual
EF filter expressions over two organization graphs plus legacy rows, handler boundaries,
and SQL Server provider command generation. The offline ADO stub cannot open a connection;
commands are captured and aborted before execution. No EF InMemory package is used.
The Infrastructure reference supplies the real EF model/repositories for these checks.
See [the endpoint evidence matrix and consolidated checklist](../docs/tenant-isolation-audit.md).

Cancellation and completion use the same `ExecuteEnrollmentTransactionAsync` callback.
Focused unit tests cover Active-only cancellation, ownership, missing enrollment,
both serial transition orders, preserved completion timestamps, and reads/saves
occurring inside the callback. These are not concurrent SQL Server tests.

Docker is unavailable on this machine (`docker` is not recognized and no Docker
service or standard executable was found). SQL Server integration tests have not run.
For the disposable fixture below, additionally race cancellation against completion
in both lock-acquisition orders. Require one successful transition and one conflict;
query with a fresh context to verify the winning status and exact CompletedAt.
Force a save failure inside the transaction, then verify rollback with another context.
Do not use an existing EnterpriseLMS database for these checks.

## Supplemental SQL/HTTP concurrency verification design

The new release suite implements selected service/repository SQL cases tagged
`Category=SqlServerIntegration` using `Integration/DisposableSqlServer.cs`. None has
run here because Docker is unavailable. The design below also describes additional
full HTTP/quiz race cases that remain unimplemented; it is not a passing-test report:

1. Start a fresh SQL Server container owned by the test fixture on a randomly assigned
   localhost port, with a generated password. Create `LMS_Test_<random GUID>` inside it.
   Construct the connection string solely from the container fixture. Never load application
   configuration, accept the development connection string, or reuse an existing database.
2. Apply the actual EF migrations to that empty database. Seed a published course, students,
   lessons, quiz questions/options, and progress as needed. Use separate DbContexts and
   request scopes for every contender; never share a DbContext between tasks.
3. Host the real API middleware/controllers in a test host with fixture-only database
   configuration and test authentication. Use a barrier at the enrollment precheck so both
   enrollment requests observe no existing enrollment before either saves. Assert response
   statuses are exactly 201 and 409, the latter uses the existing conflict body/message,
   and a fresh context observes exactly one `(CourseId, UserId)` row.
4. Use a direct duplicate insert to verify SQL error 2601/2627 for the enrollment index is
   translated through UnitOfWork. Cause a different unique-index violation and a foreign-key
   violation separately and assert they remain DbUpdateException (and retain existing HTTP
   handling). Verify Up creates only the intended index; Down drops it without deleting rows.
5. Send two completion requests for one eligible Active enrollment using independent contexts.
   Coordinate request starts before acquiring the enrollment lock (not inside the locked
   callback, which would deadlock the test). Expect one 200 and one 409; verify Completed,
   100 percent, and one stable CompletedAt. A later repeat must return 409 without changing it.
6. Race completion against prerequisite changes (lesson publication/progress). Control lock
   acquisition with explicit synchronization and bounded timeouts; assert only a valid
   serialized outcome or the existing deadlock conflict response, never partial completion.
7. Race quiz starts and submissions in separate scopes. Assert at most one Started attempt,
   no duplicate attempt numbers, one scoring/answer write per submission, and conflict for
   the loser. Recheck publication and Active enrollment at the mutation boundary.
8. Dispose only the container created by this fixture in finally/fixture cleanup. On failure,
   log test IDs and SQL error numbers without credentials or connection strings. Do not issue
   cleanup commands against any external server. Use bounded waits, never timing-only sleeps.

Until the implemented suite and supplemental cases run, real SQL unique-index enforcement, exception
translation, HTTP race responses, lock serialization, deadlock handling, transaction rollback,
and migration application/rollback remain unverified by these unit tests.
