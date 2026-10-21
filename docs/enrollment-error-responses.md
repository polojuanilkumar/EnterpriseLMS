# Enrollment error response review

The exception middleware maps UnauthorizedAccessException to 403, ArgumentException
to 400, KeyNotFoundException to 404, and InvalidOperationException to 409. These use
the existing `success`, `message`, `data`, `errors` envelope. Other exceptions return
500 with `An unexpected error occurred.`; the actual exception is logged server-side.

| Action | Success | 401 | 403 | 404 | 409 |
| --- | --- | --- | --- | --- | --- |
| Enroll | 201 | Authentication or invalid/missing user claim | Not a Student | Missing course | Unpublished course, existing enrollment, targeted enrollment uniqueness violation |
| Cancel | 200 | Authentication or invalid/missing user claim | Not enrollment owner | Missing enrollment | Non-Active enrollment or translated transaction deadlock |
| Complete | 200 | Authentication or invalid/missing/empty user claim | Student role gate or not enrollment owner | Missing enrollment or missing/unpublished course | Non-Active enrollment, unmet lesson prerequisites, translated transaction deadlock |
| GetById | 200 | Authentication or invalid/missing user claim | Not enrollment owner | Missing enrollment | No business conflict path |
| GetMyEnrollments | 200 | Authentication or invalid/missing user claim | No forbidden business path | Empty result is 200 | No business conflict path |
| GetMyCoursesProgress | 200 | Authentication or invalid/missing user ID claim | Student role gate | Missing courses are skipped | No business conflict path |

Organization selection now precedes every enrollment action: malformed/missing selection
can return 400 and absent membership/inactive organization can return 403, including
GetMyEnrollments. The table above describes controller/handler business paths; existing
action metadata does not enumerate all organization middleware responses. See the
[tenant audit and consolidated verification checklist](tenant-isolation-audit.md).

ProducesResponseType attributes describe these paths without adding blanket 403,
404, or 409 responses to list actions. Complete already had correct metadata.
Automatic model-binding 400s and unexpected 500s remain framework/global concerns;
their behavior was not changed by these action metadata additions.

The envelope applies to exception-generated failures and Complete's existing local
ownership catch. Authentication challenges and authorization role denials can have
empty bodies; MVC may also produce ProblemDetails for its own client errors. The
403 schema describes the handler-error body, not a guarantee that every role-gate
denial has that body. No authentication/authorization response rewriting was added.

Unit tests compile the production middleware and enrollment controller source files
directly into LMS.Tests using the installed ASP.NET Core framework reference. This
avoids starting the API host or its database seeder and requires no new test packages.
Middleware tests exercise status, envelope, serialization options, generic 500
messages, original-exception logging, and successful-response passthrough. Metadata
tests inspect the actual controller attributes; they do not run a hosted Swagger
document or authenticate HTTP requests. Hosted authentication, Swagger generation,
and database error paths remain unverified by these tests. SQL Server concurrency
integration tests remain unrun because Docker is unavailable.
