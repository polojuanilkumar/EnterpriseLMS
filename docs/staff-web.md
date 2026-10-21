# Role-based organization workspaces and staff authoring

Angular staff authoring routes, contracts, verification and live gaps: [staff-authoring.md](staff-authoring.md). This report preserves the existing MVC workflow below.

The [organization administration batch](organization-member-platform-administration.md) extends the member UI with bounded filters, exact-email creation, confirmed guarded role changes, and verified platform pages. It adds stored-membership checks coordinated with course assignment/enrollment saves. Its contracts supersede the original member form description in this report.

This batch extends the existing protected web session, learner viewer, Organization foundation, and lesson-content features in the same working tree. No database access, schema change/migration, completion-policy change, membership deletion, or manual browser session was performed.

## Verified organization selection and navigation

The shared organization chooser no longer calls the Student-only enrollment/progress endpoint. Selection obtains the account's allowed active organizations, then calls the new organization-scoped context endpoint. OrganizationMiddleware validates stored membership and checks platform SuperAdmin in stored Identity role assignments, replacing global JWT role claims. The context response returns OrganizationId, UserId, Role (Student/Instructor/Admin or null), and IsSuperAdmin. Web validates matching organization/user and recognized roles, then rotates the protected server session.

Routing precedence:
- Verified platform SuperAdmin -> /admin/{organizationId} (explicit target selection required).
- Organization Admin -> /admin/{organizationId}.
- Organization Instructor -> /staff/{organizationId}/courses.
- Organization Student -> /learn/{organizationId}/courses.

/account/home rechecks stored access and refreshes changed session role metadata before routing. Staff actions revalidate the current context on every operation; cached roles and global JWT claims do not grant authorization. An organization in the route must match server-side selection. Switching organizations still requires verified membership/platform access. Sessions without a selection cannot access staff/admin even when SuperAdmin.

Learner routes remain Student-only: web checks the selected role, while existing Student-only API progress checks still enforce current role, publication and enrollment. The Student-only dependency is removed only from shared selection, not learner authorization. Access denial is terminal HTTP 403 HTML with safe navigation links, not automatic redirect chains. Media denials remain plain responses without leaking upstream data. /account/access-denied also returns a standalone 403 page.

JWTs remain in protected server-side memory; browser cookies remain Secure/HttpOnly/SameSite Strict with opaque session references. Existing TLS checks, outbound log suppression, absolute expiry, logout revocation, no-store, encoded rendering and PDF sandboxing remain. The earlier instance-local-session deployment limitations still apply.

## API authorization changes

New endpoints, all using Bearer authentication and the existing X-Organization-Id header:

| Method | Route | Behavior |
| --- | --- | --- |
| GET | /api/organizations/{organizationId}/context | Current verified membership/platform context; route ID must equal selected scope; 200 or 403 |
| PUT | /api/Courses/{courseId}/instructor | Organization Admin/SuperAdmin only; JSON {"instructorId":"existing-instructor-user-guid"}; 200, 400 invalid target, 403 unauthorized, 404 foreign/missing course |

Instructor course list/detail queries now filter by stored InstructorId in SQL in addition to organization filtering. Admin/SuperAdmin retain visibility to courses within the selected organization only. Legacy section/lesson read authorization no longer treats a staff flag as sufficient: it resolves stored parents and requires current verified staff scope and Instructor course ownership. Existing content-item/media authoring handlers already perform these checks and are reused.

Reassignment loads the scoped course, validates Admin/platform context, and checks that the selected target has stored Instructor membership in that organization before changing InstructorId. The existing course-creation IdentityService instructor check was inspected: its organization roles already resolve through stored OrganizationMembership, not global Identity/JWT Instructor claims. No schema change is necessary for reassignment.

Existing API mutations reused: content-item POST/PUT/DELETE/reorder; POST lesson media-assets; PATCH lesson publish/unpublish; POST course publish/archive; organization member listing and role PUT. No alternate unguarded web-only write path is introduced.

## Web route inventory

All staff/admin routes require authentication and selected matching organization. Define:
- C = /staff/{organizationId}/courses/{courseId}
- L = /staff/{organizationId}/courses/{courseId}/sections/{sectionId}/lessons/{lessonId}

| Method | Route | Purpose |
| --- | --- | --- |
| GET, POST | /account/organizations | Choose/validate organization and route by stored role |
| GET | /account/home | Revalidate selected role and open its workspace |
| GET | /account/access-denied | Terminal 403 page |
| GET | /staff/{organizationId}/courses | Instructor's assigned courses or Admin's organization courses |
| GET | C | Course and ordered sections, including drafts |
| GET | C/sections/{sectionId} | Ordered lessons, including drafts |
| GET | L | Content items, draft preview and legacy fields |
| GET, POST | L/items/new | Create content item; optional private upload then attachment |
| GET, POST | L/items/{itemId}/edit | Edit/type change/replace attachment |
| GET, POST | L/items/{itemId}/delete | Confirmation and item deletion |
| GET, POST | L/reorder | Complete-set reorder with distinct entered positions |
| GET, POST | C/publication | Confirm course publish/archive |
| GET, POST | L/publication | Confirm lesson publish/unpublish |
| GET, HEAD | L/items/{itemId}/media | Authorized staff media preview, preserving range/HEAD |
| GET | /admin/{organizationId} | Selected organization administration |
| GET, POST | /admin/{organizationId}/members | List memberships; assign/update existing user's role |
| GET, POST | /admin/{organizationId}/courses/{courseId}/instructor | Select stored Instructor membership and assign course |

For publication GETs, supply actionName=publish/archive for courses or publish/unpublish for lessons. POST contains the same actionName plus confirmed=true and antiforgery token; missing confirmation is 400 and does not call a mutation. Deletion also requires confirmed=true. Successful modifying forms redirect (302) to their resource pages. GET requests never publish or delete anything.

All modifying forms have antiforgery protection, through the existing global filter and explicit staff/admin controller attributes. Role assignment accepts only 1=Student, 2=Instructor, 3=Admin. It does not grant platform SuperAdmin, delete memberships or create accounts. Existing API self-demotion protection remains. Memberships are displayed by existing user GUID because the current API exposes IDs/roles rather than user-directory profiles.

## Content form and preview behavior

Forms support Text=1, YouTube=2, MP3=3, MP4=4, PDF=5. Fields are Title, Type, DisplayOrder, IsRequired, Text, YouTubeUrl, AssetId, and optional File. Use Text only for type 1, a supported YouTube URL only for type 2, or a private asset ID/file for types 3/4/5. Clear fields for other types when switching. Clear AssetId before replacing it with an uploaded file. API validates asset organization, lesson and type and validates uploaded bytes; web parent/ownership checks run before the upload/write.

MP3/PDF remain 25 MiB; MP4 remains 200 MiB. API MIME/signature validation is unchanged. The web multipart request cap is 202 MiB including its authoring fields; the API media-upload request cap remains 201 MiB. Host/proxy limits must also allow the intended files. No file paths, public URLs or API tokens appear in forms/responses. File uploads are streamed server-to-server from ASP.NET's uploaded-file stream; ASP.NET may first buffer multipart content privately.

Upload and item attachment are two API operations. A successful upload is retained even if later attachment fails, and the form preserves the returned AssetId for a retry. There is no asset-delete cleanup endpoint. Existing history-safe retention policy remains. Replacing/deleting items does not remove committed assets. Reorder requires the exact current item set; additions/deletions between GET and POST may require reloading.

Text/titles/legacy fields are encoded. YouTube previews use only normalized IDs and the privacy-enhanced host. MP3/MP4/PDF previews use cookie-authenticated staff proxy URLs. Learner and staff proxies share response forwarding code while separately checking their own access chains. PDF remains sandboxed with a download fallback. Legacy fields are preserved; when no new items exist, staff see their encoded values. Required flags and publication changes do not change completion rules.

## Changed files in this batch

- LMS.Api/Controllers/OrganizationsController.cs
- LMS.Api/Controllers/CourseInstructorsController.cs
- LMS.Api/Program.cs
- LMS.Application/Features/Organizations/OrganizationHandlers.cs
- LMS.Application/Features/Lessons/Common/EnsureLessonReadable.cs
- LMS.Application/Features/Courses/AssignCourseInstructor/AssignCourseInstructorHandler.cs
- LMS.Domain/Entities/Course.cs
- LMS.Infrastructure/Persistence/LMSDbContext.Organizations.cs
- LMS.Infrastructure/Repositories/CourseRepository.cs
- LMS.Web/Controllers/AccountController.cs
- LMS.Web/Controllers/LearnController.cs
- LMS.Web/Controllers/StaffController.cs
- LMS.Web/Controllers/AdminController.cs
- LMS.Web/Models/LearnerModels.cs
- LMS.Web/Models/StaffModels.cs
- LMS.Web/Services/LearnerApi.cs
- LMS.Web/Services/LearnerSessions.cs
- LMS.Web/Services/LearnerSecurity.cs
- LMS.Web/Services/StaffApi.cs
- LMS.Web/Services/PrivateMediaProxy.cs
- LMS.Web/Views/Account/Login.cshtml
- LMS.Web/Views/Account/Organizations.cshtml
- LMS.Web/Views/Shared/_Layout.cshtml
- LMS.Web/Views/Staff/Courses.cshtml
- LMS.Web/Views/Staff/Sections.cshtml
- LMS.Web/Views/Staff/Lessons.cshtml
- LMS.Web/Views/Staff/Lesson.cshtml
- LMS.Web/Views/Staff/Edit.cshtml
- LMS.Web/Views/Staff/Delete.cshtml
- LMS.Web/Views/Staff/Reorder.cshtml
- LMS.Web/Views/Staff/Publication.cshtml
- LMS.Web/Views/Admin/Index.cshtml
- LMS.Web/Views/Admin/Members.cshtml
- LMS.Web/Views/Admin/Instructor.cshtml
- LMS.Web/wwwroot/css/site.css
- LMS.Tests/LMS.Tests.csproj
- LMS.Tests/LearnerWebTests.cs (updated shared selection expectations and reusable test fixture)
- LMS.Tests/TenantSqlTests.cs
- LMS.Tests/StaffAuthorizationTests.cs
- LMS.Tests/StaffWebTests.cs
- docs/staff-web.md

## Initial staff-workspace results and limitations (before hierarchy forms)

489 tests passed (51 added cases plus existing regressions). Full solution build with --no-restore succeeded with zero warnings/errors. SQL-filter tests intercept generated commands with an offline ADO stub and never open a database connection. Other new tests use mocked repositories/upstream HTTP, compiled Razor, session/authentication/antiforgery services and controllers. No running-file locks occurred, so a separate output directory was unnecessary. No database/browser integration was verified.

Requested workspace selection, authoring navigation/content forms/publication, and organization membership/course-Instructor assignment paths are implemented. The initial batch did not add top-level course/section/lesson creation/deletion forms; create/edit forms are now supplied by the follow-up below. It also did not add user-account provisioning, organization provisioning, membership deletion, or completion controls; existing APIs remain available within their existing scope. No concurrency tokens or transactional linkage across upload/attachment are added. Role/ownership changes are revalidated per request but not locked across multiple HTTP calls. Browser playback/PDF sandbox behavior and the live multipart path still require the manual session. Instance-local sessions and local asset retention limits remain as documented in earlier batches.

## Course / Section / Lesson authoring and publish-readiness follow-up

The follow-up adds missing hierarchy create/edit forms and navigation links, reusing all existing content, media, assignment and publication forms. New web form/view models are added; no new API endpoint, persistence model, migration or completion rule is introduced. JWTs remain server-side; every new modifying form uses antiforgery and encoded Razor rendering.

Latest automated results: **519 tests passed**, full solution build with `--no-restore` passed with **zero warnings/errors**, and `git diff --check` passed. These supersede the initial 489-test result above. Final review changed documentation only, so checks were not repeated. Tests use isolated repositories/HTTP fixtures, compiled Razor and authentication services; these results do not verify database persistence or a live browser session.

Exact new web routes (O = selected organization, C = course, S = section, L = lesson; all GUIDs):

| Methods | Route |
| --- | --- |
| GET, POST | /staff/{O}/courses/new |
| GET, POST | /staff/{O}/courses/{C}/edit |
| GET, POST | /staff/{O}/courses/{C}/sections/new |
| GET, POST | /staff/{O}/courses/{C}/sections/{S}/edit |
| GET, POST | /staff/{O}/courses/{C}/sections/{S}/lessons/new |
| GET, POST | /staff/{O}/courses/{C}/sections/{S}/lessons/{L}/edit |
| GET | /staff/{O}/courses/{C}/readiness |

Forms use `Form.*` field names. Course: Title, Code, Description, CategoryId, Level (1 Beginner / 2 Intermediate / 3 Advanced), DurationInMinutes, ThumbnailUrl, and InstructorId for Admin/platform creation. Section: Title, Description, DisplayOrder (1 or higher). Lesson: Title, Description, DisplayOrder, DurationInMinutes, LessonType (legacy Text=1 / Video=2), Content and VideoUrl. Existing Text/YouTube/MP3/MP4/PDF content items remain managed separately on the lesson page. Editing a lesson round-trips legacy fields; adding items does not delete them.

Successful POST redirects to the relevant list/parent (302); invalid form or API validation returns the same encoded form with entered values and error summary (400). Authorization failures remain terminal 403/404; expired authentication follows existing login handling. Assignment is forced to self for Instructor creation, while Admin/SuperAdmin creation lists and validates current stored Instructor members. Edit uses the existing separate Admin Instructor-assignment action. Organization comes only from the verified selected session and `X-Organization-Id`; it is never accepted from form data. Every edit/create re-resolves authorized stored parents before sending a mutation, and API handlers/repositories independently enforce ownership and organization scope.

Existing API routes used, with server-side Bearer and `X-Organization-Id`:

- GET /api/Categories (category choices).
- GET, POST /api/Courses; GET, PUT /api/Courses/{C}.
- GET, POST /api/Courses/{C}/sections; GET, PUT /api/Courses/{C}/sections/{S}.
- GET, POST /api/Sections/{S}/lessons; GET, PUT /api/Lessons/{L}.
- GET /api/Lessons/{L}/content-items.
- GET /{L}/quiz (the existing controller's actual root-level route; no new alias).
- GET /api/organizations/{O}/context and /api/organizations/{O}/members for verified access and Instructor choices.

Readiness lists course status, ordered sections/lessons/items and independent quiz publication states. Empty content, missing quizzes and legacy fallback are distinguished. Current Course.Publish / Lesson.Publish handlers have no content prerequisites; missing content is informational, not a blocking rule. Quiz publishing separately validates questions, positive marks, options and correct answers. This page displays quiz state rather than auditing its configuration. Existing confirmation GET/POST `/staff/{O}/courses/{C}/publication?actionName=publish` and `/staff/{O}/courses/{C}/sections/{S}/lessons/{L}/publication?actionName=publish` still require `confirmed=true`, antiforgery and fresh API policy/authorization checks. GETs never publish.

The existing quiz read guard previously returned immediately for staff. It now traverses the stored lesson/section/course through EnsureLessonReadable, requiring verified organization scope and Instructor ownership before returning draft/published quiz details. No learner mutation or completion access is added.

Files changed by this follow-up (earlier working-tree changes above are preserved):

- LMS.Application/Features/Quizzes/Common/EnsureQuizReadable.cs
- LMS.Web/Controllers/AuthoringController.cs (new)
- LMS.Web/Models/AuthoringModels.cs (new)
- LMS.Web/Services/AuthoringApi.cs (new)
- LMS.Web/Views/Authoring/Course.cshtml (new)
- LMS.Web/Views/Authoring/Section.cshtml (new)
- LMS.Web/Views/Authoring/Lesson.cshtml (new)
- LMS.Web/Views/Authoring/Readiness.cshtml (new)
- LMS.Web/Views/Staff/Courses.cshtml
- LMS.Web/Views/Staff/Sections.cshtml
- LMS.Web/Views/Staff/Lessons.cshtml
- LMS.Web/Views/Staff/Lesson.cshtml
- LMS.Tests/AuthoringWebTests.cs (new)
- LMS.Tests/AuthoringPolicyTests.cs (new)
- LMS.Tests/LearnerWebTests.cs (HTTP fixture extensions)
- LMS.Tests/LearningAccessTests.cs (establish verified staff context for the existing regression)
- docs/staff-web.md

Limitations: readiness is a multi-request snapshot, not a transaction or quiz configuration audit. Large courses incur multiple API calls. Concurrent role/ownership/publication changes are rechecked on each operation, but forms have no optimistic concurrency tokens. Existing API validation remains authoritative; unexpected upstream failures use generic error handling. Instructor choices display GUIDs because the membership API has no user-directory names. Hierarchy deletion and quiz authoring UI are not added. Private upload/attachment remains two requests with the existing asset-retention policy. Database persistence, browser forms, playback, seeking and PDF behavior require the later manual session. No database or browser integration has been verified.

## One consolidated manual checklist for later (not executed)

Use disposable accounts, courses, content items and files in organizations A and B. Session POSTs change session state. Authoring, uploads, assignment and publication POSTs change stored data; GET pages/readiness/media do not. Keep required existing infrastructure/schema prepared separately; this batch does not connect to a database or apply migrations.

1. Log in at /account/login over HTTPS and select A at /account/organizations. Verify Student goes to /learn/{A}/courses, Instructor to /staff/{A}/courses, Admin/SuperAdmin to /admin/{A}. SuperAdmin must select a target explicitly. Inspect that cookies are Secure/HttpOnly and no API JWT appears in HTML, JavaScript, URLs or browser storage. Denials must not redirect in a loop.
2. As Instructor, open /staff/{A}/courses/new; submit course title, unique code, description, category, level and duration. Expect redirect and self-assignment. Try invalid/missing fields and a duplicate code; expect clear retained form errors. Forge InstructorId: the course must still be assigned to the signed-in Instructor. Open /staff/{A}/courses/{C}/edit and confirm changes persist.
3. As Admin/SuperAdmin with A explicitly selected, create a disposable course using A's Instructor dropdown. Reject Student/Admin-only/nonmember/B-only Instructor IDs. Use /admin/{A}/courses/{C}/instructor to reassign; old owner loses authoring and new owner gains it. Verify /admin/{A}/members listing/role assignment remains scoped; do not delete memberships.
4. Create/edit a section at /staff/{A}/courses/{C}/sections/new and /staff/{A}/courses/{C}/sections/{S}/edit. Create/edit a lesson at /staff/{A}/courses/{C}/sections/{S}/lessons/new and /staff/{A}/courses/{C}/sections/{S}/lessons/{L}/edit. Check order >=1, duration >=0 and validation summaries retaining input. Set legacy Content/VideoUrl, save and reopen; verify values survive subsequent edits.
5. Open the lesson and reuse /items/new, /items/{I}/edit, /items/{I}/delete and /reorder under its full staff lesson URL. Create/edit Text and valid YouTube; reject unrelated/malformed YouTube URLs. HTML/script text must display as text. Verify the existing five content types remain available and legacy fallback is hidden while items exist, restored when the last disposable item is deleted.
6. Upload/attach disposable MP3, MP4 and PDF using the existing multipart content form. Limits remain MP3/PDF 25 MiB, MP4 200 MiB. Test wrong MIME/signature/extension, empty and oversized files. Verify upload rejection produces no committed asset; successful upload followed by failed attachment retains its asset under the existing retention policy. Do not manually remove stored files.
7. Preview private audio/video/PDF at /staff/{A}/courses/{C}/sections/{S}/lessons/{L}/items/{I}/media (learner equivalent: /learn/{A}/courses/{C}/sections/{S}/units/{L}/items/{I}/media; PDF download uses ?download=true); inspect authorized GET/HEAD, Range 206, invalid-range 416, seeking and isolated PDF viewing. No JWT or physical storage path should reach the browser. Actual playback/seek/PDF compatibility is unverified until this step is performed.
8. Open /staff/{A}/courses/{C}/readiness. Check course status, section/lesson ordering, all item types, draft/published quizzes and optional missing quizzes. Compare an empty course, empty lesson, legacy-only lesson and item-backed lesson: missing content must be informational. GET readiness must not mutate anything. Quiz configuration is not audited here.
9. Follow publication links. Submitting without explicit confirmation or with invalid antiforgery must not publish. Confirm lesson and course publication, then refresh readiness. Empty content must not introduce a new blocker; draft quizzes must not block course publishing. Publishing a course must not automatically publish its draft lessons or quizzes. Exercise lesson `actionName=unpublish` and course `actionName=archive` through the same confirmation routes; verify learner access is withdrawn, then republish disposable content. Use existing authorized quiz APIs for any quiz publication checks, separately from the web flow.
10. As an enrolled Student in A, navigate /learn/{A}/courses -> /learn/{A}/courses/{C} -> /learn/{A}/courses/{C}/sections/{S} -> /learn/{A}/courses/{C}/sections/{S}/units/{L}. Verify safe Text, normalized YouTube, MP3/MP4 playback/seek and isolated PDF through authenticated proxies. Check a legacy-only lesson. Draft course/lesson content and unenrolled course access must be denied, including direct item/media URLs. Staff-only forms/readiness must deny Students.
11. Attempt every new create/edit/readiness route with B's organization or course IDs, an unassigned Instructor course, mismatched course/section/lesson parents, and an old organization tab after switching selection. Expect 403/404 with no mutation or data disclosure, including direct quiz reads. Admin must remain A-scoped; SuperAdmin cannot use an unselected target. Revoke membership/ownership and verify subsequent actions recheck access.
12. Tamper/remove antiforgery on login, organization selection, logout, hierarchy forms, content forms/reorder/delete, assignment and publication. Expect rejection without mutation. Verify invalid login, no available organization, removed membership and expired session produce safe errors or authentication prompts without redirect loops. Sign out and confirm private page/media access requires authentication. Record browser/database results only after this separately authorized session; these automated checks do not establish live integration.
