# Private MP3 and MP4 lesson content

This batch extends the existing content-item feature. Text=1 and YouTube=2 remain unchanged; Mp3=3 and Mp4=4 require AssetId and prohibit Text/YouTubeUrl. Legacy Lesson.Content/VideoUrl and the legacy fallback strategy remain unchanged. No database connection, migration application, manual Swagger request, or browser playback test was performed.

## Exact routes

Every route requires Bearer authentication and a verified X-Organization-Id header. No token, organization, physical path, or storage filename is accepted in the URL query string.

| Method | Route | Behavior |
| --- | --- | --- |
| POST | /api/Lessons/{lessonId}/media-assets | Instructor/Admin/SuperAdmin multipart upload, field `file`; returns 201 with immutable asset metadata |
| GET, HEAD | /api/Lessons/{lessonId}/content-items/{itemId}/media | Authorized current item media; supports framework byte ranges |
| POST | /api/Lessons/{lessonId}/content-items | Existing create endpoint, now accepts `assetId` for types 3/4 |
| PUT | /api/Lessons/{lessonId}/content-items/{itemId} | Existing update; replace asset or change type |
| DELETE | /api/Lessons/{lessonId}/content-items/{itemId} | Deletes the item only; retains committed assets |
| GET | /api/Lessons/{lessonId}/content-items | Existing ordered list |
| GET | /api/Lessons/{lessonId}/content-items/{itemId} | Existing detail |
| PUT | /api/Lessons/{lessonId}/content-items/reorder | Existing complete-set reorder |

Upload first, then create/update an item with the returned asset ID:

```json
{"type":3,"title":"Audio lesson","displayOrder":0,"isRequired":true,"text":null,"youTubeUrl":null,"assetId":"returned-asset-guid"}
```

Use type 4 for MP4. An uploaded asset cannot be attached to a different lesson, organization, or type. No raw asset-download endpoint exists: retrieval must resolve an accessible content item first. Wrong lesson/item IDs, foreign organizations, and unavailable learner publications produce 404; missing ownership/enrollment produces 403. Bad file declarations/signatures/payloads produce 400; server request-size rejection may produce 413.

## Validation and limits

| Format | Extension | Declared MIME | Maximum bytes |
| --- | --- | --- | --- |
| MP3 | .mp3 (case insensitive) | audio/mpeg | 26,214,400 (25 MiB) |
| MP4 | .mp4 (case insensitive) | video/mp4 | 209,715,200 (200 MiB) |

Empty files are rejected. Declared length must equal stored length; copy enforces the limit even if the declaration lies. MIME parameters and application/octet-stream are not accepted. Multipart and request caps are 210,763,776 bytes (201 MiB), including 1 MiB transport overhead. IIS/reverse-proxy limits may need explicit deployment configuration. ASP.NET multipart binding can spool to its private temporary directory before the handler validates ownership/signatures.

MP3 validation requires two consecutive complete MPEG Layer III frames with valid version/bitrate/sample-rate fields. Optional ID3v2 metadata is skipped using its validated synchsafe size. MP4 validation checks box boundaries/lengths, a supported ftyp major or compatible brand (isom, iso2, mp41, mp42, avc1), and nonempty moov/mdat boxes. This is signature/container validation, not full decoding, malware scanning, transcoding, track inspection, or proof of codec compatibility. Some valid uncommon MP3/MP4 encodings are deliberately rejected.

## Privacy, access, playback

Storage is `LMS.Api/App_Data/PrivateMedia`, outside wwwroot, ignored by Git and explicitly excluded from publish content. Only generated GUID names are used, with .upload for staging and .media for retained bytes. Client filenames are used solely for extension validation and are not stored. Responses contain Id, LessonId, OrganizationId, Type, Size, CreatedAt and a derived canonical MediaType; no physical paths or public static URLs.

Both upload and retrieval reuse stored Lesson -> Section -> Course resolution. Selected organization must match the course. Instructor must own the course, including previews; Admin/SuperAdmin remain selected-organization scoped. Students cannot upload and can retrieve only published lessons in published courses with their own Active/Completed enrollment. Retrieval repeats these checks for each HTTP request, including ranges and HEAD. EF filters and write guards also scope assets and validate item/asset parent/type matching.

The response is a seekable FileStreamResult with range processing, canonical MIME, Cache-Control: private, no-store, and X-Content-Type-Options: nosniff. Automated execution tested 206 partial content, 416 unsatisfiable ranges, and HEAD without a response body. These are not browser playback tests.

LMS.Web is an MVC starter: no authentication flow, lesson UI, API proxy, or CORS integration exists. `LMS.Web/wwwroot/js/lesson-media.js` supplies an optional ES-module helper for a future authenticated same-origin API/reverse-proxy deployment. It fetches with Bearer and organization headers, checks MIME, and binds a private Blob URL to an audio/video element; its returned cleanup function revokes the URL. Tokens never enter media URLs. Native media tags cannot add these headers by themselves. The helper downloads the whole file before playback/local seeking and is not wired into the starter homepage. Server byte ranges are available to clients that can send the required headers; progressive native playback needs a future authenticated proxy/session design. Already downloaded media cannot be revoked retroactively.

## Lifecycle and replacement adapter

The storage abstraction exposes stage, publish, retain and private read. Staged upload disposal removes temporary/published bytes unless metadata persistence completed and the lease was retained. Validation errors, copy failures, cancellation, publish collisions and ordinary metadata-save failures are covered by cleanup tests. Published keys use create-only moves, never overwrite. Successful assets are immutable and retained even when an item is deleted, replaced, or changed back to Text/YouTube. This deliberately conservative retention policy protects assets that existing lesson-level learner history may rely on; there is no new playback-history feature and no asset deletion endpoint or committed-file deletion method. EF rejects updates/deletions of committed metadata; restrict FKs prevent referenced metadata deletion and cascade deletion of the associated lesson/organization ancestry. Lessons with retained assets therefore cannot be physically deleted through cascades; unpublish/archive instead. An unused successful upload is also retained.

No garbage collector is included. Process crashes, disk failures, cleanup failures, or an ambiguous database-commit result can leave orphaned files or inconsistent metadata because SQL and local storage cannot share a transaction. Production needs reconciliation, quotas, monitoring, backup/retention policy, and protected filesystem permissions. Never delete an orphan candidate solely because no current item references it; learner history must be considered.

A later Azure adapter can implement IPrivateMediaStorage/IStagedMedia using a private container and generated keys, preserve validation-before-publication and failed-upload cleanup, and return a seekable/range-capable read abstraction. Replace the DI registration without changing content-item/asset ownership rules. Do not expose public blobs or bypass per-request authorization with permanent URLs. Azure storage is not implemented here.

## Migration

`20260929160637_AddPrivateLessonMedia.cs` adds nullable LessonContentItems.AssetId and creates MediaAssets(Id, LessonId, OrganizationId, Type, Size, CreatedAt). Adds AssetId/LessonId/OrganizationId indexes and restrict foreign keys to MediaAssets, Lessons and Organizations. Replaces the content-type check constraint with the expanded four-type/payload rules and adds a media type/size constraint. No data copy, legacy column modification, or existing row deletion. Generated Designer and model snapshot accompany it. Down removes the new schema and restores the old type constraint; rollback after creating media items requires a separately reviewed data-retention plan. Migration generated only, not applied.

## Changed files in this batch

- .gitignore
- LMS.Api/LMS.Api.csproj
- LMS.Api/Program.cs
- LMS.Api/Controllers/LessonMediaController.cs
- LMS.Application/Features/LessonContent/LessonContentHandlers.cs
- LMS.Application/Features/LessonContent/MediaAssetHandlers.cs
- LMS.Application/Features/LessonContent/MediaValidation.cs
- LMS.Application/Interfaces/Lessons/IMediaAssetRepository.cs
- LMS.Application/Interfaces/Lessons/IPrivateMediaStorage.cs
- LMS.Domain/Entities/LessonContentItem.cs
- LMS.Domain/Entities/MediaAsset.cs
- LMS.Domain/Enums/LessonContentType.cs
- LMS.Infrastructure/Repositories/MediaAssetRepository.cs
- LMS.Infrastructure/Storage/LocalPrivateMediaStorage.cs
- LMS.Infrastructure/Persistence/Configurations/LessonContentItemConfiguration.cs
- LMS.Infrastructure/Persistence/Configurations/MediaAssetConfiguration.cs
- LMS.Infrastructure/Persistence/LMSDbContext.Organizations.cs
- LMS.Infrastructure/Persistence/Migrations/20260929160637_AddPrivateLessonMedia.cs
- LMS.Infrastructure/Persistence/Migrations/20260929160637_AddPrivateLessonMedia.Designer.cs
- LMS.Infrastructure/Persistence/Migrations/LMSDbContextModelSnapshot.cs
- LMS.Tests/LMS.Tests.csproj
- LMS.Tests/LessonContentTests.cs (shared existing fixture extended for assets)
- LMS.Tests/MediaAssetTests.cs
- LMS.Web/wwwroot/js/lesson-media.js
- docs/private-lesson-media.md

Validation: 359 tests passed (46 added cases), full solution build passed with zero warnings/errors, and EF reports no pending model changes. No database used; tests use mocks, local temporary files and SQL generation only.

## Consolidated Swagger checklist for later (not executed)

After separately authorized deployment/migration:
- Prepare two organizations, owner/nonowner Instructors, Admin, and Students with Active/Completed/Cancelled/no enrollment. Verify missing auth/header, invalid header, foreign organization and nonmember denial.
- Upload genuine MP3 and MP4 using `file`; verify 201 metadata, canonical type/size, generated IDs and no physical path/client filename/static URL disclosure.
- Reject empty, wrong extension/MIME, spoofed signatures, truncated frames/boxes, oversized files and mismatched payloads. Exercise exact size boundaries and host/proxy request limits.
- Attach each uploaded asset using type 3/4, verify list/detail/order/required flag. Reject unknown assets, cross-lesson/cross-organization assets and wrong asset types on both create and update.
- Verify nonowner Instructor and Student uploads fail, and Admin/owner previews can read unpublished content within their organization only.
- Retrieve as Active/Completed Student; deny Cancelled/no enrollment, unpublished course/lesson, foreign lesson and mismatched item ID. Verify item retrieval cannot be bypassed using raw asset IDs or storage names.
- Send authorized Range bytes=0-99 and an out-of-bounds range; expect 206/416, correct Content-Range and MIME. Check HEAD, no-store and nosniff. Recheck authorization after changing enrollment/publication.
- Simulate a failed/aborted upload and failed metadata save in an isolated environment; confirm no partial retained upload on ordinary handled failures.
- Replace a media asset, convert to Text/YouTube, delete its item and retry old item retrieval; committed files/metadata must remain retained and inaccessible through the removed item. Verify parent deletion cannot cascade through retained assets.
- Verify existing Text/YouTube validation and legacy Content/VideoUrl fallback remain intact.
- Separately wire an authenticated browser client and test actual MP3/MP4 playback, pause, seeking, supported codecs and Blob cleanup. Swagger/range-response success alone does not establish playback success.
