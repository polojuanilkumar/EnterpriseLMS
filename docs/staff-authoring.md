# Angular staff authoring

The Angular staff quiz/assignment authoring and grading extension is documented in [staff-assessments.md](staff-assessments.md). This earlier record remains as implementation history.

Implemented 2026-10-15. This batch reuses the staff MVC clients and guarded business APIs inspected in backend `docs/staff-web.md`, `AuthoringController`, `StaffController`, `AuthoringApi`, `StaffApi`, organization membership selection, lesson-content handlers and private media infrastructure. Earlier Student catalog, enrollment, viewer, progress, quiz, assignment and grades workflows remain in place.

## Scope and authorization

The Angular shell and workspace landing link to `/workspace/staff/courses` for Instructors, selected-organization Admins and SuperAdmins with an explicitly selected organization. Instructor lists contain assigned courses; Admin/platform lists use the selected organization. The browser sends no organization authority or bearer token. Route guards are advisory.

Every BFF operation resolves the protected server session. Its selected organization is mandatory, including for SuperAdmin. Existing clients revalidate live organization context, actor identity, role, course ownership, course/section/lesson/item relationships and upstream organization scope. Browser organization headers/query parameters cannot select a tenant. Instructor creation forces assignment to the verified actor; Admin/platform creation revalidates stored Instructor membership, rather than trusting the displayed choice page. Course editing retains assignment; the separate MVC reassignment feature is outside this batch.

All writes use the existing antiforgery cookie plus `X-CSRF-TOKEN`, including multipart uploads. GET and HEAD never mutate learning or publication state. Readiness reads quiz publication metadata only; there is no quiz authoring, grading, member administration, reporting, progress write or completion/scoring policy change.

## Exact Angular routes

Let `A=/workspace/staff/courses`, `C=A/{courseId}`, `S=C/sections/{sectionId}`, `L=S/lessons/{lessonId}`. IDs are GUIDs.

| Route | Page |
| --- | --- |
| `A` | Assigned/organization course list |
| `A/new`, `C/edit` | Course create/edit |
| `C` | Ordered sections and course publication actions |
| `C/sections/new`, `S/edit` | Section create/edit |
| `S` | Ordered lessons, including unpublished lessons |
| `S/lessons/new`, `L/edit` | Lesson create/edit, including legacy fields |
| `L` | Ordered content, authorized previews, delete/reorder and lesson publication |
| `L/items/new`, `L/items/{itemId}/edit` | Content editor and explicit upload/attachment |
| `C/readiness` | Informational publication inventory |

Routes and the authoring component load lazily inside the existing responsive role shell. Forms have labelled controls, field-limit feedback, breadcrumbs, loading/empty/error states and unsaved-navigation confirmation. Safe form values survive validation/business conflicts and Instructor-choice pagination. No form/file state is persisted to browser storage.

## Exact BFF routes and contracts

Let `B=/bff/staff/courses`, `BC=B/{courseId}`, `BS=BC/sections/{sectionId}`, `BL=BS/lessons/{lessonId}`. Successful JSON reads are typed page objects without an API envelope. Successful writes return 204, except upload, which returns safe asset metadata. There is no generic proxy or asset-ID media route.

| Method and route | Contract / existing business API |
| --- | --- |
| `GET B` | `StaffCoursesPage { access, courses }`; `GET api/Courses`, filtered for Instructor ownership |
| `GET B/new`, `GET BC/edit` | `CourseEditor { organizationId, courseId, form, categories, instructors, isAdmin, choices }`; categories from `GET api/Categories`, existing course from `GET api/Courses/{C}` |
| `POST B`, `PUT BC` | `CourseForm`; existing `POST api/Courses` / `PUT api/Courses/{C}` |
| `GET BC` | `SectionsPage { organizationId, course, sections }`; existing course/section reads |
| `GET BC/sections/new`, `GET BS/edit` | `SectionEditor { organizationId, courseId, sectionId, form }` |
| `POST BC/sections`, `PUT BS` | `SectionForm`; existing `api/Courses/{C}/sections[/{S}]` POST/PUT |
| `GET BS` | `LessonsPage { organizationId, course, section, lessons }`; existing `GET api/Sections/{S}/lessons` after course/section checks |
| `GET BS/lessons/new`, `GET BL/edit` | `LessonEditor { organizationId, courseId, sectionId, lessonId, form }`; existing lesson details from `GET api/Lessons/{L}` |
| `POST BS/lessons`, `PUT BL` | `LessonForm`; existing `POST api/Sections/{S}/lessons` / `PUT api/Lessons/{L}` |
| `GET BL` | `{ organizationId, courseId, sectionId, lesson, content: { items, legacy } }`; items from `GET api/Lessons/{L}/content-items`; legacy includes plain `content`, editable `videoUrl` and server-validated `youTubeVideoId` |
| `POST BL/items`, `PUT BL/items/{I}` | JSON content fields below; existing `api/Lessons/{L}/content-items[/{I}]` POST/PUT |
| `POST BL/items/{I}/delete` | `{ confirmed:true }`; existing item DELETE after stored-parent validation |
| `PUT BL/items/reorder` | `{ itemIds:Guid[] }`; exact current complete set once each; existing reorder PUT, which assigns zero-based positions |
| `POST BC/publication` | `{ confirmed:true, action:"publish"|"archive" }`; existing course POST action |
| `POST BL/publication` | `{ confirmed:true, action:"publish"|"unpublish" }`; existing lesson PATCH action |
| `GET BC/readiness` | `ReadinessPage { organizationId, course, sections:[{ section, lessons:[{ unit, quiz }] }] }`; existing scoped hierarchy/content reads and `GET /{lessonId}/quiz`, with 404 meaning no quiz |
| `POST BL/uploads` | Multipart `type` (3/4/5) and `file`; existing `POST api/Lessons/{L}/media-assets`; returns `{ id, lessonId, organizationId, type }` only |
| `GET/HEAD BL/items/{I}/media[?download=true]` | Item-scoped private-media proxy; revalidates staff/ownership/entire parent chain on every request, including ranges |

Optional nested IDs are explicitly route-bound. Instructor-choice query fields on the course editor use the existing `OrganizationFilterForm`: `Page` 1–10000, `PageSize` 1–50, `Search` up to 100 characters. The server fixes `Role=2` (Instructor). Angular requests 50 choices per page and retains a prior selection while paging. The server checks page/tenant/role metadata and independently fetches/revalidates the chosen membership at creation. The existing course-list API is unpaginated; this batch does not invent a different list contract.

Forms retain existing names and rules:

- Course: `title` required/max200, `code` required/max50, `description` required/max5000, optional `categoryId`, creation `instructorId`, `level` 1/2/3, nonnegative `durationInMinutes`, optional `thumbnailUrl` max500. Instructor assignment is server-forced for Instructor creation.
- Section: `title` required/max200, optional `description`, `displayOrder` at least1.
- Lesson: Section fields plus `lessonType` 1 Text/2 Video, `content`, `videoUrl`, nonnegative `durationInMinutes`. Editing roundtrips legacy values independently of content items; no automatic conversion or deletion.
- Content: `type` 1 Text/2 YouTube/3 MP3/4 MP4/5 PDF, `title` required/max200, nonnegative `displayOrder`, `isRequired`, `text`, `youTubeUrl`, `assetId`. JSON save does not upload a file. Type changes clear unrelated values and old attachment selection. Existing API/domain validation remains authoritative for valid content and matching organization/lesson/asset/type.
- Reorder request arrays are bounded at10000 entries and checked against the full current item set. Concurrent edits require refreshing; there is no added concurrency token.

`400` validation/CSRF, `401` expired session, `403` denied scope/ownership, `404` unavailable or mismatched stored parent, `409` business conflict, `413` oversize, `429` rate limit and `502` upstream/transport failure remain sanitized JSON. Angular never renders upstream diagnostic messages. Forms show local field hints and explain business conflicts without changing server rules. The shared error envelope is reused unchanged.

## Upload, preview, retention and failure behavior

MP3/PDF limits remain25 MiB; MP4 remains200 MiB. Client extension/MIME/length checks improve UX; the API still checks declaration, signature, stored parent and type. BFF multipart requests are capped at202 MiB (upstream201 MiB); other authoring requests are capped at1 MiB. ASP.NET may buffer multipart data before the stream is forwarded; this is not a claim of end-to-end unbuffered upload.

Uploading and saving an item are deliberately separate explicit actions. A successful upload leaves its asset ID in memory. An attachment validation/conflict failure retains that ID and safe entered values, allowing deliberate attachment retry without uploading again. File inputs are cleared after upload/context cleanup. Replacing/deleting an item never deletes committed assets; there is no new cleanup endpoint. Existing API staged-file cleanup, committed retention and orphan/crash recovery limitations remain unchanged.

Mutations disable duplicate actions. Deletion and course/lesson publication require explicit confirmation. No automatic mutation retry is installed. After an uncertain submitted write, Angular locks further writes and performs only the relevant GET to display stored state alongside retained values. An uncertain Course/Section/Lesson create also reads the parent collection and links stored records for inspection; a blank new form is not treated as proof that creation failed. The user must inspect it, leave and reopen before making another explicit change. An uncertain upload cannot discover a lost returned asset ID; the UI explains retention and does not replay it. Refresh is read-only. A write already in flight can still finish after navigation/context change; late results cannot repopulate the new context.

The component rejects stale results using context identity, an epoch and read sequence; logout, expiry, organization switching, uncertain context, routed selection changes and destruction clear forms, choices, files, inventory and media sources. Native media is paused and detached; iframes lose their source and trusted-resource caches are cleared. Reads refresh media DOM so an edited attachment at the same item URL is reloaded. Revoked read/mutation access removes displayed authoring content. Media errors make an authorized HEAD request because native element errors cannot report HTTP status; denied previews are removed. A successful HEAD with playback failure reports format/browser limitations instead of assuming revoked access.

Text and legacy values use Angular interpolation; there is no HTML trust bypass. YouTube frames only use exact11-character video IDs and the fixed `https://www.youtube-nocookie.com/embed/` origin. Legacy video URLs are parsed by existing `SafeContent.LegacyYouTube` on the server; arbitrary legacy URLs are displayed as text only. The only ResourceUrl trust operation is for this validated embed URL or a validated item-scoped same-origin media URL.

MP3/MP4 use native accessible controls and metadata preload against authenticated same-origin media endpoints, with no blob/full-file fetch in Angular. Range, If-Range,206/416, Content-Range, Accept-Ranges, ETag, Last-Modified, canonical MIME, private/no-store, nosniff and generated safe disposition are handled by the existing client/proxy. PDF has an empty iframe sandbox, the existing restrictive PDF response CSP and an external download fallback. MP3/MP4 disposition remains the existing inline policy; PDF `download=true` uses attachment disposition. The media contract supplies no caption track; the UI states this limitation.

## CSP and readiness decisions

No security headers were relaxed. Existing `LearnerSecurity` permits same-origin media/frames and privacy-host YouTube frames, with self-only scripts/styles, no objects and same-origin frame ancestors. The new component adds no inline scripts, inline style bindings or component CSS injection. PDF keeps the existing `sandbox; default-src 'none'; frame-ancestors 'self'` response policy. Actual browser PDF/YouTube compatibility under deployed headers remains unverified; no CSP bypass was added to make previews appear to work.

Course/lesson publishing retains existing rules: no new content prerequisites. Readiness lists course status, ordered sections, lesson publication, ordered item titles/types, legacy fallback and independent quiz presence/publication. Missing content/quiz is informational. It does not inspect quiz questions/options, audit quiz configuration, predict successful publication, guarantee an atomic snapshot or lock state across requests. Repeated hierarchy checks and inventory aggregation can be expensive for large courses.

## Changed files in this batch

Frontend additions: `src/app/features/staff-authoring/staff-authoring.routes.ts`, `staff-authoring.models.ts`, `staff-authoring.component.ts`, `staff-authoring.component.html`, `staff-authoring.component.spec.ts`.

Frontend integration: `src/app/app.routes.ts`, `src/app/app.routes.spec.ts`, `src/app/core/auth/auth.guard.ts`, `src/app/core/auth/workspace-routing.ts`, `src/app/layout/application-shell.component.ts`, `src/app/features/workspace/workspace.component.ts`, `src/app/features/workspace/workspace.component.spec.ts`, `README.md`, this report.

Backend additions: `LMS.Web/Controllers/BffStaffController.cs`, `LMS.Web/Services/BffStaffApi.cs`, `LMS.Tests/Integration/BffStaffTests.cs`, mirrored `docs/staff-authoring.md`. Backend `docs/staff-web.md` links this report. Existing business handlers, storage/proxy implementation, MVC controllers/views, entities, migrations and packages were not changed by this batch.

## Migration and deployment prerequisites

No migration was added, applied or assumed applied. A separately authorized deployment must review the full existing schema chain, including `20260928081231_AddOrganizationFoundation`, `20260929151614_AddLessonContentItems`, `20260929160637_AddPrivateLessonMedia`, `20260929175839_AddPdfPresentations`, existing quiz migrations and current application dependencies. Pending `20261004061629_AddLessonAssignments`, `20261006122454_AddOrganizationInvitations` and `20261006135202_AddAuditEvents` are not presumed deployed. No database was accessed or inspected.

Durable private-media storage configuration/permissions, API and BFF request limits, reverse-proxy limits/timeouts, same-origin HTTPS cookie/CSRF configuration, and reviewed file/database backups remain deployment prerequisites. This batch runs no cleanup, seeding, migration, normal host or storage-maintenance command.

## Checks and remaining live gaps

Verification on 2026-10-15:

- `npm.cmd run build -- --configuration production`: passed; staff routes/component are lazy chunks.
- `npm.cmd test -- --watch=false --browsers=ChromeHeadless --progress=false`: **151 passed**, including16 added staff/routing tests and updated workspace-link assertions.
- `dotnet test LMS.Tests/LMS.Tests.csproj --no-restore --filter 'Category!=SqlServerIntegration&(FullyQualifiedName~Bff|FullyQualifiedName~Learner|FullyQualifiedName~Authoring|FullyQualifiedName~Staff|FullyQualifiedName~LessonContent|FullyQualifiedName~Media|FullyQualifiedName~LearningAccess|FullyQualifiedName~Enrollment|FullyQualifiedName~Organization|FullyQualifiedName~Quiz|FullyQualifiedName~Assignment|FullyQualifiedName~Reporting)' -m:1 -p:UseSharedCompilation=false --logger 'console;verbosity=minimal'`: **825 passed**, including25 new staff BFF cases. Coverage includes live/cached role differences, Student/owner/tenant/nested/context/revocation denial, unselected SuperAdmin, bounded Instructor choices and membership revalidation, all content types, legacy preservation, CSRF/multipart, confirmations, exact reorder, safe errors/no replay, GET/HEAD/ranges/headers, and existing learner/MVC/business regressions.
- `dotnet build EnterpriseLMS.slnx --no-restore --no-incremental -m:1 -p:UseSharedCompilation=false`: passed,0 warnings/errors.
- `node --test LMS.Tests/assignments-ui.test.cjs LMS.Tests/quiz-ui.test.cjs`:2 passed; `node --check` on both scripts and every existing `LMS.Web/wwwroot/js/*.js`: passed.
- `git -c core.safecrlf=false diff --check` in both trees and direct whitespace checks of new staff source/tests/report: passed.

Tests use HTTP fakes/in-process test hosting or Angular HttpTestingController; they do not validate a real database, durable file storage or end-user playback. The Angular suite can emit expected404 warnings from iframe fixture URLs; those are not a live media test. Existing Karma/Webpack deprecation and backend test-build NU1510 package-pruning warnings are unchanged; no packages were added or removed. Initial checks caught and corrected explicit JSON binding for content saves, a test import and the prior workspace-link count; final checks above pass.

Branches remain `chore/angular-dependency-upgrade` and `feature/learning-flow-stability`. Both earlier working trees were preserved. No staging, commits, pushes, branch switches, schema changes, database access, migrations or normal hosts were used. Browser playback/PDF rendering, live permission/deadline races, real storage cleanup/retention and deployed migration state remain unverified.

One later manual checklist, in a separately authorized disposable environment:

1. Review actual migration state and durable private-storage/proxy/HTTPS prerequisites without assuming pending migrations are deployed.
2. As Instructor, verify only assigned courses; as Admin, verify only the selected organization; as SuperAdmin, require explicit selection. Try foreign course/section/lesson/item IDs and revoked memberships/ownership, including media GET/HEAD/ranges.
3. Create/edit each hierarchy level, retain values after validation/conflicts, page/search Instructor choices, verify Instructor self-assignment and Admin stored-membership revalidation, and preserve legacy Content/VideoUrl.
4. Create/edit all five content types, reject invalid YouTube/file formats/signatures/limits, upload then attach, deliberately fail attachment and retry only attachment. Verify delete/replace retention, explicit reorder and concurrent-item-set conflicts.
5. Verify text injection stays text, only validated YouTube embeds load, MP3/MP4 play/seek with206/416 and safe headers, PDF renders under actual sandbox/CSP or downloads safely. Check keyboard/screen-reader controls, responsive forms, and available captions; playback/PDF rendering is unverified until this is done.
6. Cancel/confirm each publication/delete action, double-click actions, interrupt upload/save responses and confirm GET-only recovery without replay. Compare stored state before reopening an uncertain editor.
7. Switch organizations, logout, expire/revoke access and navigate during reads/writes/playback; confirm old text/files/media disappear and late responses cannot populate a new context. Confirm no viewing, playback, readiness or navigation changes lesson/course progress.
8. Recheck existing MVC authoring and Student catalog/enrollment/content/progress/quiz/assignment/grades workflows against the live deployment. Do not treat automated fakes as proof of database/storage atomicity or browser rendering.
