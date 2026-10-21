using System.ComponentModel.DataAnnotations;
namespace LMS.Web.Models;
public sealed class BffAssignmentUpload
{
    [StringLength(100000)] public string? Text { get; set; }
    public bool Confirmed { get; set; }
    public IFormFile? File { get; set; }
}
