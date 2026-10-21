using System.ComponentModel.DataAnnotations;
namespace LMS.Web.Models;
public sealed class RecoveryForm
{
    [Required,EmailAddress,StringLength(256)] public string Email { get; set; }="";
    public string Password { get; set; }="";
    public string FirstName { get; set; }="";
    public string LastName { get; set; }="";
    public string Organization { get; set; }="";
    public string Role { get; set; }="";
    public bool ExistingAccount { get; set; }
}
public sealed class InviteForm
{
    [Required,EmailAddress,StringLength(256)] public string Email { get; set; }="";
    [Range(1,3)] public int Role { get; set; }=1;
    public bool Confirmed { get; set; }
    public bool Reissue { get; set; }
}
public sealed record WebInvitation(Guid Id,Guid OrganizationId,string Email,int Role,DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,DateTimeOffset? AcceptedAt,DateTimeOffset? RevokedAt,DateTimeOffset? DeliveredAt);
public sealed record WebInvitationPage(int Page,int PageSize,int Total,WebInvitation[] Items);
public sealed record WebInvitationPreview(string Email,string Organization,int Role,bool ExistingAccount);
