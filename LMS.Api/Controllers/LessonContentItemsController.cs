using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.LessonContent;
using LMS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Api.Controllers;

[ApiController]
[Authorize(Roles = "Student,Instructor,Admin,SuperAdmin")]
[OrganizationScope]
[Route("api/Lessons/{lessonId:guid}/content-items")]
public sealed class LessonContentItemsController(LessonContentHandlers handlers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<LessonContentResponse>>> List(Guid lessonId, CancellationToken token)
        => Ok(ApiResponse<LessonContentResponse>.Ok(await handlers.ListAsync(lessonId, token)));
    [HttpGet("{itemId:guid}")]
    public async Task<ActionResult<ApiResponse<LessonContentItem>>> Get(Guid lessonId, Guid itemId, CancellationToken token)
        => Ok(ApiResponse<LessonContentItem>.Ok(await handlers.GetAsync(lessonId, itemId, token)));
    [HttpPost]
    [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
    public async Task<ActionResult<ApiResponse<LessonContentItem>>> Create(Guid lessonId, ContentItemRequest request, CancellationToken token)
    {
        var item = await handlers.CreateAsync(lessonId, request, token);
        return CreatedAtAction(nameof(Get), new { lessonId, itemId = item.Id }, ApiResponse<LessonContentItem>.Ok(item));
    }
    [HttpPut("{itemId:guid}")]
    [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
    public async Task<ActionResult<ApiResponse<LessonContentItem>>> Update(Guid lessonId, Guid itemId, ContentItemRequest request, CancellationToken token)
        => Ok(ApiResponse<LessonContentItem>.Ok(await handlers.UpdateAsync(lessonId, itemId, request, token)));
    [HttpDelete("{itemId:guid}")]
    [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid lessonId, Guid itemId, CancellationToken token)
    {
        await handlers.DeleteAsync(lessonId, itemId, token);
        return Ok(ApiResponse<object>.Ok(new { }, "Content item deleted."));
    }
    [HttpPut("reorder")]
    [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
    public async Task<IActionResult> Reorder(Guid lessonId, ReorderContentRequest request, CancellationToken token)
    {
        await handlers.ReorderAsync(lessonId, request, token);
        return Ok(ApiResponse<object>.Ok(new { }, "Content items reordered."));
    }
}
