# Private PDF presentations under Lesson/Unit

PDF is LessonContentType.Pdf = 5. It uses the existing MediaAsset metadata, private storage interface, upload pipeline and LessonContentItem.AssetId. No Unit table, completion-policy change, PPTX support or conversion is introduced. MP3/MP4 limits, Text/YouTube behavior and legacy Lesson.Content/VideoUrl remain unchanged.

## Routes and authorization

All routes require Authorization: Bearer <token> and X-Organization-Id: <selected organization GUID>.

| Method | Exact route | Result |
| --- | --- | --- |
| POST | /api/Lessons/{lessonId}/media-assets | Multipart field `file`; PDF upload returns 201 with asset metadata |
| POST | /api/Lessons/{lessonId}/content-items | Attach uploaded asset using type 5; returns 201 |
| PUT | /api/Lessons/{lessonId}/content-items/{itemId} | Replace attachment or update metadata; returns 200 |
| GET | /api/Lessons/{lessonId}/content-items | Ordered items; returns 200 |
| GET | /api/Lessons/{lessonId}/content-items/{itemId} | Item detail; returns 200 |
| GET, HEAD | /api/Lessons/{lessonId}/content-items/{itemId}/media | Authorized inline PDF; 200, or 206/416 for byte ranges |
| PUT | /api/Lessons/{lessonId}/content-items/reorder | Existing full-set ordering; returns 200 |
| DELETE | /api/Lessons/{lessonId}/content-items/{itemId} | Deletes the item only; returns 200 |

The stored Lesson -> Section -> Course chain determines ownership and organization. Instructor requires course ownership. Organization Admin/SuperAdmin author within the selected organization. Student must have Active or Completed enrollment, and both Lesson and Course must be published. Each delivery request, including HEAD and Range, repeats access checks. There is no raw asset-ID download endpoint; supplying an asset ID in place of item ID cannot retrieve a file. Item and asset must agree on lesson, selected organization and type.

Attachment JSON (Content-Type: application/json):

```json
{
  "type": 5,
  "title": "Presentation",
  "displayOrder": 0,
  "isRequired": true,
  "text": null,
  "youTubeUrl": null,
  "assetId": "uploaded-asset-guid"
}
```

## File contract and delivery

PDF requires .pdf (case insensitive), declared MIME application/pdf, nonempty content, and a maximum of 26,214,400 bytes (25 MiB). Actual copied length must equal the declaration; private storage enforces the byte limit while copying. PDF headers must begin at byte zero with %PDF-1.0 through %PDF-1.7 or %PDF-2.0 followed by CR/LF. The final 1 KiB must end in %%EOF followed only by PDF whitespace. This is signature validation, not structural parsing, sanitization, malware scanning or a guarantee of rendering. Password-protected PDFs may still require a password in the viewer. Files with misleading headers/EOF can pass signature checks even if structurally invalid; active content and embedded attachments are not removed.

MP3 remains 25 MiB; MP4 remains 200 MiB. The shared endpoint request/multipart cap stays 201 MiB including overhead; PDF's 25 MiB cap is enforced independently. IIS/proxy and ASP.NET temporary-buffer capacity remain deployment considerations.

Files remain in LMS.Api/App_Data/PrivateMedia outside wwwroot, Git-ignored and excluded from publish content. Generated GUID names are used; client filenames are neither storage keys nor response filenames. Existing failed-upload disposal removes uncommitted staged/published bytes. Successfully committed assets remain immutable and retained after item replacement/deletion, protecting existing history. There is no asset-delete API. No physical path or public static URL is exposed.

PDF delivery sets application/pdf, Content-Disposition: inline; filename="presentation-<item-guid>.pdf", Cache-Control: private, no-store, and X-Content-Type-Options: nosniff. It returns the existing seekable FileStreamResult with range processing. No download filename is passed to FileStreamResult, so the explicit inline disposition is preserved.

LMS.Web remains an MVC starter without an authenticated lesson page/API proxy. The new optional ES module wwwroot/js/lesson-pdf.js fetches the item media route using Bearer and organization headers and attaches a private Blob URL to an iframe, returning a cleanup callback that revokes it. It requires a same-origin authenticated API/proxy integration; it is not wired into the starter homepage. Native iframe/PDF viewers cannot supply those headers on their own. The helper downloads the full PDF before viewing; header-capable clients can use the server's range support. Browser PDF support differs and may fall back to download. No manual browser test was run or PDF rendering verified.

## Migration

20260929175839_AddPdfPresentations expands CK_MediaAssets_TypeSize to allow type 5 with size 1..26214400 and CK_LessonContentItems_Type to allow type 5 only with AssetId and without Text/YouTubeVideoId. It replaces those two check constraints, preserving all previously allowed types. No table/column changes, data backfill, row deletion or original migration edits. Designer and snapshot generated with EF tooling; migration NOT applied. Down restores the prior constraints; existing PDF records would require a separately reviewed retention/rollback plan before downgrade.

## Next increment: PPTX decision

PPTX is rejected in this batch. Two distinct future choices:

- Original-file download: validate/store private PPTX and deliver as an authorized attachment for local PowerPoint-compatible software. This is smaller scope but provides no in-browser slide viewing.
- Server-side conversion: retain the original, queue conversion in an isolated worker, and store a derived PDF under the same organization/lesson and retention rules. This enables reuse of PDF viewing but requires converter choice/licensing, resource/time limits, failure/retry states, fidelity testing, and source/derivative lifecycle management.

Choose based on whether browser slide viewing is required; do not label original-file download as slide viewing. Neither option is implemented here.

## Changed files in this batch

- LMS.Domain/Enums/LessonContentType.cs
- LMS.Domain/Entities/MediaAsset.cs
- LMS.Application/Features/LessonContent/MediaValidation.cs
- LMS.Api/Controllers/LessonMediaController.cs
- LMS.Infrastructure/Persistence/Configurations/MediaAssetConfiguration.cs
- LMS.Infrastructure/Persistence/Configurations/LessonContentItemConfiguration.cs
- LMS.Infrastructure/Persistence/Migrations/20260929175839_AddPdfPresentations.cs
- LMS.Infrastructure/Persistence/Migrations/20260929175839_AddPdfPresentations.Designer.cs
- LMS.Infrastructure/Persistence/Migrations/LMSDbContextModelSnapshot.cs
- LMS.Tests/LessonContentTests.cs (shared fixture adds optional SuperAdmin context)
- LMS.Tests/MediaAssetTests.cs (PDF included in payload exclusivity checks)
- LMS.Tests/PdfPresentationTests.cs
- LMS.Web/wwwroot/js/lesson-pdf.js
- docs/pdf-presentations.md

## Verification and remaining risks

400 tests passed, including 41 added PDF cases and existing-media regression coverage. Full solution build: zero warnings/errors. Tests use mocks, local generated temporary files and framework response execution, without opening a database. PDF response tests cover full GET, HEAD, 206 byte-range data, 416 and private/inline headers. Existing tests cover MP3/MP4, Text/YouTube, organization rules and legacy compatibility.

Remaining risks: signature-only PDF validation and viewer-dependent rendering/security; no wired web authentication/viewer; full Blob download memory use; inherited local-disk retention growth and file/database crash-consistency limits. No completion events or progress writes are added. No database, live API, Swagger or browser verification was performed.

## One ordered manual checklist for later (not executed)

Use disposable lessons/items/files. Successful POST/PUT/PATCH/DELETE requests change data; GET/HEAD are read-only. Successful uploaded assets are retained even after item deletion. Run only after separately authorized deployment and migration application.

1. Prepare selected organization A, a published disposable Lesson L in a published course, owning/nonowning Instructors, Organization Admin/SuperAdmin, and Students with Active/Completed/Cancelled/no enrollment. Use Authorization: Bearer <actor-token> and X-Organization-Id: <A> on every request. Missing authentication should return 401; missing/malformed organization header 400; active organization without membership 403.
2. As owner, POST /api/Lessons/{L}/media-assets with multipart field file, filename disposable.pdf, part Content-Type application/pdf, and a real small PDF. Let the browser generate the boundary. Expect 201, type 5, canonical MIME, matching size, generated ID, and no physical/client path. Save asset ID P. Repeat with Organization Admin and SuperAdmin in A; expect 201.
3. Use the same upload route with empty PDF, PDF over 25 MiB, wrong MIME, wrong extension, HTML renamed .pdf, bad version/header, missing EOF, and PPTX. Expect 400 (or host 413 for transport limits), no committed metadata, and no leftover .upload/.media from handled failed uploads. Record a storage inventory before/after; Swagger has no asset-list endpoint, so it cannot independently prove metadata-row absence. Separately authorized failure injection can check save-failure cleanup; do not alter the live database to force it.
4. POST /api/Lessons/{L}/content-items with the attachment JSON above and P. Expect 201; save item I. GET /api/Lessons/{L}/content-items and /api/Lessons/{L}/content-items/{I}: 200, type 5, AssetId P. PUT the item with updated title/order/required and the same full payload: 200. Null/unknown AssetId, a video asset paired with type 5, or simultaneous Text/YouTubeUrl must fail 400. PUT /api/Lessons/{L}/content-items/reorder with {"itemIds":["every-current-item-id-in-desired-order"]}: 200.
5. As enrolled Student, GET /api/Lessons/{L}/content-items/{I}/media: 200 with application/pdf, inline generated filename, no-store and nosniff. HEAD the same route: 200, no body. GET with Range: bytes=0-7: 206, matching Content-Range and %PDF header bytes. Range beyond the file size: 416. Completed enrollment may read; Cancelled/no enrollment must receive 403.
6. In a separately integrated authenticated browser client, fetch that route with both headers and display the Blob using an iframe/PDF viewer. Check pages, zoom, navigation and cleanup/revocation. Verify server range requests separately because Blob viewing uses already downloaded bytes. Record actual rendering results; API status alone does not prove browser viewing.
7. As owner PATCH /api/Lessons/{L}/unpublish with no body: 200. Student list/detail/media/HEAD/Range must return 404 without PDF bytes; owner preview may return 200. PATCH /api/Lessons/{L}/publish: 200, restoring student reads. Also check a lesson in an unpublished course returns 404 to Student. Do not change production publication state for this check.
8. Nonowner Instructor upload/attach/preview must return 403; Student upload/attach must return 403. Select another organization B and attempt A's lesson/media as a legitimate member of B: 404. Nonmember B selection: 403. Substitute a different lesson's item ID: 404. Substitute raw asset ID P for item ID I: 404. Foreign-organization/foreign-lesson asset attachment must fail without mutation.
9. Regression: create/read/update Text (type 1) and YouTube (type 2); upload/attach MP3 (type 3) and MP4 (type 4) through the same routes. Expect prior 201/200 behavior and existing audio/video MIME/ranges, without PDF disposition. Read a legacy-only lesson: empty items with intact legacy Content/VideoUrl. Confirm viewing PDF has not completed lessons or changed progress.
10. Replace PDF attachment or change I to Text, then DELETE /api/Lessons/{L}/content-items/{I}: 200. Old item delivery must return 404. Committed PDF assets remain retained; do not manually delete them or their parent lesson as cleanup. Removing all disposable items restores the legacy fallback.
