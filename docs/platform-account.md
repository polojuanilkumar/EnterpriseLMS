# Angular platform administration and private account links

Implemented 2026-10-15. This batch extends the existing unstaged work; both branches and prior changes are preserved. No packages, schema changes, migrations, database connections, real email, normal hosts, staging, commits or pushes were used.

## Platform and Angular routes

Production Angular is explicitly mounted at `/app/`; development remains `/`. Route paths below are relative to the Angular base. BFF paths remain root-relative `/bff/*` on the same HTTPS origin.

| Angular route | Behavior |
| --- | --- |
| `/workspace/platform/organizations` | SuperAdmin organization list/search/pagination, confirmed atomic creation, explicit verified selection |
| `/workspace/platform/audit` | Separate read-only platform/account history, UTC filters and deterministic pagination |
| `/workspace/platform/audit/:eventId` | Authorized account event detail, no raw metadata |
| `/account/forgot-password` | Explicit generic recovery request; no registration |
| `/account/reset-password` | Private server-link status and explicit password reset |
| `/account/invitation` | Private invitation preview, matching existing-account sign-in, confirmed final acceptance |

Shell platform navigation is available with or without a selected organization. Existing organization administration and audit links remain scoped to explicit selection. UI guards are advisory; every BFF operation authenticates the cookie, validates the upstream session/stamp and rechecks stored platform authority where applicable. Selecting an organization calls the existing `/bff/organization` action through `AuthService.selectOrganization`; it resolves the authorized organization/context server-side. Creation neither selects an organization nor grants the actor membership.

## Exact BFF contracts

All JSON responses use existing non-cacheable BFF error handling. GUIDs are strings, UTC dates are ISO 8601. No actor/organization authority, JWT, raw account token or physical path is accepted from the browser in these contracts. Unknown extra JSON properties do not become upstream authority.

| Method and route | Request / response / upstream |
| --- | --- |
| `GET /bff/platform/organizations` | `page` 1..10000, `pageSize` 1..50 (defaults 1/20), optional `search` <=100; role filter rejected. Returns existing `{data:{organizationId:null,page,pageSize,total,items:[{id,name,isActive,code}]},filter,notice}`. Stored SuperAdmin check via `/api/organizations/platform/context`, then existing `GET /api/organizations` with no organization header. |
| `POST /bff/platform/organizations` | `{code,name,initialAdminEmail,confirmed:true}`; code <=50, name <=200, required exact email <=256; existing API validation remains authoritative. Returns 204. Reuses `CreateOrganization` and atomic `POST /api/organizations`, never creates the initial account. CSRF + existing organization-administration limiter. |
| `GET /bff/platform/audit` | `page` 1..10000, `pageSize` 1..50; `fromUtc`,`toUtc`, `action`,`outcome`,`actorId`,`resourceId`. UTC window <=31 days, inclusive start/exclusive end; API defaults normalize to the last seven days. Returns typed `{page,pageSize,total,filter,items,actions,outcomes}` from existing `GET /api/audit/platform`. Freeze the normalized window while paging. |
| `GET /bff/platform/audit/{id}` | Existing `/api/audit/platform/{id}`, exact returned ID checked. Typed row `{id,timestampUtc,actorUserId,scope,action,resourceType,resourceId,outcome,correlationId,metadata}`. |
| `POST /bff/account/forgot-password` | `{email}` required valid syntax <=256; upstream `/api/account/forgot-password` using server-only API antiforgery. Always generic after an accepted valid BFF request, including uncertain upstream delivery. BFF CSRF/model/limiter failures still return 400/429. |
| `GET /bff/account/reset-password` | `{available:true}` only when the protected reset link session exists. This checks link-session presence, not Identity token validity; the existing reset API validates the token only on final POST. No business write. |
| `POST /bff/account/reset-password` | `{password}` required <=1024; private session supplies email/token to `/api/account/reset-password`. Returns 204 on success. BFF CSRF required; upstream CSRF remains server-side. No automatic login. |
| `GET /bff/account/invitation` | Calls existing read-only `POST /api/account/invitations/preview` with private token; returns `{email,organization,role,existingAccount}` only. Role 1 Student /2 Instructor /3 Admin. No invitation acceptance. |
| `POST /bff/account/invitation` | `{firstName,lastName,password,confirmed:true}`; names <=100, password <=1024; existing API validates required new-account values. Server supplies token and verified session bearer, if authenticated, to `/api/account/invitations/accept`. Returns 204. CSRF + explicit browser confirmation. |

400 denotes invalid form/security/link, 401 expired authentication, 403 denied authority/matching account, 404 missing authorized resource, 409 stored-state conflict, 429 rate limiting, 502 unavailable/uncertain upstream transport. The existing BFF login policy is shared at 10 requests/minute per IP for account pages/entry; upstream recovery remains 5/minute per API-observed IP. Platform creation retains 10/minute per actor (IP fallback) and upstream administration limits. No limiter was weakened. Error bodies remain sanitized existing BFF JSON; Angular uses fixed contextual messages rather than reflecting diagnostics. API transaction, uniqueness, last-Admin and membership policies are unchanged.

Platform audit validates null organization and platform/account scope in the existing client, then uses a separate strict projection. Current durable non-organization events are only `account.password-reset`, scope `account`, resource `account`, outcome `succeeded`, empty metadata. Unexpected scope/action/metadata fails closed. Organization creation is an **organization** event in existing policies and remains in organization history. This batch does not fabricate platform events or move tenant actions into global history. Future platform event types need an explicit reviewed projection. Organization metadata allowlisting is unchanged.

## Trusted public links and /app deployment

API configuration `AccountLinks:PublicUrl` (`AccountLinks__PublicUrl`) remains an explicitly trusted HTTPS **origin root**, without credentials, path, query or fragment. It never uses request Host/forwarded Host. The new optional `AccountLinks:Destination` (`AccountLinks__Destination`) accepts exactly `Mvc` (also the missing/empty default) or `Angular`. Any other value fails link generation. MVC remains the default and all existing MVC account routes/views continue working.

| Destination | Email path on configured trusted origin |
| --- | --- |
| default / `Mvc` | `/account/invitation?token=...`, `/account/reset-password?token=...&email=...` |
| `Angular` | `/account/angular/invitation?token=...`, `/account/angular/reset-password?token=...&email=...` |

The **Web server entry** captures the token into the existing protected server-memory `AccountLinks` store and immediately redirects to `/app/account/invitation` or `/app/account/reset-password`. This GET does not preview/accept/reset or call the API. The redirect has no token, no response body/resources, no-store, no-referrer and `default-src 'none'`. The cookie is random/protected, HttpOnly, Secure, host-only, purpose separated and expires after 20 minutes; raw tokens remain encrypted in server memory, not in the cookie, response DTOs, browser storage or redirect. The existing architecture requires sticky server sessions; process restart, eviction or expiry loses the private link. Reopening the original email starts another private session without consuming the business token.

Final Angular reset/accept removes the private link session **before upstream dispatch**. Even an ambiguous network outcome cannot be replayed with that session. Passwords clear immediately in Angular and in server form objects. Known validation/conflict failures also require reopening the original email or requesting a new link; this deliberately favors safe explicit recovery over retaining a reusable private session. The underlying Identity/invitation token is consumed only by the existing successful business mutation. On ambiguity, Angular performs only status/preview GETs; after session removal these correctly report unavailable. Reset recovery is fresh sign-in or a new recovery request; invitation recovery is organization-list inspection or administrator inspection. Neither page guesses success or automatically retries. Invitation name values may remain in memory for explanation, but passwords never repopulate. Matching email, EmailConfirmed, inviter authority, expiry, revocation, single use, no SuperAdmin membership target, and membership conflicts remain backend policies.

Production deployment is opt-in:

1. Run `npm.cmd run build` (production default). The production configuration emits `/app/` asset URLs and injects Angular `APP_BASE_HREF=/app/`. Development retains `/`.
2. Deploy **only** `dist/enterprise-lms-angular/browser/` contents into `LMS.Web/wwwroot/app/` as deployment artifacts. No artifacts were copied/deployed by this batch.
3. Set **Web** `Angular__Enabled=true` to enable scoped public static serving and `/app` deep-route HTML fallback. Missing files/build return 404. Dotted asset paths do not fall back to HTML. `/bff`, MVC and `/account/angular/*` do not fall back to the SPA. Static assets run before endpoint routing and are publicly served; they contain no credentials.
4. Keep `AccountLinks__Destination=Mvc` until the deployed same-origin `/app` build is verified. Opt in to Angular explicitly on the **API** only after successful verification. Point `AccountLinks__PublicUrl` to that Web origin. Existing MVC email links remain usable regardless of the option.
5. Retain trusted `LmsApi__BaseUrl`, HTTPS certificate validation, cookies, CSRF, API rate limits and private storage configuration. Configure proxy/APM logs to omit account-link query strings and all credentials/bodies. Existing app hosting/MVC diagnostic filters and outbound HttpClient logger removal remain in place; external logging cannot be verified here.

The development proxy continues forwarding `/bff/**` only. Angular email-link entry is intended for the configured same-origin production-style `/app` deployment, not a silent redirect to the development port 4200. Do not send Angular-destination emails into an undeployed `/app` environment.

## CSP and state handling

No unsafe HTML/script bypass, third-party account resources, CSP weakening or cookie/TLS changes were added. Production uses a separate index **without `<base>`**, preserving `base-uri 'none'`; routing gets its base through Angular DI. Critical inline CSS/font inlining is disabled; generated scripts/styles use explicit same-origin `/app` URLs. Output inspection confirms no inline style/script/event handlers or base tag. Account SPA documents restrict frames to `none` and framing to `none`; other existing viewer/media policies remain unchanged. All account paths retain no-referrer and no-store, with an index meta referrer fallback. Existing YouTube content remains available on authorized viewer routes only.

Before Angular bootstrap/session restoration, an accidentally token-bearing SPA entry removes its query with `history.replaceState` and refuses to process it; trusted generated email links always use the server capture route before SPA boot. This fallback is not a substitute for correctly configured links or proxy logging. No token or password uses local/session storage. Angular account preview, input and platform data clear on identity/context changes, logout, expiry and destruction. Session/epoch/sequence checks reject stale reads and mutations; pending guards prevent duplicate dispatch. Stored platform state is refreshed after creation; uncertain creation locks further create actions and performs a read-only code search. No automatic selection/membership grant occurs.

## Changed files in this batch

Frontend new: `src/app/features/platform/platform.component.{ts,html,spec.ts}`, `src/app/features/account/account.component.{ts,html,spec.ts}`, `src/environments/environment.ts`, `environment.production.ts`, `src/index.production.html`, `src/app/core/auth/account-entry.ts` and its spec, this report.

Frontend integration: `angular.json`, `src/main.ts`, `src/index.html`, `src/app/app.config.ts`, `app.routes.ts`, `core/auth/auth.guard.ts`, `auth.service.ts`, `workspace-routing.ts`, `layout/application-shell.component.ts`, `features/workspace/workspace.component.ts` and its spec, `features/auth/login/login.component.ts`, template and spec, `README.md`, `docs/bff-authentication.md`.

Backend new: `LMS.Web/Controllers/BffPlatformAccountController.cs`, `AngularAppController.cs`, `LMS.Tests/Integration/BffPlatformAccountTests.cs`, mirrored report. Backend integration: `LMS.Web/Program.cs`, `Services/LearnerSecurity.cs`, `LMS.Infrastructure/Identity/AccountLinkPolicy.cs`, `AccountOnboarding.cs`, relevant documentation links. Many other changes already existed from earlier batches and remain unstaged.

## Verification and prerequisites

- Angular production build passed. All **204** Angular tests passed, including explicit actions/CSRF, safe text, confirmation, payloads, ambiguity, stale responses, matching-account UI, token URL cleanup, password clearing and reset CSRF/session transition. Existing catalog, viewer, progress, quizzes, assignments, gradebook, shell and staff/admin tests pass.
- Existing LMS.Tests relevant regression filter passed **994** tests, no skips, with SQL integration excluded. New isolated in-process tests exercise stored SuperAdmin denial/revalidation, no-selected-org list/create, CSRF/confirmation, narrow payloads, separate platform audit/metadata, clean token redirects, missing tokens, generic forgot/outages, one-dispatch ambiguous reset/accept, matching-account bearer forwarding, rate-limited JSON and scoped SPA/header behavior. Existing tests exercise exact verified email, inviter authority, membership conflicts, 48-hour invitation expiry boundaries/revocation/single use, real Identity one-hour expiry/password rules/stamp rotation and JWT/session revocation with mocked stores/fake email; no database connection or real email is required.
- Full no-restore solution build passed with 0 warnings and 0 errors. The final audit DTO refinement also passed all 24 focused platform/account tests. Existing NU1510 package-pruning warnings appeared during the test-project build; no dependency changes were made.
- Node regression checks: **2 passed**; syntax checks passed for existing MVC JavaScript and those tests.
- Whitespace checks passed for both tracked working-tree diffs and all new files in this batch.

No schema migration was added or applied. Do not assume prior migrations were applied. Deployment requires the full existing chain, particularly `20261004061629_AddLessonAssignments`, `20261006122454_AddOrganizationInvitations`, `20261006135202_AddAuditEvents` and their predecessors, plus durable Identity/data-protection/session configuration. Audit writes and atomic organization creation need the existing schema. Existing production email sender is intentionally unconfigured and refuses delivery until separately configured; forgot remains generic. No email infrastructure was tested or configured here.

## Manual checklist for later authorized verification

1. Deploy disposable `/app` artifacts on the same trusted HTTPS Web origin; verify direct deep links/refresh, lazy chunks/styles, router base, no CSP violations, no inline/external account resources, no root/MVC takeover and 404 missing assets. Test keyboard/mobile forms, focus and screen readers. These are not live-browser verified.
2. With isolated database fixtures and fake email, test stored/revoked SuperAdmin, no selected org, bounded org search/pagination, exact-email initial Admin/atomic rollback, duplicate code, unavailable account, limiter and explicit selection without actor grants. Compare organization versus account audit counts/windows/filters and inspect all JSON for unrelated tenant records or metadata.
3. Keep MVC destination default and inspect fake captures; then opt in to Angular with the trusted origin and verify clean redirects/cookies/address bar/history, restrictive headers, missing/evicted/20-minute private sessions, existing MVC links and password-manager behavior. Verify proxy/application/APM logs never retain token queries or password bodies.
4. Test new-account invitation, authenticated verified matching/mismatched/unconfirmed accounts, conflicting roles, withdrawn inviter authority, inactive organization, revoked/reissued/used/48-hour expiry boundaries, CSRF before/after login, duplicate clicks and interrupted acceptance. Confirm GET never consumes an invitation and uncertain outcomes never replay.
5. Test known/unknown forgot with identical generic response, fake sender failure/rate limiting; reset invalid/expired/used tokens, weak/valid password, double clicks, network interruption and browser back/refresh. Verify password fields clear, fresh login is required and previous JWTs/cookies are rejected on subsequent requests. Confirm historical browser/DB/email behavior rather than inferring it from isolated tests.

Remaining live gaps: no deployed browser/end-to-end `/app` routing/CSP/rendering test, no DB migration/query/transaction verification, no real email delivery/click test, no production proxy logging inspection, no distributed/session-restart deployment test. No live playback/PDF verification was performed; previous viewer/media gaps remain unverified. The APIs intentionally expose no reset-token validation endpoint; reset status is only private session presence. No new platform audit event rules, public registration, global-role editing, account deactivation or membership deletion was added.
