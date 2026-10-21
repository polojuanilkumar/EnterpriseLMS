using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.Assignments;
using LMS.Application.Features.LessonContent;
using LMS.Application.Features.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Api.Controllers;

[ApiController,OrganizationScope,Authorize(Roles="Student,Instructor,Admin,SuperAdmin")]
[Route("api/Lessons/{lessonId:guid}/assignments")]
public sealed class AssignmentsController(AssignmentHandlers handlers,OrganizationContext org):ControllerBase
{
    [HttpGet("context")] public async Task<IActionResult> Context(Guid lessonId,CancellationToken token)=>Ok(ApiResponse<AssignmentContext>.Ok(await handlers.Context(lessonId,org.CanManageContent,token)));
    [HttpGet("~/api/Assignments/submissions/me"),Authorize(Roles="Student")]
    public async Task<IActionResult> History(CancellationToken token)=>Ok(ApiResponse<AssignmentHistory[]>.Ok(await handlers.History(token)));
    [HttpGet] public async Task<IActionResult> List(Guid lessonId,CancellationToken token)=>Ok(ApiResponse<AssignmentResponse[]>.Ok(await handlers.List(lessonId,org.CanManageContent,token)));
    [HttpGet("{assignmentId:guid}")] public async Task<IActionResult> Detail(Guid lessonId,Guid assignmentId,CancellationToken token)=>Ok(ApiResponse<AssignmentResponse>.Ok(await handlers.Detail(lessonId,assignmentId,org.CanManageContent,token)));
    [HttpPost,Authorize(Roles="Instructor,Admin,SuperAdmin")]
    public async Task<IActionResult> Create(Guid lessonId,AssignmentRequest request,CancellationToken token)=>StatusCode(201,ApiResponse<AssignmentResponse>.Ok(await handlers.Create(lessonId,request,token)));
    [HttpPut("{assignmentId:guid}"),Authorize(Roles="Instructor,Admin,SuperAdmin")]
    public async Task<IActionResult> Edit(Guid lessonId,Guid assignmentId,AssignmentRequest request,CancellationToken token)=>Ok(ApiResponse<AssignmentResponse>.Ok(await handlers.Edit(lessonId,assignmentId,request,token)));
    [HttpPatch("{assignmentId:guid}/publication"),Authorize(Roles="Instructor,Admin,SuperAdmin")]
    public async Task<IActionResult> Publish(Guid lessonId,Guid assignmentId,PublicationRequest request,CancellationToken token)=>Ok(ApiResponse<AssignmentResponse>.Ok(await handlers.Publish(lessonId,assignmentId,request,token)));
    [HttpGet("{assignmentId:guid}/submissions/page"),Authorize(Roles="Instructor,Admin,SuperAdmin")]
    public async Task<IActionResult> SubmissionPage(Guid lessonId,Guid assignmentId,int page=1,int pageSize=20,CancellationToken token=default)
        =>Ok(ApiResponse<SubmissionPage>.Ok(await handlers.StaffSubmissionPage(lessonId,assignmentId,page,pageSize,token)));
    [HttpGet("{assignmentId:guid}/submissions"),Authorize(Roles="Instructor,Admin,SuperAdmin")]
    public async Task<IActionResult> Submissions(Guid lessonId,Guid assignmentId,CancellationToken token)=>Ok(ApiResponse<SubmissionResponse[]>.Ok(await handlers.Submissions(lessonId,assignmentId,true,token)));
    [HttpGet("{assignmentId:guid}/submissions/me"),Authorize(Roles="Student")]
    public async Task<IActionResult> Mine(Guid lessonId,Guid assignmentId,CancellationToken token)=>Ok(ApiResponse<SubmissionResponse[]>.Ok(await handlers.Submissions(lessonId,assignmentId,false,token)));
    [HttpGet("{assignmentId:guid}/submissions/{submissionId:guid}")]
    public async Task<IActionResult> Review(Guid lessonId,Guid assignmentId,Guid submissionId,CancellationToken token)=>Ok(ApiResponse<SubmissionResponse>.Ok(await handlers.Review(lessonId,assignmentId,submissionId,org.CanManageContent,token)));
    [HttpPost("{assignmentId:guid}/submissions"),Authorize(Roles="Student"),Consumes("multipart/form-data")]
    [RequestSizeLimit(MediaValidation.PdfLimit+2*1024*1024),RequestFormLimits(MultipartBodyLengthLimit=MediaValidation.PdfLimit+2*1024*1024)]
    public async Task<IActionResult> Submit(Guid lessonId,Guid assignmentId,[FromForm]SubmissionUpload request,CancellationToken token)
    {
        if(Request.Form.Files.Count>1)throw new ArgumentException("Only one PDF attachment is accepted.");
        await using var source=request.File?.OpenReadStream();
        return StatusCode(201,ApiResponse<SubmissionResponse>.Ok(await handlers.Submit(lessonId,assignmentId,request.Text,request.Confirmed,request.File?.FileName,request.File?.ContentType,request.File?.Length,source,token)));
    }
    [HttpPost("{assignmentId:guid}/submissions/{submissionId:guid}/grade"),Authorize(Roles="Instructor,Admin,SuperAdmin")]
    public async Task<IActionResult> Grade(Guid lessonId,Guid assignmentId,Guid submissionId,GradeRequest request,CancellationToken token)=>Ok(ApiResponse<SubmissionResponse>.Ok(await handlers.Grade(lessonId,assignmentId,submissionId,request,token)));
    [HttpGet("{assignmentId:guid}/submissions/{submissionId:guid}/attachment"),HttpHead("{assignmentId:guid}/submissions/{submissionId:guid}/attachment")]
    public async Task<IActionResult> Attachment(Guid lessonId,Guid assignmentId,Guid submissionId,CancellationToken token)
    {
        var stream=await handlers.Attachment(lessonId,assignmentId,submissionId,org.CanManageContent,token);
        Response.Headers.CacheControl="private, no-store";Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers["Content-Security-Policy"]="sandbox; default-src 'none'";
        return File(stream,"application/pdf",$"assignment-{submissionId:N}.pdf");
    }
}
public sealed class SubmissionUpload
{public string? Text{get;set;}public bool Confirmed{get;set;}public IFormFile? File{get;set;}}
