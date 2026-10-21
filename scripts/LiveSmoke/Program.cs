using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Identity;
using LMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// No application configuration is loaded by this executable.
var mode = args[0];
var root = Path.GetFullPath(args[1]);
if (mode == "certificate")
{
    using var rsa = RSA.Create(2048);
    using var caKey = RSA.Create(3072);
    var caRequest = new CertificateRequest("CN=LMS disposable smoke CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
    using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddDays(8));
    var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
    using var cert = request.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7), RandomNumberGenerator.GetBytes(16));
    File.WriteAllText(Path.Combine(root, "sql.pem"), cert.ExportCertificatePem());
    File.WriteAllText(Path.Combine(root, "sql.key"), rsa.ExportPkcs8PrivateKeyPem());
    return;
}
var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "state.json"))).RootElement;
string S(string key) => state.GetProperty(key).GetString()!;
var run = S("Run");
if (!Guid.TryParseExact(run, "N", out _) || Path.GetFileName(root) != run) throw new Exception("Invalid run directory.");
var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
start.ArgumentList.Add("inspect"); start.ArgumentList.Add(S("Container"));
using (var process = Process.Start(start)!)
{
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(); await error;
    if (process.ExitCode != 0) throw new Exception("Owned container unavailable.");
    using var document = JsonDocument.Parse(await output); var c = document.RootElement[0];
    var ports = c.GetProperty("NetworkSettings").GetProperty("Ports").GetProperty("1433/tcp");
    if (c.GetProperty("Id").GetString() != S("Container") || c.GetProperty("Name").GetString() != "/lms-smoke-" + run
        || c.GetProperty("Config").GetProperty("Labels").GetProperty("lms.smoke.owner").GetString() != run
        || ports.GetArrayLength() != 1 || ports[0].GetProperty("HostIp").GetString() != "127.0.0.1"
        || ports[0].GetProperty("HostPort").GetString() != S("SqlPort")) throw new Exception("Container ownership/binding mismatch.");
}
var secrets = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "private.json"))).RootElement;
string Secret(string key) => secrets.GetProperty(key).GetString()!;
Guid Id(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes("LMS synthetic smoke " + key)));
var orgA = Id("org-a"); var orgB = Id("org-b");
if (mode == "refresh-snapshot")
{
    var outputPath = Path.GetFullPath(args[2]);
    if (!outputPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Snapshot path must stay inside this run.");
    var cs = new SqlConnectionStringBuilder { DataSource = "127.0.0.1," + S("SqlPort"), InitialCatalog = "LMS_Smoke_" + run, UserID = "sa", Password = Secret("SqlPassword"), Encrypt = SqlConnectionEncryptOption.Strict, ServerCertificate = Path.Combine(root, "sql.pem"), HostNameInCertificate = "localhost", Pooling = false, ConnectTimeout = 5 };
    await using var sql = new SqlConnection(cs.ConnectionString); await sql.OpenAsync();
    await using (var check = sql.CreateCommand()) { check.CommandText = "SELECT DB_NAME()"; if ((string?)await check.ExecuteScalarAsync() != cs.InitialCatalog) throw new Exception("Wrong database."); }
    await using (var roles = sql.CreateCommand()) { roles.CommandText = "SELECT COUNT(*) FROM AspNetRoles WHERE Name IN ('Student','Instructor','Admin','SuperAdmin')"; if (Convert.ToInt32(await roles.ExecuteScalarAsync()) != 4) throw new Exception("Expected roles missing; refusing startup that would seed roles."); }
    var rows = new SortedDictionary<string, object>();
    foreach (var table in new[] { "Courses", "CourseSections", "Lessons", "LessonContentItems", "CourseEnrollments", "LessonProgresses", "Quizzes", "QuizQuestions", "QuizOptions", "QuizAttempts", "QuizAttemptAnswers", "QuizAttemptAnswerOptions", "LessonAssignments", "AssignmentSubmissions", "AssignmentGrades", "AspNetRoles", "__EFMigrationsHistory" })
    {
        // Fixed table names only; SELECT only, no ORM initialization, migration or seed.
        var order = table == "__EFMigrationsHistory" ? "MigrationId" : "Id";
        await using var command = sql.CreateCommand(); command.CommandText = $"SELECT * FROM [dbo].[{table}] ORDER BY [{order}] FOR JSON PATH, INCLUDE_NULL_VALUES";
        var body = new StringBuilder();
        await using (var reader = await command.ExecuteReaderAsync()) { while (await reader.ReadAsync()) body.Append(reader.GetString(0)); }
        var json = body.Length == 0 ? "[]" : body.ToString(); var count = JsonDocument.Parse(json).RootElement.GetArrayLength();
        rows[table] = new { count, sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))) };
    }
    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Read-only disposable database snapshot recorded; strict pinned TLS and expected existing roles verified.");
}
else if (mode == "refresh-assets")
{
    await RefreshVerification.Run(root, state);
}
else if (mode == "assessment-acceptance")
{
    await AssessmentAcceptance.Run(root, state, secrets);
}
else if (mode == "diagnose-login")
{
    var lines = await File.ReadAllLinesAsync(Path.Combine(root, "credentials.txt"));
    const string prefix = "Disposable synthetic identities only. Password for these five accounts: ";
    if (!lines[0].StartsWith(prefix)) throw new Exception("Unsupported credential file format.");
    var password = lines[0][prefix.Length..];
    var email = lines.Single(x => x == "smoke-student@example.invalid");
    Console.WriteLine($"Credential file/private secret match: {password == Secret("LoginPassword")}");
    var cs = new SqlConnectionStringBuilder { DataSource = "127.0.0.1," + S("SqlPort"), InitialCatalog = "LMS_Smoke_" + run, UserID = "sa", Password = Secret("SqlPassword"), Encrypt = SqlConnectionEncryptOption.Strict, ServerCertificate = Path.Combine(root, "sql.pem"), HostNameInCertificate = "localhost", Pooling = false, ConnectTimeout = 5 };
    await using var sql = new SqlConnection(cs.ConnectionString);
    await sql.OpenAsync();
    await using (var entered = sql.CreateCommand())
    {
        entered.CommandText = "SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail = @email";
        entered.Parameters.AddWithValue("@email", "student@example.invalid".ToUpperInvariant());
        Console.WriteLine($"Entered email is listed in credential file: {lines.Contains("student@example.invalid")}; entered email exists in owned database: {Convert.ToInt32(await entered.ExecuteScalarAsync()) != 0}");
    }
    await using var command = sql.CreateCommand();
    command.CommandText = "SELECT Id, PasswordHash, EmailConfirmed, LockoutEnabled, LockoutEnd, AccessFailedCount FROM AspNetUsers WHERE NormalizedEmail = @email";
    command.Parameters.AddWithValue("@email", email.ToUpperInvariant());
    await using (var reader = await command.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync()) throw new Exception("Synthetic Student missing.");
        var user = new ApplicationUser { Id = reader.GetGuid(0) };
        Console.WriteLine($"Synthetic Student identity matches run fixture: {user.Id == Id("student")}; password verifies: {new PasswordHasher<ApplicationUser>().VerifyHashedPassword(user, reader.GetString(1), password) != PasswordVerificationResult.Failed}; email confirmed: {reader.GetBoolean(2)}");
        Console.WriteLine($"Synthetic Student currently locked out: {reader.GetBoolean(3) && !reader.IsDBNull(4) && reader.GetFieldValue<DateTimeOffset>(4) > DateTimeOffset.UtcNow}; failed access count: {reader.GetInt32(5)}");
    }
    var jar = new CookieContainer();
    using var client = new HttpClient(new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = false }) { BaseAddress = new Uri(S("WebUrl")) };
    using var csrf = await client.GetAsync("/bff/antiforgery");
    using var bootstrap = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
    using (var wrong = new HttpRequestMessage(HttpMethod.Post, "/bff/login") { Content = JsonContent.Create(new { email = "student@example.invalid", password }) })
    {
        wrong.Headers.Add("X-CSRF-TOKEN", bootstrap.RootElement.GetProperty("requestToken").GetString());
        using var response = await client.SendAsync(wrong);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var invalid = body.RootElement.TryGetProperty("code", out var code) && code.GetString() == "invalid_credentials";
        Console.WriteLine($"Entered-email BFF login HTTP {(int)response.StatusCode}; error is invalid_credentials: {invalid}; rate limited: {(int)response.StatusCode == 429}; CSRF rejected: {(int)response.StatusCode == 400}");
    }
    using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login") { Content = JsonContent.Create(new { email, password }) };
    request.Headers.Add("X-CSRF-TOKEN", bootstrap.RootElement.GetProperty("requestToken").GetString());
    using var login = await client.SendAsync(request);
    Console.WriteLine($"Fresh cookie jar: CSRF bootstrap HTTP {(int)csrf.StatusCode}; login HTTP {(int)login.StatusCode}; secure session cookie accepted: {jar.GetCookies(client.BaseAddress).Cast<Cookie>().Any(c => c.Name == "__Host-LmsLearner" && c.Secure && c.HttpOnly)}");
    using var session = await client.GetAsync("/bff/session");
    Console.WriteLine($"Session HTTP {(int)session.StatusCode}");
    if (!login.IsSuccessStatusCode || !session.IsSuccessStatusCode) throw new Exception("Fresh BFF login/session failed (response bodies suppressed).");
    using var refreshed = await client.GetAsync("/bff/antiforgery");
    using var refreshedBody = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
    using var logout = new HttpRequestMessage(HttpMethod.Post, "/bff/logout") { Content = JsonContent.Create(new { }) };
    logout.Headers.Add("X-CSRF-TOKEN", refreshedBody.RootElement.GetProperty("requestToken").GetString());
    using var loggedOut = await client.SendAsync(logout);
    Console.WriteLine($"Diagnostic session logout HTTP {(int)loggedOut.StatusCode}");
}
else if (mode == "probe")
{
    var cs = new SqlConnectionStringBuilder { DataSource = "127.0.0.1," + S("SqlPort"), InitialCatalog = "LMS_Smoke_" + run, UserID = "sa", Password = Secret("SqlPassword"), Encrypt = SqlConnectionEncryptOption.Strict, ServerCertificate = Path.Combine(root, "sql.pem"), HostNameInCertificate = "localhost", Pooling = false, ConnectTimeout = 2 };
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
    while (true)
    {
        try { await using var sql = new SqlConnection(cs.ConnectionString); await sql.OpenAsync(deadline.Token); break; }
        catch (SqlException) when (!deadline.IsCancellationRequested) { await Task.Delay(1000, deadline.Token); }
    }
    Console.WriteLine("Existing run database reachable with strict pinned TLS.");
}
else if (mode == "seed")
{
    var cs = new SqlConnectionStringBuilder { DataSource = "127.0.0.1," + S("SqlPort"), InitialCatalog = "master", UserID = "sa", Password = Secret("SqlPassword"), Encrypt = SqlConnectionEncryptOption.Strict, ServerCertificate = Path.Combine(root, "sql.pem"), HostNameInCertificate = "localhost", Pooling = false, ConnectTimeout = 2 };
    // SQL uses TDS 8 strict encryption with this run's exact certificate pin; no machine trust change.
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
    while (true)
    {
        try { await using var sql = new SqlConnection(cs.ConnectionString); await sql.OpenAsync(deadline.Token); break; }
        catch (SqlException) when (!deadline.IsCancellationRequested) { await Task.Delay(1000, deadline.Token); }
    }
    cs.InitialCatalog = "LMS_Smoke_" + run;
    LMSDbContext Context(Guid? org = null)
    {
        var scope = new OrganizationContext();
        if (org.HasValue) scope.Initialize(org.Value, Id("admin"), OrganizationRole.Admin, false);
        return new(new DbContextOptionsBuilder<LMSDbContext>().UseSqlServer(cs.ConnectionString).Options, scope);
    }
    await using (var db = Context())
    {
        await db.Database.MigrateAsync();
        foreach (var (key, name) in new[] { ("org-a", "SMOKE Alpha"), ("org-b", "SMOKE Beta") })
        {
            var organizationId = Id(key);
            if (!await db.Organizations.AnyAsync(x => x.Id == organizationId))
            { var org = new Organization("SMOKE-" + key, name); db.Entry(org).Property(x => x.Id).CurrentValue = Id(key); db.Add(org); }
        }
        foreach (var role in new[] { "Student", "Instructor", "Admin", "SuperAdmin" })
        {
            var roleId = Id(role);
            if (!await db.Roles.AnyAsync(x => x.Id == roleId)) db.Roles.Add(new IdentityRole<Guid>(role) { Id = Id(role), NormalizedName = role.ToUpperInvariant() });
        }
        foreach (var (key, role) in new[] { ("student", "Student"), ("instructor", "Instructor"), ("admin", "Admin"), ("superadmin", "SuperAdmin"), ("outsider", "Student") })
        {
            var userId = Id(key);
            if (await db.Users.AnyAsync(x => x.Id == userId)) continue;
            var email = "smoke-" + key + "@example.invalid";
            var user = new ApplicationUser { Id = Id(key), UserName = email, Email = email, NormalizedUserName = email.ToUpperInvariant(), NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("N"), FirstName = "SMOKE", LastName = key };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Secret("LoginPassword"));
            db.Users.Add(user); db.UserProfiles.Add(new UserProfile(user.Id));
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = Id(role) });
            if (key != "superadmin") db.OrganizationMemberships.Add(new OrganizationMembership(key == "outsider" ? orgB : orgA, user.Id, Enum.Parse<OrganizationRole>(role)));
            if (key is "admin" or "instructor") db.OrganizationMemberships.Add(new OrganizationMembership(orgB, user.Id, Enum.Parse<OrganizationRole>(role)));
        }
        await db.SaveChangesAsync();
    }
    foreach (var (org, key) in new[] { (orgA, "alpha"), (orgB, "beta") })
    {
        await using var db = Context(org);
        var courseId = Id(key);
        if (await db.Courses.AnyAsync(c => c.Id == courseId)) continue;
        var course = new Course("SMOKE " + key + " course", "SMOKE-" + key, "Disposable synthetic course", Id("instructor"), CourseLevel.Beginner);
        db.Entry(course).Property(x => x.Id).CurrentValue = Id(key); course.AssignOrganization(org); course.Publish(); db.Add(course); await db.SaveChangesAsync();
        var section = new CourseSection(course.Id, "SMOKE introduction", "Synthetic section", 1);
        db.Entry(section).Property(x => x.Id).CurrentValue = Id(key + "-section"); db.Add(section); await db.SaveChangesAsync();
        var lesson = new Lesson(section.Id, "SMOKE welcome", "Synthetic text lesson", LessonType.Text, "Welcome to the isolated smoke environment.", null, 2, 1);
        db.Entry(lesson).Property(x => x.Id).CurrentValue = Id(key + "-lesson"); lesson.Publish(); db.Add(lesson); await db.SaveChangesAsync();
        db.CourseEnrollments.Add(new CourseEnrollment(course.Id, Id(key == "alpha" ? "student" : "outsider"))); await db.SaveChangesAsync();
    }
    await File.WriteAllTextAsync(Path.Combine(root, "credentials.txt"), "Disposable synthetic identities only. Password for these five accounts: " + Secret("LoginPassword") + "\n" + string.Join("\n", new[] { "student", "instructor", "admin", "superadmin", "outsider" }.Select(x => "smoke-" + x + "@example.invalid")));
    await File.WriteAllTextAsync(Path.Combine(root, "fixtures.json"), JsonSerializer.Serialize(new { orgA, orgB, course = Id("alpha"), section = Id("alpha-section"), lesson = Id("alpha-lesson"), foreignCourse = Id("beta") }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Disposable migrations and synthetic fixtures ready.");
}
else if (mode == "verify")
{
    var cookies = new CookieContainer();
    using var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = new Uri(S("WebUrl")), Timeout = TimeSpan.FromSeconds(30) };
    var results = new List<string>();
    async Task<(HttpResponseMessage Response, string Body)> Request(string path, int expected, object? body = null, string? csrf = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != expected) throw new Exception($"HTTP {path}: expected {expected}, got {(int)response.StatusCode}.");
        if (path.StartsWith("/bff") && expected != 204 && response.Content.Headers.ContentType?.MediaType != "application/json") throw new Exception("BFF returned non-JSON.");
        if (Regex.IsMatch(text, "eyJ[A-Za-z0-9_-]+\\.eyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+") || Regex.IsMatch(text, "\"(accessToken|sessionId|sessionReference)\"", RegexOptions.IgnoreCase)) throw new Exception("Unexpected secret in response.");
        if (response.Headers.Location is not null) throw new Exception("Unexpected redirect.");
        results.Add($"PASS HTTP {path} -> {expected}; no JWT/session reference in body or redirect");
        return (response, text);
    }
    async Task<string> Csrf() { var r = await Request("/bff/antiforgery", 200); return JsonDocument.Parse(r.Body).RootElement.GetProperty("requestToken").GetString()!; }
    await Request("/bff/session", 401);
    await Request("/bff/login", 400, new { email = "smoke-student@example.invalid", password = Secret("LoginPassword") }, "invalid");
    var login = await Request("/bff/login", 200, new { email = "smoke-student@example.invalid", password = Secret("LoginPassword") }, await Csrf());
    var cookie = login.Response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-LmsLearner="));
    if (!new[] { "secure", "httponly", "samesite=strict", "path=/" }.All(x => cookie.Contains(x, StringComparison.OrdinalIgnoreCase))) throw new Exception("Cookie security attributes missing.");
    results.Add("PASS Secure/HttpOnly/SameSite=Strict host cookie issuance");
    var session = await Request("/bff/session", 200);
    if (!JsonDocument.Parse(session.Body).RootElement.GetProperty("requiresOrganizationSelection").GetBoolean()) throw new Exception("Selection was not explicit.");
    var orgs = await Request("/bff/organizations", 200);
    if (orgs.Body.Contains(orgB.ToString()) || !orgs.Body.Contains(orgA.ToString())) throw new Exception("Organization discovery isolation failed.");
    await Request("/bff/organization", 200, new { organizationId = orgA }, await Csrf());
    var catalog = await Request("/bff/student/courses", 200);
    var mine = await Request("/bff/student/my-courses", 200);
    if (!catalog.Body.Contains(Id("alpha").ToString()) || !mine.Body.Contains(Id("alpha").ToString()) || catalog.Body.Contains(Id("beta").ToString())) throw new Exception("Course listing mismatch.");
    await Request("/bff/student/courses/" + Id("alpha"), 200);
    await Request($"/bff/student/courses/{Id("alpha")}/sections/{Id("alpha-section")}/lessons/{Id("alpha-lesson")}", 200);
    await Request("/bff/student/courses/" + Id("beta"), 404);
    await Request("/bff/organization", 403, new { organizationId = orgB }, await Csrf());
    await Request("/bff/logout", 204, new { }, await Csrf());
    await Request("/bff/session", 401);
    var index = await Request("/app/", 200);
    var deep = await Request("/app/workspace/student/catalog", 200);
    if (index.Body != deep.Body || !index.Body.Contains("<base href=\"/app/\"")) throw new Exception("Deep link fallback/base mismatch.");
    foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "web", "wwwroot", "app"), "*", SearchOption.AllDirectories).Where(x => x.EndsWith(".js") || x.EndsWith(".css")))
    {
        var relative = Path.GetRelativePath(Path.Combine(root, "web", "wwwroot", "app"), file).Replace('\\', '/');
        var response = await Request("/app/" + relative, 200);
        if (response.Body.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)) throw new Exception("Asset returned HTML.");
    }
    results.Add("UNVERIFIED browser execution, lazy route execution, browser storage and browser cookie acceptance (HTTP only)");
    await File.WriteAllLinesAsync(Path.Combine(root, "http-results.txt"), results);
    Console.WriteLine($"HTTP smoke passed ({results.Count - 1} checks); browser checks remain manual.");
}
else throw new Exception("Unknown command.");
