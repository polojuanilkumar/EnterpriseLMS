using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.LessonContent;
using LMS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Api.Controllers;

[ApiController]
[OrganizationScope]
[Authorize(Roles = "Student,Instructor,Admin,SuperAdmin")]
[Route("api/Lessons/{lessonId:guid}")]
public sealed class LessonMediaController(MediaAssetHandlers handlers) : ControllerBase
{
    [HttpPost("media-assets")]
    [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MediaValidation.Mp4Limit + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaValidation.Mp4Limit + 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<MediaAsset>>> Upload(Guid lessonId, [FromForm] MediaUploadRequest request, CancellationToken token)
    {
        if (request.File is null) return BadRequest(ApiResponse<MediaAsset>.Fail("A file is required."));
        await using var source = request.File.OpenReadStream();
        var result = await handlers.UploadAsync(lessonId,request.File.FileName,request.File.ContentType,request.File.Length,source,token);
        return StatusCode(StatusCodes.Status201Created,ApiResponse<MediaAsset>.Ok(result));
    }
    [HttpGet("content-items/{itemId:guid}/media")]
    [HttpHead("content-items/{itemId:guid}/media")]
    public async Task<IActionResult> Media(Guid lessonId, Guid itemId, CancellationToken token)
    {
        var media = await handlers.OpenAsync(lessonId,itemId,token);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (media.MediaType == "application/pdf")
            Response.Headers.ContentDisposition = $"inline; filename=\"presentation-{itemId:N}.pdf\"";
        return File(media.Stream,media.MediaType,enableRangeProcessing:true);
    }
}
public sealed class MediaUploadRequest { public IFormFile File { get; set; } = null!; }
