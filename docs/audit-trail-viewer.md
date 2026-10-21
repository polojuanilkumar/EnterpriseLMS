EnterpriseLMS durable audit trail and viewer

The Angular organization administration, reports and audit extension is documented in [organization-administration.md](organization-administration.md). This earlier record remains as policy and implementation history.
===========================================

This change adds transactional success auditing and authorized read-only API/MVC history. It preserves the current branch, existing working-tree changes, LMS.Tests, authentication, membership locks, and learning rules. No database was accessed, migration applied, application started, manual browser/Swagger test run, or Git staging/commit/push performed. Administrative denial/conflict auditing is deferred, so this is not full failure-event coverage.

Foundation and provenance
-------------------------

`AuditEvent` stores a generated event ID, generated operation ID, UTC timestamp, optional verified actor ID, optional verified organization ID, scope, stable action/outcome codes, resource type/ID, generated correlation ID, and bounded JSON metadata. IDs are internal GUIDs. The timestamp is captured at staging/save/observation time, not claimed to be the exact SQL commit time.

`AuditRequestContext` is scoped to the API request. Organization middleware sets its actor only after JWT validation and stored organization/platform checks. Organization-scoped operations also have the existing verified `OrganizationContext`. Middleware returns a server-generated `X-Correlation-Id`; incoming correlation/actor/outcome headers or body properties are never copied into events. A valid anonymous invitation acceptance or password reset has no authenticated actor ID. Its stored target/resource IDs identify the verified operation; the code does not invent an authenticated actor.

`IAuditWriter.Stage` queues a typed success event in the business DbContext without saving. `IAuditQueryRepository` provides scoped read-only projections. `IAuditObservationWriter` accepts only invitation delivery observations and persists them in a fresh scope/context. These are server-side abstractions, not exposed mutation APIs. New domain metadata factories accept roles, booleans and internal IDs; no arbitrary request dictionary, exception string or serialized entity is accepted.

Organization IDs come from newly created organizations, locked stored memberships/invitations, or the stored course ancestry of learning resources. Reset is an account event with no organization, even if the account belongs to organizations. Organization creation and initial membership are organization events for the newly created target; SuperAdmin must explicitly select that organization to view them. Platform history currently contains account password-reset events; there is no synthetic platform activity or historical backfill. Platform/account lists exclude organization histories.

Append-only behavior uses private entity setters plus synchronous/asynchronous SaveChanges guards rejecting modified/deleted audit entities. API DbContext configuration also installs `AuditCommandGuard` to reject generated bulk/raw UPDATE/DELETE/MERGE/TRUNCATE/DROP/ALTER statements against AuditEvents. INSERT/SELECT and normal mixed business-update/audit-insert batches are allowed. The SQL guard is defense in depth for application-generated statement shapes, not a general SQL parser. Out-of-band connections and database administrators can still alter the table. Application append-only behavior is **not tamper-proof**, and there is no cryptographic chaining, export, deletion endpoint, retention policy/job or retroactive fabrication in this batch.

Instrumented outcomes
---------------------

All of these database success codes are persisted with their business changes, not inferred from HTTP status:

| Action code | Resource type / ID | Scope and approved metadata |
|---|---|---|
| `organization.created` | organization / new organization ID | Organization; empty metadata. |
| `membership.created` | membership / membership ID | Organization; newRole and targetUserId. Covers initial Admin, administrative creation and invitation-created membership. |
| `membership.role-changed` | membership / existing membership ID | Organization; oldRole, newRole, targetUserId. Identical roles emit no change event. |
| `course.instructor-changed` | course / course ID | Organization; oldInstructorId, newInstructorId from stored/current verified assignment. |
| `invitation.created` | invitation / new invitation ID | Organization; newRole. |
| `invitation.reissued` | invitation / replacement ID | Organization; newRole and relatedResourceId referencing the retired invitation ID. One reissue event describes replacement; it does not duplicate a separate revoke event for the retired row. |
| `invitation.revoked` | invitation / stored invitation ID | Organization; empty metadata. Already terminal invitations emit no new revoke event. |
| `invitation.accepted` | invitation / consumed invitation ID | Stored invitation organization; relatedResourceId is the verified target account ID. Matching existing membership emits acceptance only; new membership also emits membership.created. |
| `invitation.delivery` | invitation / committed invitation ID | Organization; empty metadata. Separate external observation, described below. |
| `account.password-reset` | account / verified reset account ID | Account, no organization; empty metadata. No forgot-password event. |
| `course.publication-changed` | course / course ID | Organization; oldPublished/newPublished. Archive/unpublish changes from published to unpublished are included. |
| `lesson.publication-changed` | lesson / lesson ID | Organization; oldPublished/newPublished. |
| `quiz.publication-changed` | quiz / quiz ID | Organization; oldPublished/newPublished. |
| `assignment.publication-changed` | assignment / assignment ID | Organization; oldPublished/newPublished. |
| `assignment.submitted` | assignment-submission / new submission ID | Organization; relatedResourceId is assignment ID; actor checked against submission owner. |
| `assignment.graded` | assignment-grade / new grade ID | Organization; relatedResourceId is submission ID; actor checked against grader. No marks or feedback. |
| `enrollment.cancelled` | enrollment / stored enrollment ID | Organization; relatedResourceId is stored course ID. |
| `enrollment.completed` | enrollment / stored enrollment ID | Organization; relatedResourceId is stored course ID. Includes existing automatic progress completion when it persists a state transition. |

Publication no-ops emit no event. Initial unpublished resource creation, content edits, enrollment creation/reactivation, quiz attempts/answers, profile changes, GETs and login/logout are not audited by this batch. Existing staff, learner and platform behavior remains unchanged except that critical audited mutations now fail if their audit insert fails.

Stable outcomes are `succeeded`, `failed`, and `unknown`. Only external invitation delivery accepts failed/unknown outcomes here; database mutation events are succeeded and become durably visible only with the business commit. No `denied` or `conflict` event is fabricated from middleware exceptions.

Metadata allowlist
------------------

Metadata is a JSON object of string values, maximum 512 characters, with no duplicate keys. Allowed keys and values:

- `oldRole`, `newRole`: exactly Student, Instructor, or Admin; never SuperAdmin.
- `oldPublished`, `newPublished`: exactly `true` or `false`.
- `targetUserId`, `oldInstructorId`, `newInstructorId`, `relatedResourceId`: nonempty GUIDs in D format.

An additional per-action allowlist restricts which of those keys each action may contain, as listed above. Password-reset, delivery, revocation and organization-creation metadata are empty. No passwords/password hashes, JWTs, invitation/reset tokens or hashes, security stamps, emails/email bodies, submission text, instructions, feedback, marks, quiz answers/answer keys, filenames/physical paths, request bodies or raw exception messages are stored. Resource names and account display names are not copied into history. Metadata is Razor encoded, as are other displayed strings.

Transaction guarantees and limits
---------------------------------

Organization administration explicitly stages events after its verified resource/role checks and before its business save. Its existing serializable transactions and organization locks remain. Invitation creation/reissue/revocation/acceptance explicitly stage events; reissue's earlier save retires the pending slot inside the same outer transaction and the subsequent replacement save includes its audit event. New-account acceptance may save the Identity account first, but membership, confirmation, consumption and all events remain in the same enclosing transaction. Password reset stages its event only after Identity succeeds, saves that event, then commits the same transaction which owns Identity's earlier password/stamp save.

Learning auditing runs inside the existing guarded SaveChanges pipeline. It reads stored state without tracking before comparing publication, instructor or enrollment changes, including repositories that attach previously untracked entities. Parent joins establish stored organization ownership. Submission/grade metadata uses only safe identifiers. These events are added to the same EF save as business changes. The pipeline uses the existing outer transaction when present; otherwise it owns a serializable transaction for the audited save. Existing membership write locks and assignment/enrollment transactions remain authoritative and are not bypassed.

The hook does not instrument memberships/invitations/reset a second time. After normal successful saves EF accepts tracked changes; repeated identical staging in the same operation is deduplicated. A unique `(OperationId, Action, ResourceType, ResourceId)` index provides another duplicate guard. Each current API request represents one business operation; separate requests receive separate operation IDs. Delivery observations receive distinct operation IDs and share the originating correlation ID. Audited `SaveChanges(false)` is rejected so unaccepted entries cannot accidentally replay an audit insert. This is not an HTTP idempotency-key implementation or an automatic retry mechanism.

An audit construction/insert error propagates and prevents the owning business transaction from committing. Save or owned-commit failures mark the context unusable for another save/stage. Existing explicit transaction owners propagate commit exceptions and dispose the request scope; do not reuse a context after any failed/rolled-back/ambiguous outer transaction. Neither the success-event writer nor general exception middleware independently saves success before commit. Rollback must remove both business writes and their events; this structural guarantee still needs real SQL Server integration verification.

A lost commit acknowledgement can mean the complete transaction committed or did not commit. No automatic replay or compensating fabricated failure record occurs. Recover by reading the stored resource and scoped audit history in a new request. A persisted success event is part of that stored transaction; a client error alone is not evidence of rollback. UTC ordering plus event ID is deterministic but is not a globally serialized chronology of concurrent commit times.

Invitation delivery is external
--------------------------------

The workflow is: commit invitation plus creation/reissue event; call the email sender; append one delivery observation using a fresh context and a five-second persistence deadline; then acknowledge DeliveredAt on the invitation when the sender returned successfully. The observation cannot commit pending changes from the business context and is not atomic with sending or with DeliveredAt acknowledgement.

- `succeeded`: the sender returned successfully. In Development this means its private capture write completed; with a future provider it means that provider boundary accepted the send, not confirmed inbox receipt.
- `failed`: the known UnconfiguredAccountEmailSender refused without attempting delivery.
- `unknown`: another sender exception or cancellation leaves actual delivery unconfirmed. An exception is not proof that an external provider did not accept the email.

Delivery observation persistence failures are not swallowed or converted into business success. An email may already have been sent even if its audit commit/DeliveredAt acknowledgement fails. Process termination between sending and recording can leave no delivery observation. Inspect the invitation list and audit history without replay; any reissue requires explicit confirmation and rotates the token as before. Production email remains blocked until an explicit provider is supplied; no paid service/package/provider was added here.

Deferred failure auditing
------------------------

Authenticated administrative denials/conflicts are **unaudited in this batch**. Existing handlers expose general exceptions without a verified resource/failure descriptor; failures may happen before tenant verification or after a transaction leaves tracked changes unusable. A supplied foreign organization/resource ID or HTTP 403/409 cannot safely supply audit provenance or outcome. The fresh observation writer is deliberately restricted to delivery observations; it is not a generic exception sink.

Future durable failure auditing needs explicit capture at verified policy boundaries, fresh stored authorization/resource validation in an independent clean context, safe stable denial/conflict codes, and its own bounded write budget/rate limiting. General exception middleware remains diagnostic logging only. This batch does not write every GET, invalid anonymous request, token-validation failure or forgot-password request. No failure events are falsely attributed to an attacker's requested tenant.

Routes and access matrix
-------------------------

| Method | Exact route | Purpose |
|---|---|---|
| GET | `/api/audit/organizations/{organizationId}` | Scoped list/count; requires matching selected X-Organization-Id. |
| GET | `/api/audit/organizations/{organizationId}/{eventId}` | Detail within that same verified scope; foreign/nonexistent ID is 404. |
| GET | `/api/audit/platform` | Platform/account events only; no organization header/combined history. |
| GET | `/api/audit/platform/{eventId}` | Platform/account detail only; organization IDs are not reachable here. |
| GET | `/admin/{organizationId}/audit` | Organization MVC viewer, linked from organization Admin navigation. |
| GET | `/admin/{organizationId}/audit/{eventId}` | Organization MVC detail. |
| GET | `/platform/audit` | Platform/account MVC viewer, linked from platform navigation. |
| GET | `/platform/audit/{eventId}` | Platform/account MVC detail. |

No audit POST/PUT/PATCH/DELETE/export endpoints exist. MVC keeps JWTs in existing server sessions and reuses their API validation and expired-session/access-denial behavior. Empty results have an explicit message. Filters are GET forms; there are no audit mutation controls. Existing global antiforgery/logout/navigation behavior is retained.

| Verified role | Selected organization history | Platform/account history |
|---|---|---|
| Organization Admin | Only the selected active organization where stored membership remains Admin | Denied |
| Instructor / Student | Denied | Denied |
| Stored SuperAdmin | Allowed after explicit target selection and matching route/header/session | Allowed; organization histories excluded |
| Anonymous / expired session | Denied | Denied |

Repository methods recheck stored membership and SuperAdmin role joins; JWT/cookie organization role labels alone do not authorize a read. Selection mismatch is rejected before querying the supplied foreign organization. Existing selection requires active organizations; inactive/deleted organization history is retained but cannot be viewed through this active-organization selection workflow in this batch. This limitation does not cause history deletion.

Query parameters are `page` (1..10000), `pageSize` (1..50, default 20), `fromUtc`, `toUtc`, `action`, `outcome`, `actorId`, and `resourceId`. Dates must have UTC offset zero; the default range is the last seven days ending now, maximum 31 days. The start is inclusive and end exclusive. Actions/outcomes use the exact allowlists above. Actor/resource filters accept GUIDs, not emails. Lists, totals and detail predicates are scoped in the repository and use no-tracking projections. Results order by TimestampUtc descending, then event ID descending. Pagination links preserve the normalized date range and filters. Known details may be read beyond a current list's date window if their scope is authorized.

Migration and deployment
-------------------------

The additive offline migration is `20261006135202_AddAuditEvents`, with review SQL in `docs/sql/audit-events-up.sql`, generated only from `20261006122454_AddOrganizationInvitations` to this migration. Up creates AuditEvents, bounded columns, an organization/platform-account scope check, and four indexes: scoped timestamp/event ID, actor timestamp/event ID, resource timestamp/event ID, and unique operation/action/resource identity. Historical actor/organization/resource IDs intentionally have no foreign keys or cascading dependencies so deletion of business rows cannot erase audit history. Down blocks if audit history exists, before dropping an empty new table. No learning table alterations, fabricated audit backfill, seed changes or existing history updates occur.

Assignment `20261004061629_AddLessonAssignments`, invitation `20261006122454_AddOrganizationInvitations`, and audit `20261006135202_AddAuditEvents` remain pending/unapplied. Review the entire schema chain, backups, permissions and deployment sequencing under separate authorization before applying anything. Audited mutations require the AuditEvents table and INSERT permission; missing schema/permission is a blocking failure, not a reason to silently omit auditing. History viewing needs SELECT permission. Review least-privilege database access and monitoring independently; app guards do not remove DBA privileges.

No new package or configuration secret/key is required. Existing SQL Server, JWT, HTTPS API origin, TimeProvider, private Development email capture and production email-provider prerequisites remain. Persist existing Data Protection/session settings as documented for onboarding. Configure upstream logging/tracing so secrets and request bodies stay excluded. New correlation IDs contain no secrets. The current per-request administrative rate limits bound invitation delivery observations; the independent observation save has a five-second deadline. Viewer pagination/date bounds limit work, but production query/index plans, concurrent count/page consistency and traffic limits need real load verification. Additional serializable audit reads may affect blocking/deadlocks; no automatic retry is introduced.

Files added or changed for this batch
-------------------------------------

- Domain: `LMS.Domain/Entities/AuditEvent.cs` (entity, codes and typed metadata).
- Application: `LMS.Application/Features/Auditing/AuditContracts.cs`, `AuditAccess.cs`, `AuditTransitions.cs`.
- Infrastructure: `LMS.Infrastructure/Persistence/LMSDbContext.cs`, `LMSDbContext.Auditing.cs`, `LMSDbContext.Organizations.cs`, `LMSDbContext.MembershipWrites.cs`, `AuditCommandGuard.cs`, `Configurations/AuditEventConfiguration.cs`; `Repositories/OrganizationRepository.cs`, `OrganizationAdministrationRepository.cs`, `AuditQueries.cs`, `AuditQueryRepository.cs`, `AuditObservationWriter.cs`; `Identity/AccountOnboarding.cs`; `Persistence/Migrations/20261006135202_AddAuditEvents.cs`, its `.Designer.cs`, and `LMSDbContextModelSnapshot.cs`.
- API: `LMS.Api/Controllers/AuditController.cs`, `Middleware/OrganizationMiddleware.cs`, `Program.cs`.
- Web: `LMS.Web/Controllers/AuditController.cs`, `Models/AuditModels.cs`, `Services/AuditApi.cs`, `Views/Audit/Index.cshtml`, `Views/Audit/Detail.cshtml`, `Views/Admin/Index.cshtml`, `Views/Platform/Index.cshtml`.
- Tests: `LMS.Tests/AuditTests.cs`, `AuditTransactionTests.cs`, `AuditWebTests.cs`, and `LMS.Tests.csproj` (links the API controller for isolated tests).
- Documentation: this file, the audit SQL review artifact, and an audit extension note in the onboarding documentation.

Earlier working-tree changes remain intact and are not listed as new audit work.

Verification and remaining integration work
--------------------------------------------

The full existing LMS.Tests suite passes 906 tests. Audit coverage includes in-memory two-organization totals/filter/pagination/detail isolation, explicit selected scope, stored-role access policy and SQL joins/predicates, bounded/allowlisted metadata and sensitive-key rejection, append-only entity/SQL guards, stable action/resource transitions, no-op/duplicate suppression, staged business+audit entries sharing an EF save/transaction, injected audit/save/commit failures propagating, separate delivery contexts, read-only API/MVC routes, safe actual Razor rendering, empty states and session/access denial. All HTTP/email behavior is fake/in-memory; no provider or application host runs.

Transaction tests use inert ADO connection/transaction objects and a suppressed EF SaveChanges interceptor. They verify code enlistment/error propagation, **not actual persistence, SQL Server rollback, locking or commit ambiguity**. SQL-generation tests and offline migration/model checks open no connection. No real SQL Server, email provider, capture ACL behavior, deployed logging, browser behavior or browser/Swagger test was verified. These remain deployment/integration prerequisites, not claimed completed coverage.

Solution build passes with `--no-restore` and separate `artifacts/audit/bin/` output. Existing JavaScript syntax checks and both Node behavior tests pass. Offline EF pending-model checking reports no model changes. Generated Up SQL was reviewed for additive table/index creation and no history mutation. `git diff --check` passes. The three existing NU1510 package-pruning warnings remain; cached dependencies were reused without adding packages.

One consolidated manual checklist (not executed)
------------------------------------------------

1. In a separately authorized disposable SQL Server deployment, review/apply the full pending assignment/invitation/audit chain only with permission. Confirm audit Up is additive, indexes and no-cascade choices match the design, and normal Down refuses retained history. Do not fabricate/backfill events.
2. Test Admin A versus Admin B, Student, Instructor, revoked roles, anonymous/expired sessions and forged JWT/cookie role labels. Verify lists, totals and details cannot leak another organization; foreign event IDs return 404 within an authorized scope. Require explicit SuperAdmin target selection. Verify platform/account list/detail never includes organization events.
3. Exercise organization creation and initial membership, administrative membership creation/role changes, course Instructor change, each invitation lifecycle operation, successful reset, all four publication kinds, final submission/grade, cancellation and both explicit/automatic completion. Inspect stable action, actor, stored organization, resource IDs and minimal metadata. Verify no-ops and matching invitation membership acceptance do not duplicate unrelated events.
4. Force AuditEvents INSERT failure/permission loss for each critical database operation. Verify both mutation and audit roll back, including multi-save invite reissue/acceptance and Identity reset. Verify no success response and no automatic retry. Check real SQL Server enlistment, locking, deadlocks, unique operation indexes and concurrent same-resource writes separately from unit results.
5. Simulate lost save/commit responses before/after SQL commit. Re-read resource and history in a new request; do not replay automatically. Confirm persisted success records reflect actual commits, and request error/HTTP status alone does not generate a success/failure event. Discard failed contexts.
6. Test sender success, known no-send refusal, exceptions/cancellation and crash windows before/after sender observation, audit commit and DeliveredAt acknowledgement. Confirm delivery has separate outcome/operation ID, no tokens/hash/email metadata, and no claim of email/database atomicity. Review list and history before any explicit reissue.
7. Attempt tracked audit updates/deletes and generated bulk/raw mutations through authorized test code. Verify guards reject them while normal mixed business-update/audit-insert batches still succeed. Review SQL parser limits and DBA/out-of-band privileges; do not describe the table as tamper-proof.
8. Test default and 31-day UTC boundaries, invalid offsets/actions/outcomes/GUIDs, scoped totals, equal-timestamp ordering, pagination filter preservation, empty results and archived/inactive organization limitations. Inspect production execution plans and throughput; add later tuning only from evidence.
9. Inject disposable malicious display/metadata fixtures and verify Razor encoding, no audit write/export controls, clear session/access errors and no leaked sensitive fields in API, viewer, app/proxy/APM logs or captures. Verify no GET/forgot-password/token-validation/foreign-denial audit flooding. Administrative denial/conflict auditing remains deferred.
10. Recheck existing authentication/reset invalidation, organization/platform navigation, role-change/enrollment/instructor safeguards, assignments, quizzes and completion. Record actual SQL Server, delivery and browser results separately from this offline validation.
