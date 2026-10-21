using System.Xml.Linq;
using Xunit;
namespace LMS.Tests.Integration;
public sealed class ReleasePackagingTests
{
    private static string Workspace()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EnterpriseLMS.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Run release packaging checks from the repository build output.");
    }
    [Theory]
    [InlineData("PrivateMedia")]
    [InlineData("PrivateAssignments")]
    [InlineData("PrivateEmails")]
    public void Sensitive_storage_is_excluded_from_Git_and_publish_items(string name)
    {
        var root = Workspace();
        Assert.Contains("LMS.Api/App_Data/" + name + "/", File.ReadAllText(Path.Combine(root, ".gitignore")));
        var project = XDocument.Load(Path.Combine(root, "LMS.Api", "LMS.Api.csproj"));
        foreach (var item in new[] { "Content", "None" })
            Assert.Contains(project.Descendants(item), x => x.Attribute("Remove")?.Value.Replace('\\', '/') == "App_Data/" + name + "/**");
    }
}
