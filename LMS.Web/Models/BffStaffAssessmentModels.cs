using System.ComponentModel.DataAnnotations;
using LMS.Web.Models;
namespace LMS.Web.Models;
public sealed class StaffAssignmentForm
{
    [Required,StringLength(200)] public string Title {get;set;}="";
    [StringLength(20000)] public string? Instructions {get;set;}
    public decimal? MaximumMarks {get;set;}
    public DateTimeOffset? DueAtUtc {get;set;}
    public bool ProtectedFieldsChanged {get;set;}
    public bool DueAtChanged {get;set;}
}
public sealed record StaffSubmissionPage(int Page,int PageSize,int Total,AssignmentSubmissionView[] Items);
