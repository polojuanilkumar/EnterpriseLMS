namespace LMS.Infrastructure.Identity;
public static class AccountLinkPolicy
{
    public static string Destination(string path,string? destination) => destination switch
    {
        null or "" or "Mvc" => path,
        "Angular" when path is "account/invitation" or "account/reset-password" => path.Replace("account/","account/angular/",StringComparison.Ordinal),
        _ => throw new InvalidOperationException("AccountLinks:Destination must be Mvc or Angular.")
    };
    public static Uri TrustedOrigin(string? configured)
    {
        if(!Uri.TryCreate(configured,UriKind.Absolute,out var origin) || origin.Scheme!="https"
            || origin.UserInfo.Length!=0 || origin.AbsolutePath!="/" || origin.Query.Length!=0 || origin.Fragment.Length!=0)
            throw new InvalidOperationException("AccountLinks:PublicUrl must be a trusted HTTPS origin.");
        return origin;
    }
}
