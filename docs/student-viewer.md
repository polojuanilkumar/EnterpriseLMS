# Student course, section and lesson viewer

The explicit lesson progress and course completion extension is documented in [student-progress.md](student-progress.md). The viewer-only batch record below remains as implementation history.

This extends the catalog/My Courses work described in [student-courses.md](student-courses.md).
The existing MVC viewer, API contracts, server sessions, private-media proxy and that
record were inspected before editing. Existing pending changes and branches are retained.
No packages, schema changes, database access, normal hosts, or completion mutations are involved.

## Routes and contracts

Angular lazily loads `/workspace/student/courses/:courseId`, with componentless nested
`sections/:sectionId` and `lessons/:lessonId` children. All three render inside the
existing shell. My Courses links only available Active/Completed enrollments.
Revalidation recognizes these destinations instead of redirecting to the overview.

All BFF paths below start with `/bff/student/courses/{courseId:guid}`. They require
the verified cookie/session, selected organization and current stored Student role.
Organization headers/query parameters supplied by the browser are never authority.
User identity and upstream JWT come exclusively from the protected server session.

| Method | Suffix | Response |
| --- | --- | --- |
| GET | empty or `/sections` | `{organizationId, course, sections}` |
| GET | `/sections/{sectionId:guid}` or `/sections/{sectionId:guid}/lessons` | `{organizationId, course, section, lessons}` |
| GET | `/sections/{sectionId:guid}/lessons/{lessonId:guid}` or the same plus `/content-items` | `{organizationId, courseId, sectionId, lesson, items, legacy}` |
| GET, HEAD | `/sections/{sectionId:guid}/lessons/{lessonId:guid}/items/{itemId:guid}/media` | Authorized stream; optional `?download=true` makes PDFs attachments |

Objects (camelCase JSON; GUIDs are strings):

```typescript
course = { id, title, description, categoryName, level, durationInMinutes };
section = { id, courseId, title, displayOrder };
lesson = { id, sectionId, title, description, isPublished, displayOrder };
item = { id, type, title, displayOrder, isRequired, text, youTubeVideoId };
legacy = null | { content, youTubeVideoId };
```

Sections, published lessons and items sort by displayOrder then ID. Item types are
1 plain text, 2 YouTube, 3 MP3, 4 MP4, 5 PDF. Text is projected only for type 1;
YouTube IDs only for type 2 after validation. Asset IDs, physical paths, legacy
VideoUrl, arbitrary media URLs, JWTs and progress are absent. Legacy content is
projected only when there are no items; supported legacy YouTube URLs are reduced
to validated IDs server-side. Unsupported legacy videos are not loaded.

The BFF checks current stored organization/user/Student access, published course,
owned Active/Completed enrollment, section-to-course, lesson-to-section, lesson
publication, and item-to-lesson relationships. Stored enrollment details must match
the verified user and course. Duplicate/mismatched parent collections fail closed.
The APIs retain authoritative tenant/enrollment/publication checks, including the
private-media API on every GET/HEAD/range. No authorization decision is cached.
Viewer calls only context, course metadata, enrollment, section, lesson and content
reads. It does not call MVC's quiz, assignment, learning panel or progress loaders.

All errors reuse sanitized BFF `{code,message,fieldErrors}`: 401 session expired,
403 access denied, 404 missing/unpublished/nested mismatch, 502 transport/contract
failure. Anonymous requests receive JSON rather than login redirects. Resources
from another organization cannot be distinguished through content disclosure.

## Media, cleanup and CSP

Audio/video use native accessible controls, playsinline for video, preload=none,
and fixed same-origin item BFF URLs. The proxy uses ResponseHeadersRead and streams
the upstream body; Angular never downloads a Blob or reads media into memory.
Range/If-Range, Accept-Ranges, Content-Range, Content-Length, ETag and Last-Modified
are forwarded by the existing infrastructure. 206/416 are retained; MIME types are
canonical audio/mpeg, video/mp4 and application/pdf, including 416 responses.
Successful mismatched upstream MIME is rejected. All responses retain no-store and
nosniff. Content-Disposition uses fixed item-ID filenames; upstream filenames
cannot inject headers. PDFs support inline and download dispositions.

The existing Web CSP remains unchanged: media-src 'self', frame-src 'self' plus
https://www.youtube-nocookie.com, object-src 'none', frame-ancestors 'self'. The
viewer styles live in the external stylesheet bundle to respect style-src 'self'.
PDF iframes use an empty sandbox, no-referrer and lazy loading. The PDF response
also retains `sandbox; default-src 'none'; frame-ancestors 'self'`. Native PDF
support may fail within this isolation; an authenticated download link is always
provided. No scripts or same-origin privileges are enabled for PDFs.

YouTube iframes use the fixed privacy-enhanced embed host and only exact 11-character
ASCII video IDs. Angular's ResourceUrl trust boundary is private and accepts only
URLs constructed from these IDs or canonical GUID-based item paths. No HTML trust
bypass exists; descriptions, lesson text and legacy content use interpolation.
YouTube's sandbox permits scripts, its own origin and presentation; it does not
permit top navigation or popups. No broad CSP/frame-policy relaxation was added.
Development proxy configuration does not establish production SPA hosting/CSP;
actual deployment headers and HTTPS/shared-cookie behavior still need verification.

Page state is scoped to the routed component and held only in memory. Route,
session, organization, logout, absolute expiry and uncertain/revalidating context
changes invalidate generations, clear content, pause media, remove src attributes,
call media.load() to release the player, remove iframe sources and discard trusted
URL references. Stale reads and HEAD probes cannot replace new-context data.
Response organization/parent identities are checked against the captured selection.
Native media errors hide HTTP status, so a HEAD probe checks access without reading
the body. 401/403/404 clears the whole lesson; other failures show a safe format or
transport message. PDF failures do not reliably emit events, so an explicit Check
access control is available. Access revocation is detected on the next server
request or session revalidation; bytes already delivered cannot be recalled.

Viewing, loading, seeking and playback never mark a lesson complete. No quiz,
assignment or progress mutation UI is included.

## Changed files

Frontend:
- `src/app/app.routes.ts`, `src/app/app.routes.spec.ts`
- `src/app/core/auth/workspace-routing.ts`
- `src/app/features/student-courses/student-courses.component.html`, `.component.spec.ts`
- `src/app/features/student-viewer/student-viewer.routes.ts`
- `src/app/features/student-viewer/student-viewer.models.ts`
- `src/app/features/student-viewer/student-viewer.service.ts`, `.service.spec.ts`
- `src/app/features/student-viewer/student-viewer.component.ts`, `.component.html`, `.component.spec.ts`
- `src/app/features/student-viewer/student-viewer.testing.ts`
- `src/styles.css`, `docs/student-courses.md`, `docs/student-viewer.md`

Backend:
- `LMS.Web/Services/BffStudentViewerApi.cs`
- `LMS.Web/Controllers/BffStudentViewerController.cs`
- `LMS.Web/Services/PrivateMediaProxy.cs`
- `LMS.Tests/Integration/BffStudentViewerTests.cs`
- `LMS.Tests/Integration/InProcessMvcTests.cs` (optional Range/If-Range test transport headers)
- `docs/student-courses.md`, `docs/student-viewer.md`

## Checks and remaining gaps

Automated results are recorded below after final verification. Backend tests use
fake repositories/transports and an in-process MVC server, without database access
or starting the normal API/Web programs. Angular tests assert DOM/source policies,
cleanup and request behavior; they do not prove real playback or PDF rendering.

Browser MP3/MP4 playback/seeking, YouTube playback, and native PDF rendering/download
remain **unverified**. Real API storage/SQL behavior, real uploaded codecs/files,
production SPA routing/CSP/cookies, and device/screen-reader behavior also remain
unverified. The existing Karma builder deprecation warning is not addressed.

## One later manual checklist

1. With real HTTPS/proxy hosting, sign in as a Student, select an organization and
   open both Active and Completed courses from My Courses. Follow nested deep links,
   refresh/back/forward, inspect order/breadcrumbs and mobile keyboard navigation.
2. Open all five types and legacy-only lessons. Confirm HTML appears as text and
   malicious legacy/YouTube URLs do not create frames or external requests.
3. Play/seek MP3 and MP4. Confirm cookies only, same-origin BFF URLs, Range/206 and
   416 behavior, canonical MIME, safe dispositions, no-store/nosniff and no full
   client-side download. Test a codec failure and the HEAD access check.
4. Check sandboxed PDF rendering across supported browsers and use its authenticated
   download fallback. Verify effective CSP and frame headers without weakening them.
5. During playback and delayed reads, change organization, logout, expire/revoke the
   session, cancel enrollment and unpublish resources. Confirm sources/content clear,
   stale responses stay discarded, 401/403/404 are safe, and later ranges are denied.
6. Try foreign course/section/lesson/item IDs and browser organization headers.
   Confirm no foreign metadata/media appears, no JWT/path leaks, and no quiz,
   assignment, progress mutation or lesson-completion request occurs.
7. Recheck catalog/enrollment/auth/shell and the existing MVC viewer. Confirm stored
   completion state has not changed from viewing or playback.

## Final automated verification (2026-10-15)

- `npm.cmd run build`: passed; separate lazy viewer routes/component chunks.
- `npm.cmd test -- --watch=false --browsers=ChromeHeadless --progress=false`:
  **77 passed**, zero failures. Existing Karma builder deprecation warning remains.
  The first run had one test-only missing-login-route mock failure; the mock was
  corrected and the complete suite passed.
- `dotnet build EnterpriseLMS.slnx --no-restore --no-incremental -m:1 -p:UseSharedCompilation=false`:
  passed, **0 warnings / 0 errors**.
- The following backend command passed **307 tests**, zero failures/skips:

```powershell
dotnet test LMS.Tests/LMS.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~BffStudentViewerTests|FullyQualifiedName~BffStudentTests|FullyQualifiedName~BffAuthTests|FullyQualifiedName~BffOrganizationTests|FullyQualifiedName~EnrollmentProgressWebTests|FullyQualifiedName~LearnerWebTests|FullyQualifiedName~InProcessMvcTests|FullyQualifiedName~OrganizationSelectionTests|FullyQualifiedName~LearnerWorkflowPolicyTests|FullyQualifiedName~EnrollmentCancellationTests|FullyQualifiedName~EnrollmentResponseMetadataTests|FullyQualifiedName~TenantHandlerTests|FullyQualifiedName~LessonContentTests|FullyQualifiedName~MediaAssetTests" --verbosity minimal
```

- `git diff --check` in both repositories and direct trailing-whitespace/EOF checks
  on new/edited viewer files passed. Git reports existing LF/CRLF conversion notices.
- Initial sandboxed build attempts could not resolve parent directories/write
  backend compiler output; authorized reruns completed successfully.
- Existing branches remain `chore/angular-dependency-upgrade` (Angular) and
  `feature/learning-flow-stability` (backend). Existing changes are preserved.
  No staging, commit, push, branch switch, packages, migrations or normal hosts.

The tests cover parent/cross-context denial, Active/Completed access, publication,
all five types/order, safe legacy/YouTube projection, authenticated media headers,
GET/HEAD/206/416, native-error HEAD recovery, source cleanup, expiry, stale responses,
and existing catalog/enrollment/auth/shell/MVC behavior. Media protocol assertions
and DOM tests do not constitute actual browser playback or PDF rendering tests.
