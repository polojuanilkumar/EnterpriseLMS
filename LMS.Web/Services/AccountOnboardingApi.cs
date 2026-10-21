using System.Net.Http.Json;
using LMS.Web.Models;
namespace LMS.Web.Services;
public sealed partial class LearnerApi
{
    private sealed record Csrf(string Token);
    public async Task<bool> SessionValid(LearnerSession session,CancellationToken ct)
    {
        using var request=Request(HttpMethod.Get,"api/account/session",session,null);
        using var response=await http.SendAsync(request,ct);
        return response.IsSuccessStatusCode;
    }
    public async Task<T?> AccountRequest<T>(string path,object? body,LearnerSession? session,Guid? org,CancellationToken ct,bool read=false)
    {
        using var request=session is null ? new HttpRequestMessage(read?HttpMethod.Get:HttpMethod.Post,path)
            : Request(read?HttpMethod.Get:HttpMethod.Post,path,session,org);
        if(!read)
        {
            // Obtain an API antiforgery pair with the same bearer identity, kept entirely server-side.
            using var csrfRequest=session is null ? new HttpRequestMessage(HttpMethod.Get,"api/account/antiforgery")
                : Request(HttpMethod.Get,"api/account/antiforgery",session,null);
            using var csrfResponse=await http.SendAsync(csrfRequest,ct);Ensure(csrfResponse);
            var csrf=await csrfResponse.Content.ReadFromJsonAsync<Csrf>(cancellationToken:ct) ?? throw new WebApiException(502);
            request.Headers.Add("X-CSRF-TOKEN",csrf.Token);
            if(csrfResponse.Headers.TryGetValues("Set-Cookie",out var cookies))request.Headers.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Split(';')[0])));
            request.Content=JsonContent.Create(body);
        }
        using var response=await http.SendAsync(request,ct);Ensure(response);
        if(response.Content.Headers.ContentLength==0)return default;
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct);
    }
}
