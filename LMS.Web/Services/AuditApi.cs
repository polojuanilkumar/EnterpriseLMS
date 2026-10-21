using LMS.Web.Models;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;
namespace LMS.Web.Services;
public sealed partial class LearnerApi
{
    public static string AuditQuery(string path,AuditViewFilter f)
    {
        f.Validate();
        return QueryHelpers.AddQueryString(path,new Dictionary<string,string?> {
            ["page"]=f.Page.ToString(CultureInfo.InvariantCulture),["pageSize"]=f.PageSize.ToString(CultureInfo.InvariantCulture),
            ["fromUtc"]=f.FromUtc?.ToString("O",CultureInfo.InvariantCulture),["toUtc"]=f.ToUtc?.ToString("O",CultureInfo.InvariantCulture),
            ["action"]=f.Action,["outcome"]=f.Outcome,["actorId"]=f.ActorId?.ToString("D"),["resourceId"]=f.ResourceId?.ToString("D")});
    }
    private static void AuditRowScope(AuditViewRow row,Guid? org)
    {
        if(row.OrganizationId!=org||org.HasValue&&row.Scope!="organization"||!org.HasValue&&row.Scope is not ("platform" or "account")
            ||!AuditDisplay.Actions.Contains(row.Action)||!AuditDisplay.Outcomes.Contains(row.Outcome)||row.Metadata.Length>512)
            throw new WebApiException(502);
    }
    public async Task<AuditViewPage> AuditPage(LearnerSession session,Guid? org,AuditViewFilter filter,CancellationToken token)
    {
        if(org.HasValue)await StaffAccess(session,org.Value,true,token);else await PlatformAccess(session,token);
        var path=org.HasValue?$"api/audit/organizations/{org}":"api/audit/platform";
        var page=await Get<AuditViewPage>(AuditQuery(path,filter),session,org,token);
        if(page.OrganizationId!=org||page.Page!=filter.Page||page.PageSize!=filter.PageSize||page.Items.Length>filter.PageSize||page.Total<0)throw new WebApiException(502);
        foreach(var row in page.Items)AuditRowScope(row,org);
        return page;
    }
    public async Task<AuditViewRow> AuditDetail(LearnerSession session,Guid? org,Guid id,CancellationToken token)
    {
        if(org.HasValue)await StaffAccess(session,org.Value,true,token);else await PlatformAccess(session,token);
        var path=org.HasValue?$"api/audit/organizations/{org}/{id}":$"api/audit/platform/{id}";
        var row=await Get<AuditViewRow>(path,session,org,token);AuditRowScope(row,org);
        if(row.Id!=id)throw new WebApiException(404);return row;
    }
}
