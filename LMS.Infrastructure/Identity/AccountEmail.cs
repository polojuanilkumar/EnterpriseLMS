using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LMS.Infrastructure.Identity;

public sealed record AccountEmail(string To, string Subject, string Body);
public interface IAccountEmailSender
{
    Task SendAsync(AccountEmail email, CancellationToken cancellationToken = default);
}

// Deliberately has no HTTP endpoint. Deployment must restrict this directory to the service owner.
public sealed class DevelopmentAccountEmailSender(string directory) : IAccountEmailSender
{
    public async Task SendAsync(AccountEmail email, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        if(OperatingSystem.IsWindows())
        {
            var owner=WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Email capture owner unavailable.");
            var acl=new DirectorySecurity();
            acl.SetAccessRuleProtection(true,false);
            acl.SetOwner(owner);
            acl.AddAccessRule(new FileSystemAccessRule(owner,FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(acl);
        }
        else File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        var temporary=path+".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(email), cancellationToken);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temporary,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            File.Move(temporary,path);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
}

public sealed class UnconfiguredAccountEmailSender : IAccountEmailSender
{
    public Task SendAsync(AccountEmail email, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Configure an explicit production IAccountEmailSender before enabling account email.");
}
