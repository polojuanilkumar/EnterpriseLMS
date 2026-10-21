using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LMS.Application.Features.Auditing;
using LMS.Application.Features.Organizations;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LMS.Tests.Integration;

public sealed class SqlIntegrationFactAttribute : FactAttribute
{
    public SqlIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LMS_SQL_INTEGRATION") != "1")
            Skip = "SQL integration requires explicit LMS_SQL_INTEGRATION=1 and a cached Docker SQL Server image.";
    }
}

// Never accepts a connection string. Only the container created and verified by this fixture is used.
public sealed class DisposableSqlServer : IAsyncLifetime
{
    private string? container;
    private string? connection;
    private readonly string run = Guid.NewGuid().ToString("N");
    public string PrivateRoot { get; } = Path.Combine(Path.GetTempPath(), "LMS_Integration_" + Guid.NewGuid().ToString("N"));
    public static void ValidateTarget(string host, int port, string database, string expectedDatabase,
        string label, string expectedLabel, string binding)
    {
        if (host != "127.0.0.1" || binding != "127.0.0.1" || port is < 1024 or > 65535
            || database != expectedDatabase || !database.StartsWith("LMS_Integration_", StringComparison.Ordinal)
            || !Guid.TryParseExact(database[16..], "N", out _) || label != expectedLabel
            || !Guid.TryParseExact(expectedLabel, "N", out _))
            throw new InvalidOperationException("Refusing an unverified integration target.");
    }
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("LMS_SQL_INTEGRATION") != "1") return;
        var image = Environment.GetEnvironmentVariable("LMS_SQL_IMAGE") ?? "mcr.microsoft.com/mssql/server:2022-latest";
        // Inspect only; never pull/install. No appsettings or ambient SQL connection variables are read.
        await Docker("image", "inspect", image);
        Directory.CreateDirectory(PrivateRoot);
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Test directory owner unavailable.");
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(owner);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow)); new DirectoryInfo(PrivateRoot).SetAccessControl(acl);
        }
        else File.SetUnixFileMode(PrivateRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var password = "Lms!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
        var envFile = Path.Combine(PrivateRoot, "container.env");
        await File.WriteAllTextAsync(envFile, "ACCEPT_EULA=Y\nMSSQL_PID=Developer\nMSSQL_SA_PASSWORD=" + password + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(envFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        try
        {
            container = (await Docker("run", "--detach", "--pull=never", "--label", "lms.integration=" + run,
                "--name", "lms-integration-" + run, "--publish", "127.0.0.1::1433", "--env-file", envFile, image)).Trim();
            if (container.Length != 64 || !container.All(Uri.IsHexDigit))
                throw new InvalidOperationException("Docker did not return a container ID.");
        }
        finally { File.Delete(envFile); }
        try
        {
            using var inspect = JsonDocument.Parse(await Docker("inspect", container));
            var root = inspect.RootElement[0];
            var label = root.GetProperty("Config").GetProperty("Labels").GetProperty("lms.integration").GetString()!;
            var ports = root.GetProperty("NetworkSettings").GetProperty("Ports").GetProperty("1433/tcp");
            if (ports.GetArrayLength() != 1) throw new InvalidOperationException("Unexpected SQL port bindings.");
            var binding = ports[0];
            var port = int.Parse(binding.GetProperty("HostPort").GetString()!);
            var database = "LMS_Integration_" + run;
            ValidateTarget("127.0.0.1", port, database, database, label, run, binding.GetProperty("HostIp").GetString()!);
            var cs = new SqlConnectionStringBuilder { DataSource = "127.0.0.1," + port, InitialCatalog = "master",
                UserID = "sa", Password = password, TrustServerCertificate = true, ConnectTimeout = 2, Pooling = false };
            // Credentials and SQL exceptions are deliberately excluded from prerequisite diagnostics.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (true)
            {
                try { await using var probe = new SqlConnection(cs.ConnectionString); await probe.OpenAsync(deadline.Token); break; }
                catch (SqlException) when (!deadline.IsCancellationRequested) { await Task.Delay(500, deadline.Token); }
            }
            cs.InitialCatalog = database;
            connection = cs.ConnectionString;
            await using var db = Context();
            await db.Database.MigrateAsync(deadline.Token); // Only this new, verified container/database.
        }
        catch { await DisposeAsync(); throw; }
    }
    public LMSDbContext Context(Guid? organization = null, Guid? actor = null,
        OrganizationRole role = OrganizationRole.Admin, bool platform = false, params IInterceptor[] interceptors)
    {
        if (connection is null) throw new InvalidOperationException("Disposable SQL fixture is not initialized.");
        var scope = new OrganizationContext();
        if (organization.HasValue) scope.Initialize(organization.Value, actor!.Value, role, platform);
        var audit = new AuditRequestContext();
        if (actor.HasValue) audit.Authenticate(actor.Value);
        var options = new DbContextOptionsBuilder<LMSDbContext>().UseSqlServer(connection, o =>
        { if (interceptors.Length > 0) o.MaxBatchSize(1); }) // Failure injection can occur after an earlier business command executes.
            .AddInterceptors(new AuditCommandGuard()).AddInterceptors(interceptors).Options;
        return new LMSDbContext(options, scope, audit);
    }
    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            var id = container;
            // Verify ownership again before deleting exactly one container, never volumes or other containers.
            var label = (await Docker("inspect", "--format", "{{index .Config.Labels \"lms.integration\"}}", id)).Trim();
            if (label != run) throw new InvalidOperationException("Refusing cleanup of a foreign container.");
            await Docker("rm", "--force", id);
            container = null;
        }
        connection = null;
        var resolved = Path.GetFullPath(PrivateRoot);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved).StartsWith("LMS_Integration_", StringComparison.Ordinal)
            || (Directory.Exists(resolved) && new DirectoryInfo(resolved).Attributes.HasFlag(FileAttributes.ReparsePoint)))
            throw new InvalidOperationException("Refusing unsafe test storage cleanup.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
    private static async Task<string> Docker(params string[] args)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker unavailable.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(true); throw; }
            await error; // Never echo potentially sensitive daemon diagnostics.
            if (process.ExitCode != 0) throw new InvalidOperationException("Docker prerequisite/operation failed; verify daemon and cached SQL image. No fallback is permitted.");
            return await output;
        }
        catch (System.ComponentModel.Win32Exception)
        { throw new InvalidOperationException("SQL integration blocked: Docker executable is unavailable. No fallback is permitted."); }
    }
}

[CollectionDefinition("Disposable SQL", DisableParallelization = true)]
public sealed class SqlCollection : ICollectionFixture<DisposableSqlServer> { }
