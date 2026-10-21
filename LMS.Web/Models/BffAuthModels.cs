namespace LMS.Web.Models;

public sealed record BffAntiforgery(string RequestToken, string HeaderName);
public sealed record BffLogin(Guid UserId, DateTimeOffset ExpiresAt, bool RequiresOrganizationSelection = true);
public sealed record BffOrganization(Guid Id, string? Role, string Name = "", string Code = "");
public sealed record BffOrganizationOption(Guid Id, string Name, string Code);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BffOrganizationSelection(Guid OrganizationId);
public sealed record BffSession(Guid UserId, DateTimeOffset ExpiresAt, BffOrganization? SelectedOrganization,
    bool IsSuperAdmin, bool RequiresOrganizationSelection, string? HomePath, string? RecoveryCode = null);
public sealed record BffError(string Code, string Message, IReadOnlyDictionary<string, string[]>? FieldErrors = null);
