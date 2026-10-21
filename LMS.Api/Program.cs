using Microsoft.AspNetCore.DataProtection;
using FluentValidation;
using LMS.Api.Middleware;
using LMS.Application.Features.Authentication.Login;
using LMS.Application.Features.Categories.ActivateCategory;
using LMS.Application.Features.Categories.CreateCategory;
using LMS.Application.Features.Categories.DeactivateCategory;
using LMS.Application.Features.Categories.GetCategories;
using LMS.Application.Features.Categories.GetCategoryById;
using LMS.Application.Features.Categories.UpdateCategory;
using LMS.Application.Features.CourseProgress.GetProgress;
using LMS.Application.Features.CourseProgress.MyCourses;
using LMS.Application.Features.Courses.ArchiveCourse;
using LMS.Application.Features.Courses.CreateCourse;
using LMS.Application.Features.Courses.GetCourseById;
using LMS.Application.Features.Courses.GetCourses;
using LMS.Application.Features.Courses.PublishCourse;
using LMS.Application.Features.Courses.UpdateCourse;
using LMS.Application.Features.CourseSections.CreateCourseSection;
using LMS.Application.Features.CourseSections.DeleteCourseSection;
using LMS.Application.Features.CourseSections.GetCourseSectionById;
using LMS.Application.Features.CourseSections.GetCourseSections;
using LMS.Application.Features.CourseSections.UpdateCourseSection;
using LMS.Application.Features.Enrollments.CancelEnrollment;
using LMS.Application.Features.Enrollments.CompleteEnrollment;
using LMS.Application.Features.Enrollments.EnrollCourse;
using LMS.Application.Features.Enrollments.GetEnrollmentById;
using LMS.Application.Features.Enrollments.GetMyEnrollments;
using LMS.Application.Features.LessonProgress.CompleteLesson;
using LMS.Application.Features.LessonProgress.GetProgress;
using LMS.Application.Features.LessonProgress.StartLesson;
using LMS.Application.Features.LessonProgress.UpdateProgress;
using LMS.Application.Features.Lessons.CreateLesson;
using LMS.Application.Features.Lessons.DeleteLesson;
using LMS.Application.Features.Lessons.GetLessonById;
using LMS.Application.Features.Lessons.GetLessons;
using LMS.Application.Features.Lessons.PublishLesson;
using LMS.Application.Features.Lessons.UnpublishLesson;
using LMS.Application.Features.Lessons.UpdateLesson;
using LMS.Application.Features.QuizOptions.CreateOption;
using LMS.Application.Features.QuizOptions.DeleteOption;
using LMS.Application.Features.QuizOptions.GetOptions;
using LMS.Application.Features.QuizOptions.GetStudentOptions;
using LMS.Application.Features.QuizOptions.UpdateOption;
using LMS.Application.Features.QuizQuestions.CreateQuestion;
using LMS.Application.Features.QuizQuestions.DeleteQuestion;
using LMS.Application.Features.QuizQuestions.GetQuestionById;
using LMS.Application.Features.QuizQuestions.GetQuestions;
using LMS.Application.Features.QuizQuestions.UpdateQuestion;
using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Features.Quizzes.CreateQuiz;
using LMS.Application.Features.Quizzes.DeleteQuiz;
using LMS.Application.Features.Quizzes.GetQuiz;
using LMS.Application.Features.Quizzes.PublishQuiz;
using LMS.Application.Features.Quizzes.UnpublishQuiz;
using LMS.Application.Features.Quizzes.UpdateQuiz;
using LMS.Application.Features.Users.Commands.RegisterUser;
using LMS.Application.Features.Users.Profile;
using LMS.Application.Interfaces.Authentication;
using LMS.Application.Interfaces.Categories;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.LessonProgresses;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Interfaces.Quizzes;
using LMS.Application.Interfaces.Users;
using LMS.Infrastructure.Identity;
using LMS.Infrastructure.Persistence;
using LMS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;





var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc", LogLevel.Warning);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account-recovery", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("organization-administration", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});

builder.Services.AddScoped<LMS.Application.Features.Organizations.OrganizationContext>();
builder.Services.AddScoped<LMS.Application.Interfaces.Organizations.IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<LMS.Application.Features.Organizations.OrganizationHandlers>();

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<LMS.Application.Features.Assignments.IAssignmentRepository, AssignmentRepository>();
builder.Services.AddScoped<LMS.Application.Features.Assignments.AssignmentHandlers>();
builder.Services.AddScoped<LMS.Application.Features.Reporting.IReportingRepository, ReportingRepository>();
builder.Services.AddScoped<LMS.Application.Features.Reporting.ReportingHandlers>();
builder.Services.AddSingleton<LMS.Application.Features.Assignments.IAssignmentStorage>(_=>new LMS.Infrastructure.Storage.AssignmentStorage(
    builder.Configuration["PrivateStorage:Assignments"] ?? Path.Combine(builder.Environment.ContentRootPath,"App_Data","PrivateAssignments"),
    builder.Environment.WebRootPath??Path.Combine(builder.Environment.ContentRootPath,"wwwroot")));
builder.Services.AddScoped<LMS.Application.Interfaces.QuizAttempts.IQuizAttemptRepository, QuizAttemptRepository>();
builder.Services.AddScoped<LMS.Application.Features.QuizAttempts.StartQuizAttempt.StartQuizAttemptHandler>();
builder.Services.AddScoped<LMS.Application.Features.QuizAttempts.SubmitQuizAttempt.SubmitQuizAttemptHandler>();
builder.Services.AddScoped<LMS.Application.Features.QuizAttempts.GetQuizAttemptResult.GetQuizAttemptResultHandler>();
builder.Services.AddScoped<LMS.Application.Features.QuizAttempts.GetQuizAttemptAnswers.GetQuizAttemptAnswersHandler>();
builder.Services.AddScoped<LMS.Application.Features.QuizAttempts.GetQuizAttempts.GetQuizAttemptsHandler>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; });
builder.Services.AddValidatorsFromAssembly(
    typeof(UpdateUserProfileValidator).Assembly);
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
//builder.Services.AddSwaggerGen(options =>
//{
//    options.AddSecurityDefinition(
//        "Bearer",
//        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
//        {
//            Name = "Authorization",
//            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
//            Scheme = "bearer",
//            BearerFormat = "JWT",
//            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
//            Description =
//                "Enter JWT token. Example: Bearer {token}"
//        });

//    options.AddSecurityRequirement(
//        new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
//        {
//            {
//                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
//                {
//                    Reference =
//                        new Microsoft.OpenApi.Models.OpenApiReference
//                        {
//                            Type =
//                                Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
//                            Id = "Bearer"
//                        }
//                },
//                Array.Empty<string>()
//            }
//        });
//});

builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<LMS.Api.Swagger.OrganizationHeaderOperationFilter>();
    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter JWT token. Example: Bearer {token}"
        });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] =
                new List<string>()
        });
});


builder.Services.AddDbContext<LMSDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString(
            "DefaultConnection")).AddInterceptors(new AuditCommandGuard()));

var protection = builder.Services.AddDataProtection();
if (builder.Configuration["DataProtection:KeyPath"] is string keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "LMS.Api");
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddScoped<IAccountOnboarding, AccountOnboarding>();
builder.Services.AddScoped<LMS.Application.Features.Auditing.AuditRequestContext>();
builder.Services.AddScoped<LMS.Application.Features.Auditing.IAuditWriter>(sp => sp.GetRequiredService<LMSDbContext>());
builder.Services.AddScoped<LMS.Application.Features.Auditing.IAuditObservationWriter, AuditObservationWriter>();
builder.Services.AddScoped<LMS.Application.Features.Auditing.IAuditQueryRepository, AuditQueryRepository>();
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Smoke")) builder.Services.AddSingleton<IAccountEmailSender>(new DevelopmentAccountEmailSender(builder.Configuration["PrivateStorage:Emails"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "PrivateEmails")));
else builder.Services.AddSingleton<IAccountEmailSender, UnconfiguredAccountEmailSender>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<LMSDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey =
            builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT key is not configured.");

        var jwtIssuer =
            builder.Configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT issuer is not configured.");

        var jwtAudience =
            builder.Configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT audience is not configured.");

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var users = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                if (!await JwtSecurityStampValidation.ValidAsync(ctx.Principal, users))
                    ctx.Fail("Fresh login required.");
            }
        };
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),

                ValidateLifetime = true,

                ClockSkew = TimeSpan.Zero
            };
    });

builder.Services.AddScoped<
    IJwtTokenService,
    JwtTokenService>();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<
    IIdentityService,
    IdentityService>();

//builder.Services.AddScoped<IUserRepository, UserRepository>();

//builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

//builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();

builder.Services.AddScoped<
    IUnitOfWork,
    UnitOfWork>();

builder.Services.AddScoped<RegisterUserHandler>();
builder.Services.AddScoped<IUserProfileRepository, UserProfileRepository>();
builder.Services.AddScoped<GetMyProfileHandler>();
builder.Services.AddScoped<UpdateMyProfileHandler>();

builder.Services.AddScoped<
    ICourseRepository,
    CourseRepository>();
builder.Services.AddScoped<LMS.Application.Features.Courses.AssignCourseInstructor.AssignCourseInstructorHandler>();
builder.Services.AddScoped<CreateCourseHandler>();
builder.Services.AddScoped<GetCoursesHandler>();
builder.Services.AddScoped<GetCourseByIdHandler>();
builder.Services.AddScoped<UpdateCourseHandler>();
builder.Services.AddScoped<PublishCourseHandler>();
builder.Services.AddScoped<ArchiveCourseHandler>();

builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<CreateCategoryHandler>();
builder.Services.AddScoped<GetCategoriesHandler>();
builder.Services.AddScoped<GetCategoryByIdHandler>();
builder.Services.AddScoped<UpdateCategoryHandler>();
builder.Services.AddScoped<ActivateCategoryHandler>();
builder.Services.AddScoped<DeactivateCategoryHandler>();


builder.Services.AddScoped<
    ICourseEnrollmentRepository,
    CourseEnrollmentRepository>();
builder.Services.AddScoped<EnrollCourseHandler>();
builder.Services.AddScoped<GetMyEnrollmentsHandler>();
builder.Services.AddScoped<GetEnrollmentByIdHandler>();
builder.Services.AddScoped<CompleteEnrollmentHandler>();
builder.Services.AddScoped<CancelEnrollmentHandler>();

builder.Services.AddScoped<ICourseSectionRepository, CourseSectionRepository>();
builder.Services.AddScoped<CreateCourseSectionHandler>();
builder.Services.AddScoped<GetCourseSectionsHandler>();
builder.Services.AddScoped<GetCourseSectionByIdHandler>();
builder.Services.AddScoped<UpdateCourseSectionHandler>();
builder.Services.AddScoped<DeleteCourseSectionHandler>();

builder.Services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
builder.Services.AddScoped<LMS.Application.Features.LessonContent.MediaAssetHandlers>();
builder.Services.AddSingleton<IPrivateMediaStorage>(_ => new LMS.Infrastructure.Storage.LocalPrivateMediaStorage(
    builder.Configuration["PrivateStorage:Media"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "PrivateMedia"),
    builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot")));
builder.Services.AddScoped<ILessonContentItemRepository, LessonContentItemRepository>();
builder.Services.AddScoped<LMS.Application.Features.LessonContent.LessonContentHandlers>();
builder.Services.AddScoped<ILessonRepository, LessonRepository>();
builder.Services.AddScoped<LMS.Application.Features.Lessons.Common.EnsureLessonOwnership>();
builder.Services.AddScoped<CreateLessonHandler>();
builder.Services.AddScoped<LMS.Application.Features.Lessons.Common.EnsureLessonReadable>();
builder.Services.AddScoped<GetLessonsHandler>();
builder.Services.AddScoped<GetLessonByIdHandler>();
builder.Services.AddScoped<UpdateLessonHandler>();
builder.Services.AddScoped<PublishLessonHandler>();
builder.Services.AddScoped<UnpublishLessonHandler>();
builder.Services.AddScoped<DeleteLessonHandler>();


builder.Services.AddScoped<
    ILessonProgressRepository,
    LessonProgressRepository>();
builder.Services.AddScoped<StartLessonHandler>();
builder.Services.AddScoped<UpdateLessonProgressHandler>();
builder.Services.AddScoped<CompleteLessonHandler>();
builder.Services.AddScoped<GetLessonProgressHandler>();


builder.Services.AddScoped<GetCourseProgressHandler>();
builder.Services.AddScoped<GetMyCoursesProgressHandler>();



builder.Services.AddScoped<IQuizRepository,QuizRepository>();
builder.Services.AddScoped<CreateQuizHandler>();
builder.Services.AddScoped<EnsureQuizReadable>();
builder.Services.AddScoped<GetQuizHandler>();
builder.Services.AddScoped<UpdateQuizHandler>();
builder.Services.AddScoped<PublishQuizHandler>();
builder.Services.AddScoped<UnpublishQuizHandler>();
builder.Services.AddScoped<DeleteQuizHandler>();
builder.Services.AddScoped<EnsureQuizEditable>();
builder.Services.AddScoped<EnsureQuizOwnership>();

builder.Services.AddScoped<IQuizQuestionRepository, QuizQuestionRepository>();
builder.Services.AddScoped<CreateQuestionHandler>();
builder.Services.AddScoped<GetQuestionsHandler>();
builder.Services.AddScoped<GetQuestionByIdHandler>();
builder.Services.AddScoped<UpdateQuestionHandler>();
builder.Services.AddScoped<DeleteQuestionHandler>();

builder.Services.AddScoped<IQuizOptionRepository, QuizOptionRepository>();
builder.Services.AddScoped<CreateOptionHandler>();
builder.Services.AddScoped<GetOptionsHandler>();
builder.Services.AddScoped<GetStudentOptionsHandler>();
builder.Services.AddScoped<UpdateOptionHandler>();
builder.Services.AddScoped<DeleteOptionHandler>();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var roleManager =
        scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    await IdentitySeeder.SeedRolesAsync(roleManager);
}

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseMiddleware<GlobalExceptionMiddleware>();

//var summaries = new[]
//{
//    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
//};

//app.MapGet("/weatherforecast", () =>
//{
//    var forecast =  Enumerable.Range(1, 5).Select(index =>
//        new WeatherForecast
//        (
//            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
//            Random.Shared.Next(-20, 55),
//            summaries[Random.Shared.Next(summaries.Length)]
//        ))
//        .ToArray();
//    return forecast;
//})
//.WithName("GetWeatherForecast");

app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<OrganizationMiddleware>();

app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

//record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
//{
//    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
//}
