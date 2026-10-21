namespace LMS.Domain.Entities;

public sealed class Organization
{
    public static readonly Guid LegacyId = Guid.Parse("6f085af1-c85b-47c1-bd1e-73435b3e0001");
    private Organization() { }
    public Organization(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
            throw new ArgumentException("Organization code must contain 1 to 50 characters.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Organization name must contain 1 to 200 characters.");
        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = true;
    }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
}
