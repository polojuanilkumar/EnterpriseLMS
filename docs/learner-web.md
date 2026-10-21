# Authenticated learner web application

The current catalog, enrollment, lesson progress and course completion extension is documented
in [Learner enrollment and progress UI](learner-enrollment-progress-ui.md). That report supersedes
the initial viewer-only course listing/action limitations below; quiz routes remain documented
in [the quiz UI batch](quiz-ui-batch.md).

LMS.Web now signs learners into LMS.Api server-to-server, offers verified organization selection, and renders Course -> Section -> Unit content. The existing API/storage/Organization/stability changes and completion policy remain intact. No API/EF/domain/migration changes in this batch. Earlier PDF/media batch reports describe the previous web starter state; this report supersedes their web-integration limitations.

## Configuration and startup prerequisites

Development API origin is LmsApi:BaseUrl = https://localhost:7196/ in LMS.Web/appsettings.Development.json. Production must configure LmsApi__BaseUrl to a trusted HTTPS origin (no credentials, path, query, or fragment). API requests never use a client-supplied host. Redirect following and automatic cookie handling are disabled; normal TLS certificate verification remains enabled. Trust development HTTPS certificates. Use LMS.Web's HTTPS profile at https://localhost:7207; Secure cookies will not work over plain HTTP. The API must be reachable and already have the required migrations/data. Neither app was started during this work; starting LMS.Api invokes its existing database role seeder.

The initial session store is protected server memory. Restarting LMS.Web signs users out. For multi-instance deployment, replace it with a protected distributed store with atomic revocation/rotation and shared Data Protection keys, or require instance affinity until then. Login rate limiting is 10 POST attempts per remote IP per minute per instance (429 on excess); configure trusted proxies deliberately before using forwarded client IPs. No TLS certificate-validation bypass, CORS relaxation, API token refresh, or persistent remember-me is introduced.

## Security design

- API JWTs live only in Data-Protection-encrypted server cache entries. Browser cookie __Host-LmsLearner contains a protected ticket with an opaque random 256-bit session reference and user ID, never the API token. Cookie is Secure, HttpOnly, SameSite=Strict, Path=/, nonpersistent, and nonsliding. Absolute lifetime is bounded by API token expiry. Every authenticated request checks the server entry. Login and organization switching rotate references; logout revokes them. No passwords are stored or redisplayed.
- Login, organization selection and logout are POST forms with global antiforgery validation, including login CSRF protection. Redirects are fixed/local; no user returnUrl is accepted. Anonymous page navigation redirects to sign-in; anonymous media returns 401 without redirecting a media element to HTML.
- Selection first checks GET api/organizations/me, then the Student-only GET api/Enrollments/my-courses/progress with the candidate organization header. Only after API membership/role success is selected organization stored in a new session. This viewer is Student-facing: staff-only memberships cannot use it as a preview UI. No roles from the login JWT are trusted for organization selection.
- Every learner route includes organization ID, which must match server selection. All upstream organization headers come from that checked session context, never an incoming browser header. Old tabs from another selected organization are denied. API independently revalidates stored membership for each scoped call.
- Course listing intersects published courses with Active/Completed enrollments. Course access uses the API Student-only progress read, enforcing publication/enrollment before fetching sections. Section, lesson and content parent IDs are checked through the stored API responses before rendering or proxying. There is no generic URL proxy or raw asset-ID endpoint.
- All dynamic titles, descriptions, Text and legacy Content are Razor encoded. Text displays as plain text with preserved whitespace, never raw HTML. Only exactly 11-character normalized YouTube IDs produce fixed youtube-nocookie.com embeds. Legacy YouTube URLs pass a strict local allowlist/parser; other legacy VideoUrl values display as encoded references and are not fetched, linked or embedded. New items suppress legacy fallback, so content is not duplicated.
- The main pages have a restrictive CSP, same-origin form action, no-store, nosniff and no-referrer. PDFs are isolated by iframe sandbox and by a sandbox CSP on the file response (no allow-same-origin or script permission). A generated-name attachment download fallback is offered if a browser refuses isolated PDF rendering. No PDF sanitization is claimed.
- Cookie-authenticated proxy requests recheck the complete route chain and current API learner access before opening media. JWT is added server-side only. HttpClient logging is removed for this client; no request-body/token logging is added. Upstream errors and raw bodies are never forwarded for 401/403/404; 401 revokes the web session, other denials preserve it. Transport/unexpected upstream errors produce generic 502 responses.
- Optional lesson-media.js/lesson-pdf.js helpers now accept only route IDs and use the authenticated web endpoint. The rendered viewer needs no JavaScript for media authentication and does not use the former token/Blob approach.

## Exact web routes

All IDs are GUIDs. Let B = /learn/{organizationId}/courses and U = /learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | / | Redirect to organization chooser (authentication required) |
| GET, POST | /account/login | Sign-in form; POST fields Email, Password and antiforgery token |
| GET | /account/organizations | Organizations returned by the API for this account |
| POST | /account/organizations | Validate/select organizationId with antiforgery token; rotate session |
| POST | /account/logout | Revoke server session and clear cookie; antiforgery required |
| GET | /learn/{organizationId}/courses | Published enrolled courses |
| GET | /learn/{organizationId}/courses/{courseId} | Ordered sections |
| GET | /learn/{organizationId}/courses/{courseId}/sections/{sectionId} | Ordered published units |
| GET | /learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId} | Ordered content and legacy fallback |
| GET, HEAD | /learn/{organizationId}/courses/{courseId}/sections/{sectionId}/units/{lessonId}/items/{itemId}/media | Private audio/video/PDF proxy |
| GET, HEAD | same media route with ?download=true | PDF attachment fallback; no token in URL |

No browser Authorization or X-Organization-Id header is needed: the HttpOnly cookie identifies the server session. Route organization is compared to the stored selection; API requests send Authorization: Bearer <server token> and X-Organization-Id: <verified selection>. The browser never receives those outgoing credentials.

## Proxy and presentation behavior

Audio/video elements use ordinary same-origin src URLs with controls and preload=none. PDF iframe src uses the same proxy. Range and valid If-Range are forwarded to the API; HEAD remains HEAD for the media request (parent authorization uses read-only API GETs). Response status, Content-Length, Content-Range, Accept-Ranges, ETag and Last-Modified are preserved when supplied. Successful MIME is checked against the content type; HTML or unexpected content cannot be served as media. Set-Cookie, Location, authentication headers and arbitrary upstream headers are not forwarded. Successful bodies stream with ResponseHeadersRead and cancellation/disposal, without buffering the entire asset. 416 is preserved. The proxy sets private,no-store and nosniff; PDFs use inline/attachment with a generated filename.

Type 1 = safe Text; 2 = normalized YouTube; 3 = MP3; 4 = MP4; 5 = PDF. IsRequired is displayed as metadata only. No start/complete/progress/enrollment mutation is performed by viewing, playing, switching organizations or downloading. Already delivered bytes and in-flight authorized requests cannot be retroactively revoked on logout or membership changes; subsequent requests reauthorize.

## Changed files

- LMS.Web/Program.cs
- LMS.Web/appsettings.Development.json
- LMS.Web/Controllers/HomeController.cs
- LMS.Web/Controllers/AccountController.cs
- LMS.Web/Controllers/LearnController.cs
- LMS.Web/Models/LearnerModels.cs
- LMS.Web/Services/LearnerSessions.cs
- LMS.Web/Services/LearnerApi.cs
- LMS.Web/Services/LearnerSecurity.cs
- LMS.Web/Services/LearnerWebRegistration.cs
- LMS.Web/Services/SafeContent.cs
- LMS.Web/Views/Account/Login.cshtml
- LMS.Web/Views/Account/Organizations.cshtml
- LMS.Web/Views/Learn/Courses.cshtml
- LMS.Web/Views/Learn/Sections.cshtml
- LMS.Web/Views/Learn/Lessons.cshtml
- LMS.Web/Views/Learn/Unit.cshtml
- LMS.Web/Views/Shared/_Layout.cshtml
- LMS.Web/wwwroot/css/site.css
- LMS.Web/wwwroot/js/lesson-media.js
- LMS.Web/wwwroot/js/lesson-pdf.js
- LMS.Tests/LMS.Tests.csproj (references compiled web project without starting it)
- LMS.Tests/LearnerWebTests.cs
- docs/learner-web.md

## Automated verification and limits

438 tests passed, including 38 new web cases and all existing API/content/organization/media tests. Tests use a fake upstream HttpMessageHandler, real cookie and antiforgery services, protected server sessions, compiled Razor rendering, and direct controller/middleware execution. No network/database/API-host startup is used. Full solution build: zero warnings/errors. git diff --check passed. JavaScript helpers are optional; main views render media directly.

Manual playback and PDF rendering remain unverified. Browser PDF sandbox behavior varies, so the download fallback is intentional. Existing legacy HTML is shown as encoded text; non-YouTube legacy URLs remain readable references rather than automatic network loads. No shared-store/high-availability session deployment is configured. Multiple API authorization calls per media request favor correctness over throughput and may need measured optimization later. Current API checks are performed sequentially rather than atomically across all calls; the final media endpoint independently reauthorizes. Local asset retention/storage and signature-only PDF limitations from prior batches still apply.

## One end-to-end browser checklist for later (not executed)

Use disposable enrolled learner accounts in organizations A/B, a nonmember account, Active/Completed/Cancelled enrollments, published/unpublished courses and units, legacy-only lessons, and existing disposable Text/YouTube/MP3/MP4/PDF items. Prepare these only under separate authorization; no authoring UI is added here.

1. Separately start configured HTTPS API/web services. Open https://localhost:7207/account/login. Confirm unauthenticated learner-page navigation redirects to this route and a guessed nested /media URL returns 401 without an HTML sign-in redirect. Confirm missing/invalid antiforgery on POST login/selection/logout returns 400; repeated login attempts reach 429 after the configured limit.
2. Submit invalid credentials through POST /account/login: expect 401, generic error and blank password field. Submit valid credentials: expect redirect to /account/organizations. Inspect cookie flags (Secure, HttpOnly, Strict) without copying credentials. Confirm HTML, JS, browser storage, URLs and browser network requests contain no API JWT. Browser sees only the web login POST, never a direct API login response.
3. On /account/organizations choose A via POST with the generated antiforgery token. Expect redirect to /learn/{A}/courses. Submit a foreign/nonmember organization ID: expect 403 with no session/selection change. Try a staff-only organization membership: 403; learner selection requires the API Student role. Revoked/inactive membership must be denied by the API.
4. Navigate /learn/{A}/courses -> /learn/{A}/courses/{C} -> /learn/{A}/courses/{C}/sections/{S} -> /learn/{A}/courses/{C}/sections/{S}/units/{L}. Expect 200 at each permitted step, only published enrolled courses/units and ordered sections/items. Completed enrollment may read; Cancelled/no enrollment cannot open the course/unit. Verify empty-course/section messages.
5. Open a legacy-only unit: Content appears safely as plain text and a supported legacy YouTube URL embeds. Unknown/javascript/foreign legacy URLs remain encoded references without automatic fetch. Open a unit with new items: legacy is not duplicated. Test disposable script-like Text/title values: visible encoded text, no script execution. Invalid YouTube ID cannot create an iframe; valid IDs use only youtube-nocookie.com.
6. Play MP3 and MP4 using native controls. Inspect only same-origin web /items/{I}/media requests with cookie authentication; there must be no API bearer token. Test pause, seeking, duration and resume; record actual codec/browser results. Use authorized Range requests against the same route: expect 206 with correct Content-Range or 416 beyond EOF. HEAD must have no body. Compare If-Range/ETag/Last-Modified where the API supplies validators.
7. Open PDF: verify sandboxed frame and inline application/pdf response. Check page navigation/zoom in the target browser. If isolated rendering is blocked, use /items/{I}/media?download=true and expect generated-name attachment. Confirm the PDF cannot access the application DOM/cookies. Record actual PDF behavior; automated response checks do not prove rendering.
8. Tamper with organization/course/section/unit/item IDs, including an asset ID substituted for item ID. Expect 403 for a route organization not selected and 404 for missing/mismatched stored parents/items; no private bytes or raw API error body. Make publication/enrollment changes only through separately authorized setup, then repeat page/media/range requests: expect API-derived 403/404 denials. Spoofing a browser X-Organization-Id header must not change server selection.
9. Switch to B through the organization form, then revisit an old A media/page URL: expect 403 under the new selection. Verify B's courses and media remain isolated. Logout using POST /account/logout: session is revoked and cookie cleared. Reuse of the old cookie must fail. Also check token/session expiry and a web restart require fresh sign-in; API 401 produces a generic session-expired response.
10. Confirm web browsing/playback/download caused no lesson completion/progress/enrollment changes. Check application logs contain no passwords/API JWTs, and private responses have no-store/nosniff. End the session by signing out; retain existing private assets under the established retention policy rather than manually deleting them.
