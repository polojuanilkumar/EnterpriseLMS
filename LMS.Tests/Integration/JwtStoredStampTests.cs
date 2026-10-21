using System.Security.Claims;
using LMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;
namespace LMS.Tests.Integration;
public sealed class JwtStoredStampTests
{
    [Theory]
    [InlineData("fresh", "fresh", true)]
    [InlineData("old", "fresh", false)]
    [InlineData(null, "fresh", false)]
    [InlineData("fresh", null, false)]
    public async Task Validation_reads_the_authenticated_subjects_stored_stamp(string? supplied, string? stored, bool valid)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        users.Setup(x => x.GetSecurityStampAsync(user)).ReturnsAsync(stored!);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()) };
        if (supplied is not null) claims.Add(new("lms-stamp", supplied));
        Assert.Equal(valid, await JwtSecurityStampValidation.ValidAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")), users.Object));
        users.Verify(x => x.GetSecurityStampAsync(user), Times.Once);
    }
    [Fact]
    public async Task Unknown_subject_and_missing_subject_cannot_validate()
    {
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var unknown = Guid.NewGuid().ToString(); users.Setup(x => x.FindByIdAsync(unknown)).ReturnsAsync((ApplicationUser?)null);
        Assert.False(await JwtSecurityStampValidation.ValidAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, unknown), new Claim("lms-stamp", "fresh")], "Bearer")), users.Object));
        Assert.False(await JwtSecurityStampValidation.ValidAsync(new ClaimsPrincipal(), users.Object));
        users.Verify(x => x.GetSecurityStampAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }
}
