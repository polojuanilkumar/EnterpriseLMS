using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace LMS.Web.Services;

public sealed partial class LearnerApi
{
    public async Task<MemberView> OrganizationMember(LearnerSession session,Guid org,Guid user,CancellationToken token)
    {
        await StaffAccess(session,org,true,token);
        var row=await Get<MemberView>($"api/organizations/{org}/members/{user}",session,org,token);
        if(row.OrganizationId!=org || row.UserId!=user || row.Role is <1 or >3)throw new WebApiException(404);
        return row;
    }
    private static string AdministrationQuery(string path, OrganizationFilterForm filter)
    {
        if (filter.Page is < 1 or > 10000 || filter.PageSize is < 1 or > 50 || filter.Search?.Length > 100 || filter.Role is < 1 or > 3)
            throw new WebApiException(400);
        return QueryHelpers.AddQueryString(path, new Dictionary<string,string?> {
            ["page"]=filter.Page.ToString(CultureInfo.InvariantCulture), ["pageSize"]=filter.PageSize.ToString(CultureInfo.InvariantCulture),
            ["search"]=filter.Search, ["role"]=filter.Role?.ToString(CultureInfo.InvariantCulture) });
    }
    private static void CheckAdministrationPage<T>(AdministrationPage<T> page, Guid? org, OrganizationFilterForm filter)
    {
        if (page.OrganizationId != org || page.Items is null || page.Page != filter.Page || page.PageSize != filter.PageSize
            || page.Total < 0 || page.Items.Length > filter.PageSize) throw new WebApiException(502);
    }
    public async Task<MembersPage> Members(LearnerSession session, Guid org, OrganizationFilterForm filter, CancellationToken token)
    {
        await StaffAccess(session,org,true,token);
        var page = await Get<AdministrationPage<MemberView>>(AdministrationQuery($"api/organizations/{org}/members",filter),session,org,token);
        CheckAdministrationPage(page,org,filter);
        if(page.Items.Any(x=>x.OrganizationId!=org || x.UserId==Guid.Empty || x.Role is <1 or >3 || filter.Role.HasValue && x.Role!=filter.Role))throw new WebApiException(502);
        return new(org,page.Items,page.Page,page.PageSize,page.Total,filter.Search,filter.Role);
    }
    public async Task PlatformAccess(LearnerSession session, CancellationToken token)
    {
        var access = await Get<PlatformAccessView>("api/organizations/platform/context",session,null,token);
        if(access.UserId!=session.UserId || !access.IsSuperAdmin)throw new WebApiException(403);
    }
    public async Task<PlatformOrganizationsPage> PlatformOrganizations(LearnerSession session, OrganizationFilterForm filter, CancellationToken token)
    {
        await PlatformAccess(session,token);
        if(filter.Role.HasValue)throw new WebApiException(400);
        var page=await Get<AdministrationPage<OrganizationView>>(AdministrationQuery("api/organizations",filter),session,null,token);
        CheckAdministrationPage(page,null,filter);
        if(page.Items.Any(x=>x.Id==Guid.Empty))throw new WebApiException(502);
        return new(page,filter);
    }
    public async Task CreateOrganization(LearnerSession session, OrganizationCreateForm form, CancellationToken token)
    {
        await PlatformAccess(session,token);
        if(!form.Confirmed)throw new WebApiException(400,"Confirm organization creation and initial Admin assignment.");
        await AdministrationChange(HttpMethod.Post,"api/organizations",form,session,null,token);
    }
    private async Task AdministrationChange(HttpMethod method,string path,object body,LearnerSession session,Guid? org,CancellationToken token)
    {
        using var request=Request(method,path,session,org); request.Content=JsonContent.Create(body);
        using var response=await http.SendAsync(request,token);
        if(response.IsSuccessStatusCode)return;
        string? message=null;
        if((int)response.StatusCode is 400 or 409)
        {
            using var json=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token),cancellationToken:token);
            if(json.RootElement.TryGetProperty("message",out var value) && value.ValueKind==JsonValueKind.String)
            {
                // Only known public administration messages are reflected into forms.
                var candidate=value.GetString();
                if(candidate is not null && AdministrationMessages.Contains(candidate))message=candidate;
            }
        }
        throw new WebApiException((int)response.StatusCode,message);
    }
    private static readonly HashSet<string> AdministrationMessages = [
        "Account unavailable or membership conflicts with current state.",
        "The last organization Admin cannot be demoted. Assign another Admin first.",
        "Reassign all courses before changing this Instructor's role.",
        "Resolve all Active enrollments before changing this Student's role.",
        "An organization Admin cannot demote their own membership.",
        "Organization code is already in use or account assignment is unavailable.",
        "Enter an exact email address, without a display name.",
        "Organization code must contain 1 to 50 characters.",
        "Organization name must contain 1 to 200 characters.",
        "Confirm organization creation and initial Admin assignment.",
        "Confirm the membership role change.",
        "Confirm membership creation and select a valid role."
    ];
}
