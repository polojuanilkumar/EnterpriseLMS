# Lesson content items

Lesson remains the Unit; no Unit table, uploads, MP3, MP4, or SCORM is introduced.

## Routes and payloads

All routes use `/api/Lessons/{lessonId}/content-items`, Bearer authentication, and the verified `X-Organization-Id` header.

| Method | Suffix | Purpose |
| --- | --- | --- |
| GET | (none) | Ordered learner list or authorized staff preview |
| GET | `/{itemId}` | Learner detail or authorized staff preview |
| POST | (none) | Instructor/Admin/SuperAdmin create; 201 with Location |
| PUT | `/{itemId}` | Instructor/Admin/SuperAdmin full update |
| DELETE | `/{itemId}` | Instructor/Admin/SuperAdmin delete |
| PUT | `/reorder` | Instructor/Admin/SuperAdmin reorder |

Create/update JSON: `{"type":1,"title":"Reading","displayOrder":0,"isRequired":true,"text":"Plain text body","youTubeUrl":null}`.
YouTube uses type 2, null text, and `youTubeUrl`, for example `https://youtu.be/dQw4w9WgXcQ`.
Responses expose `youTubeVideoId`, never the supplied URL. Text is plain text and should be rendered as text, not raw HTML.
Reorder JSON: `{"itemIds":["first-item-guid","second-item-guid"]}`; every existing item must occur exactly once. Validation precedes mutations, with one transactional SaveChanges call.
Orders are nonnegative; ties sort by ID. Reorder assigns positions starting at zero. Create/update permit ties and gaps; delete does not renumber.

The stored Lesson -> Section -> Course chain determines organization and ownership. No organization/parent ID is accepted in the item body. Instructor preview and authoring require course ownership; Admin/SuperAdmin remain restricted to the selected organization. Students can read only published lessons in published courses with their own Active or Completed enrollment. Sections have no publication state. Unpublished resources, foreign organizations, and mismatched nested item IDs return 404; missing enrollment or ownership returns 403. Invalid payloads return 400. Existing middleware handles missing authentication/header/membership.

## Compatibility and publication

Existing Lesson.Content, VideoUrl, LessonType, endpoints, publication behavior, and data are preserved. No backfill or virtual content-item records are created. The new list response has `items` and `legacy`: with zero items it returns the old Content/VideoUrl in `legacy`; with one or more items, `legacy` is null. Consumers must render one mode only. Deleting the last item restores the fallback. Existing lesson detail remains readable through its existing checks and still exposes the original fields.

Legacy VideoUrl is not revalidated or represented as a validated YouTube item. Existing clients keep their current legacy rendering; new clients should treat legacy URLs as untrusted. Authors explicitly create new items when replacing legacy presentation. New items inherit Lesson/Course publication; there is no independent item publication state. Editing published content takes effect immediately. IsRequired is metadata in this batch and does not change existing lesson completion or progress rules.

Supported URLs require HTTPS and default port: youtube.com/www.youtube.com/m.youtube.com watch?v=, embed/, shorts/, live/; youtu.be/{id}; youtube-nocookie.com/www.youtube-nocookie.com embed/. IDs contain exactly 11 ASCII letters, digits, underscores, or hyphens. Reject other hosts/schemes, credentials, nondefault ports, whitespace/control characters, percent escapes, backslashes, duplicate v parameters, and malformed IDs. Extra query/fragment metadata is discarded. Nothing fetches URLs or verifies video availability/embedding rights.

## Migration and files

`20260929151614_AddLessonContentItems.cs` is additive: creates LessonContentItems with Id, LessonId, Type, Title (200), DisplayOrder, IsRequired, Text (nvarchar(max)), YouTubeVideoId (11). It adds a cascading Lesson FK, a nonunique (LessonId, DisplayOrder) index, and checks for nonnegative order and type/payload consistency. Down drops only this new table. Generated Designer and model snapshot accompany it. Migration generated but not applied; no database accessed.

Files for this batch:
- LMS.Domain/Enums/LessonContentType.cs
- LMS.Domain/Entities/LessonContentItem.cs
- LMS.Application/Interfaces/Lessons/ILessonContentItemRepository.cs
- LMS.Application/Features/LessonContent/LessonContentHandlers.cs
- LMS.Infrastructure/Repositories/LessonContentItemRepository.cs
- LMS.Infrastructure/Persistence/Configurations/LessonContentItemConfiguration.cs
- LMS.Infrastructure/Persistence/LMSDbContext.Organizations.cs (extends existing scope/filter/write guards)
- LMS.Infrastructure/Persistence/Migrations/20260929151614_AddLessonContentItems.cs
- LMS.Infrastructure/Persistence/Migrations/20260929151614_AddLessonContentItems.Designer.cs
- LMS.Infrastructure/Persistence/Migrations/LMSDbContextModelSnapshot.cs
- LMS.Api/Controllers/LessonContentItemsController.cs
- LMS.Api/Program.cs (two scoped registrations)
- LMS.Tests/LessonContentTests.cs
- LMS.Tests/TenantFilterTests.cs (adds an item to existing tenant test graphs)
- LMS.Tests/LMS.Tests.csproj (includes actual controller source)
- docs/lesson-content-items.md

Remaining limits: no live database/HTTP/Swagger validation; concurrent authoring is last-write-wins, including overlapping reorders; ties are intentionally allowed; video existence and embed permission are unknown; required flags do not enforce completion. Consumers must adopt the documented legacy/item mode and safe text rendering.

## Consolidated Swagger checklist (not executed)

After separately authorized migration/deployment, use two organizations, an owning and nonowning Instructor, Admin, and Students with Active/Completed/Cancelled/no enrollment:
- Confirm Bearer auth and X-Organization-Id are required; wrong/nonmember organization is denied.
- Read a legacy-only lesson: items empty, legacy Content/VideoUrl intact; verify old lesson detail also remains unchanged.
- Owner/Admin creates Text and YouTube items; verify 201/Location, required flag, normalized ID, and ascending order.
- Confirm legacy is null once an item exists; delete all items and confirm fallback reappears.
- Update title/type/body/required/order; verify incompatible payload, unknown type, blank title/text, and negative order return 400.
- Exercise each supported YouTube format; reject unrelated/spoofed hosts, credentials, unsafe schemes, malformed IDs, duplicate v, and nondefault ports.
- Reorder the full set; verify list/detail and contiguous positions. Reject duplicate, missing, foreign, or unknown IDs without partial changes.
- Verify nonowner Instructor cannot preview/create/update/delete/reorder, while owner/Admin can preview drafts.
- Substitute another lesson's item ID in GET/PUT/DELETE and another organization's lesson ID in every route; confirm denial and no mutation.
- Students can list/detail only with both publication flags and Active/Completed enrollment; deny drafts, Cancelled/no enrollment, and all authoring verbs.
- Confirm successful delete removes only the item, and old Lesson.Content/VideoUrl remain intact.
