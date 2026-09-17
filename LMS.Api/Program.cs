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
using LMS.Application.Features.Quizzes.CreateQuiz;
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

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();
builder.Services.AddControllers();
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
            "DefaultConnection")));

builder.Services.AddDataProtection();

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

builder.Services.AddScoped<ILessonRepository, LessonRepository>();
builder.Services.AddScoped<CreateLessonHandler>();
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

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

//record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
//{
//    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
//}
