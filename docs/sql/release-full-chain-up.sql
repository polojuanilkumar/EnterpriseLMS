IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [Users] (
    [Id] uniqueidentifier NOT NULL,
    [FirstName] nvarchar(100) NOT NULL,
    [LastName] nvarchar(100) NOT NULL,
    [Email] nvarchar(255) NOT NULL,
    [PasswordHash] nvarchar(500) NOT NULL,
    [Role] int NOT NULL,
    [Status] int NOT NULL,
    [LastLoginOn] datetime2 NULL,
    [CreatedOn] datetime2 NOT NULL,
    [ModifiedOn] datetime2 NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);

CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260820115608_InitialCreate', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] uniqueidentifier NOT NULL,
    [FirstName] nvarchar(max) NOT NULL,
    [LastName] nvarchar(max) NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] uniqueidentifier NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] uniqueidentifier NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] uniqueidentifier NOT NULL,
    [RoleId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] uniqueidentifier NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260821103457_AddIdentity', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [UserProfiles] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [PhoneNumber] nvarchar(20) NULL,
    [EmployeeCode] nvarchar(50) NULL,
    [Department] nvarchar(100) NULL,
    [Designation] nvarchar(100) NULL,
    [ProfileImageUrl] nvarchar(500) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_UserProfiles] PRIMARY KEY ([Id])
);

CREATE UNIQUE INDEX [IX_UserProfiles_UserId] ON [UserProfiles] ([UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260822074254_AddUserProfile', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [Courses] (
    [Id] uniqueidentifier NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Code] nvarchar(50) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [CategoryId] uniqueidentifier NULL,
    [InstructorId] uniqueidentifier NOT NULL,
    [ThumbnailUrl] nvarchar(500) NULL,
    [Status] int NOT NULL,
    [Level] int NOT NULL,
    [DurationInMinutes] int NOT NULL,
    [IsPublished] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_Courses] PRIMARY KEY ([Id])
);

CREATE UNIQUE INDEX [IX_Courses_Code] ON [Courses] ([Code]);

CREATE INDEX [IX_Courses_InstructorId] ON [Courses] ([InstructorId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260827172959_AddCourse', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [Categories] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260903093444_AddCategory', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [CourseEnrollments] (
    [Id] uniqueidentifier NOT NULL,
    [CourseId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [EnrolledAt] datetime2 NOT NULL,
    [CompletedAt] datetime2 NULL,
    [Status] int NOT NULL,
    [ProgressPercentage] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_CourseEnrollments] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_Courses_CategoryId] ON [Courses] ([CategoryId]);

ALTER TABLE [Courses] ADD CONSTRAINT [FK_Courses_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260907165900_AddCourseEnrollment', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [CourseSections] (
    [Id] uniqueidentifier NOT NULL,
    [CourseId] uniqueidentifier NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [DisplayOrder] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_CourseSections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CourseSections_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_CourseSections_CourseId_DisplayOrder] ON [CourseSections] ([CourseId], [DisplayOrder]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909192412_AddCourseSection', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [Lessons] (
    [Id] uniqueidentifier NOT NULL,
    [SectionId] uniqueidentifier NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [LessonType] int NOT NULL,
    [Content] nvarchar(max) NULL,
    [VideoUrl] nvarchar(1000) NULL,
    [DurationInMinutes] int NOT NULL,
    [DisplayOrder] int NOT NULL,
    [IsPublished] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_Lessons] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Lessons_CourseSections_SectionId] FOREIGN KEY ([SectionId]) REFERENCES [CourseSections] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_Lessons_SectionId_DisplayOrder] ON [Lessons] ([SectionId], [DisplayOrder]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260910093647_AddLesson', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [LessonProgresses] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [LessonId] uniqueidentifier NOT NULL,
    [IsCompleted] bit NOT NULL,
    [CompletedAt] datetime2 NULL,
    [StartedAt] datetime2 NOT NULL,
    [ProgressPercentage] decimal(5,2) NOT NULL,
    [LastAccessedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_LessonProgresses] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LessonProgresses_Lessons_LessonId] FOREIGN KEY ([LessonId]) REFERENCES [Lessons] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_LessonProgresses_LessonId] ON [LessonProgresses] ([LessonId]);

CREATE UNIQUE INDEX [IX_LessonProgresses_UserId_LessonId] ON [LessonProgresses] ([UserId], [LessonId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260911074628_AddLessonProgress', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [Quizzes] (
    [Id] uniqueidentifier NOT NULL,
    [LessonId] uniqueidentifier NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [PassingPercentage] decimal(5,2) NOT NULL,
    [TimeLimitInMinutes] int NOT NULL,
    [MaxAttempts] int NOT NULL,
    [IsPublished] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_Quizzes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Quizzes_Lessons_LessonId] FOREIGN KEY ([LessonId]) REFERENCES [Lessons] ([Id]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_Quizzes_LessonId] ON [Quizzes] ([LessonId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260917133814_AddQuiz', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [QuizQuestions] (
    [Id] uniqueidentifier NOT NULL,
    [QuizId] uniqueidentifier NOT NULL,
    [QuestionText] nvarchar(2000) NOT NULL,
    [QuestionType] int NOT NULL,
    [DisplayOrder] int NOT NULL,
    [Marks] decimal(5,2) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_QuizQuestions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuizQuestions_Quizzes_QuizId] FOREIGN KEY ([QuizId]) REFERENCES [Quizzes] ([Id]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_QuizQuestions_QuizId_DisplayOrder] ON [QuizQuestions] ([QuizId], [DisplayOrder]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260920125610_AddQuizQuestion', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [QuizOptions] (
    [Id] uniqueidentifier NOT NULL,
    [QuestionId] uniqueidentifier NOT NULL,
    [OptionText] nvarchar(1000) NOT NULL,
    [IsCorrect] bit NOT NULL,
    [DisplayOrder] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_QuizOptions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuizOptions_QuizQuestions_QuestionId] FOREIGN KEY ([QuestionId]) REFERENCES [QuizQuestions] ([Id]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_QuizOptions_QuestionId_DisplayOrder] ON [QuizOptions] ([QuestionId], [DisplayOrder]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260923060208_AddQuizOption', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [QuizAttempts] (
    [Id] uniqueidentifier NOT NULL,
    [QuizId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [AttemptNumber] int NOT NULL,
    [StartedAt] datetime2 NOT NULL,
    [Deadline] datetime2 NOT NULL,
    [SubmittedAt] datetime2 NULL,
    [EarnedMarks] decimal(18,2) NULL,
    [PossibleMarks] decimal(18,2) NOT NULL,
    [PassingPercentageSnapshot] decimal(5,2) NOT NULL,
    [IsPassed] bit NULL,
    [Status] int NOT NULL,
    CONSTRAINT [PK_QuizAttempts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuizAttempts_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_QuizAttempts_Quizzes_QuizId] FOREIGN KEY ([QuizId]) REFERENCES [Quizzes] ([Id])
);

CREATE TABLE [QuizAttemptAnswers] (
    [Id] uniqueidentifier NOT NULL,
    [AttemptId] uniqueidentifier NOT NULL,
    [QuestionId] uniqueidentifier NOT NULL,
    [QuestionTextSnapshot] nvarchar(2000) NOT NULL,
    [QuestionTypeSnapshot] int NOT NULL,
    [MarksSnapshot] decimal(5,2) NOT NULL,
    [AwardedMarks] decimal(5,2) NOT NULL,
    [IsCorrect] bit NOT NULL,
    CONSTRAINT [PK_QuizAttemptAnswers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuizAttemptAnswers_QuizAttempts_AttemptId] FOREIGN KEY ([AttemptId]) REFERENCES [QuizAttempts] ([Id]),
    CONSTRAINT [FK_QuizAttemptAnswers_QuizQuestions_QuestionId] FOREIGN KEY ([QuestionId]) REFERENCES [QuizQuestions] ([Id])
);

CREATE TABLE [QuizAttemptAnswerOptions] (
    [Id] uniqueidentifier NOT NULL,
    [AnswerId] uniqueidentifier NOT NULL,
    [OptionId] uniqueidentifier NOT NULL,
    [OptionTextSnapshot] nvarchar(1000) NOT NULL,
    CONSTRAINT [PK_QuizAttemptAnswerOptions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuizAttemptAnswerOptions_QuizAttemptAnswers_AnswerId] FOREIGN KEY ([AnswerId]) REFERENCES [QuizAttemptAnswers] ([Id]),
    CONSTRAINT [FK_QuizAttemptAnswerOptions_QuizOptions_OptionId] FOREIGN KEY ([OptionId]) REFERENCES [QuizOptions] ([Id])
);

CREATE UNIQUE INDEX [IX_QuizAttemptAnswerOptions_AnswerId_OptionId] ON [QuizAttemptAnswerOptions] ([AnswerId], [OptionId]);

CREATE INDEX [IX_QuizAttemptAnswerOptions_OptionId] ON [QuizAttemptAnswerOptions] ([OptionId]);

CREATE UNIQUE INDEX [IX_QuizAttemptAnswers_AttemptId_QuestionId] ON [QuizAttemptAnswers] ([AttemptId], [QuestionId]);

CREATE INDEX [IX_QuizAttemptAnswers_QuestionId] ON [QuizAttemptAnswers] ([QuestionId]);

CREATE UNIQUE INDEX [IX_QuizAttempts_QuizId_UserId_AttemptNumber] ON [QuizAttempts] ([QuizId], [UserId], [AttemptNumber]);

CREATE INDEX [IX_QuizAttempts_UserId] ON [QuizAttempts] ([UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260924134637_AddQuizAttempts', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE UNIQUE INDEX [IX_QuizAttempts_QuizId_UserId] ON [QuizAttempts] ([QuizId], [UserId]) WHERE [Status] = 1;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260924145638_EnforceSingleStartedQuizAttempt', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE UNIQUE INDEX [IX_CourseEnrollments_CourseId_UserId] ON [CourseEnrollments] ([CourseId], [UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260927040119_EnforceUniqueCourseEnrollment', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
DROP INDEX [IX_Courses_Code] ON [Courses];

ALTER TABLE [Courses] ADD [OrganizationId] uniqueidentifier NULL;

CREATE TABLE [Organizations] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Organizations] PRIMARY KEY ([Id])
);

CREATE TABLE [OrganizationMemberships] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Role] int NOT NULL,
    CONSTRAINT [PK_OrganizationMemberships] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_OrganizationMemberships_Role] CHECK ([Role] IN (1, 2, 3)),
    CONSTRAINT [FK_OrganizationMemberships_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrganizationMemberships_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_Courses_Code] ON [Courses] ([Code]) WHERE [OrganizationId] IS NULL;

CREATE UNIQUE INDEX [IX_Courses_OrganizationId_Code] ON [Courses] ([OrganizationId], [Code]) WHERE [OrganizationId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_OrganizationMemberships_OrganizationId_UserId] ON [OrganizationMemberships] ([OrganizationId], [UserId]);

CREATE INDEX [IX_OrganizationMemberships_UserId] ON [OrganizationMemberships] ([UserId]);

CREATE UNIQUE INDEX [IX_Organizations_Code] ON [Organizations] ([Code]);

ALTER TABLE [Courses] ADD CONSTRAINT [FK_Courses_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260928081231_AddOrganizationFoundation', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [LessonContentItems] (
    [Id] uniqueidentifier NOT NULL,
    [LessonId] uniqueidentifier NOT NULL,
    [Type] int NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [DisplayOrder] int NOT NULL,
    [IsRequired] bit NOT NULL,
    [Text] nvarchar(max) NULL,
    [YouTubeVideoId] nvarchar(11) NULL,
    CONSTRAINT [PK_LessonContentItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_LessonContentItems_Order] CHECK ([DisplayOrder] >= 0),
    CONSTRAINT [CK_LessonContentItems_Type] CHECK (([Type] = 1 AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL)),
    CONSTRAINT [FK_LessonContentItems_Lessons_LessonId] FOREIGN KEY ([LessonId]) REFERENCES [Lessons] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_LessonContentItems_LessonId_DisplayOrder] ON [LessonContentItems] ([LessonId], [DisplayOrder]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260929151614_AddLessonContentItems', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [LessonContentItems] DROP CONSTRAINT [CK_LessonContentItems_Type];

ALTER TABLE [LessonContentItems] ADD [AssetId] uniqueidentifier NULL;

CREATE TABLE [MediaAssets] (
    [Id] uniqueidentifier NOT NULL,
    [LessonId] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [Type] int NOT NULL,
    [Size] bigint NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_MediaAssets] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_MediaAssets_TypeSize] CHECK (([Type] = 3 AND [Size] > 0 AND [Size] <= 26214400) OR ([Type] = 4 AND [Size] > 0 AND [Size] <= 209715200)),
    CONSTRAINT [FK_MediaAssets_Lessons_LessonId] FOREIGN KEY ([LessonId]) REFERENCES [Lessons] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MediaAssets_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_LessonContentItems_AssetId] ON [LessonContentItems] ([AssetId]);

ALTER TABLE [LessonContentItems] ADD CONSTRAINT [CK_LessonContentItems_Type] CHECK (([Type] = 1 AND [AssetId] IS NULL AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [AssetId] IS NULL AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL) OR ([Type] IN (3, 4) AND [AssetId] IS NOT NULL AND [Text] IS NULL AND [YouTubeVideoId] IS NULL));

CREATE INDEX [IX_MediaAssets_LessonId] ON [MediaAssets] ([LessonId]);

CREATE INDEX [IX_MediaAssets_OrganizationId] ON [MediaAssets] ([OrganizationId]);

ALTER TABLE [LessonContentItems] ADD CONSTRAINT [FK_LessonContentItems_MediaAssets_AssetId] FOREIGN KEY ([AssetId]) REFERENCES [MediaAssets] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260929160637_AddPrivateLessonMedia', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [MediaAssets] DROP CONSTRAINT [CK_MediaAssets_TypeSize];

ALTER TABLE [LessonContentItems] DROP CONSTRAINT [CK_LessonContentItems_Type];

ALTER TABLE [MediaAssets] ADD CONSTRAINT [CK_MediaAssets_TypeSize] CHECK (([Type] = 3 AND [Size] > 0 AND [Size] <= 26214400) OR ([Type] = 4 AND [Size] > 0 AND [Size] <= 209715200) OR ([Type] = 5 AND [Size] > 0 AND [Size] <= 26214400));

ALTER TABLE [LessonContentItems] ADD CONSTRAINT [CK_LessonContentItems_Type] CHECK (([Type] = 1 AND [AssetId] IS NULL AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [AssetId] IS NULL AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL) OR ([Type] IN (3, 4, 5) AND [AssetId] IS NOT NULL AND [Text] IS NULL AND [YouTubeVideoId] IS NULL));

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260929175839_AddPdfPresentations', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [LessonAssignments] (
    [Id] uniqueidentifier NOT NULL,
    [LessonId] uniqueidentifier NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Instructions] nvarchar(max) NOT NULL,
    [DueAtUtc] datetime2 NULL,
    [MaximumMarks] decimal(18,2) NOT NULL,
    [IsPublished] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_LessonAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_LessonAssignments_MaximumMarks] CHECK ([MaximumMarks] > 0),
    CONSTRAINT [FK_LessonAssignments_Lessons_LessonId] FOREIGN KEY ([LessonId]) REFERENCES [Lessons] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [AssignmentSubmissions] (
    [Id] uniqueidentifier NOT NULL,
    [AssignmentId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Text] nvarchar(max) NULL,
    [AttachmentId] uniqueidentifier NULL,
    [Filename] nvarchar(200) NULL,
    [AttachmentSize] bigint NULL,
    [SubmittedAt] datetime2 NOT NULL,
    [InstructionsSnapshot] nvarchar(max) NOT NULL,
    [MaximumMarksSnapshot] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_AssignmentSubmissions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_AssignmentSubmissions_Attachment] CHECK (([AttachmentId] IS NULL AND [Filename] IS NULL AND [AttachmentSize] IS NULL) OR ([AttachmentId] IS NOT NULL AND [Filename] IS NOT NULL AND [AttachmentSize] IS NOT NULL AND [AttachmentSize] > 0 AND [AttachmentSize] <= 26214400)),
    CONSTRAINT [CK_AssignmentSubmissions_Content] CHECK (([Text] IS NOT NULL AND LEN(LTRIM(RTRIM([Text]))) > 0) OR [AttachmentId] IS NOT NULL),
    CONSTRAINT [CK_AssignmentSubmissions_MaximumMarks] CHECK ([MaximumMarksSnapshot] > 0),
    CONSTRAINT [FK_AssignmentSubmissions_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssignmentSubmissions_LessonAssignments_AssignmentId] FOREIGN KEY ([AssignmentId]) REFERENCES [LessonAssignments] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [AssignmentGrades] (
    [Id] uniqueidentifier NOT NULL,
    [SubmissionId] uniqueidentifier NOT NULL,
    [Marks] decimal(18,2) NOT NULL,
    [Feedback] nvarchar(max) NOT NULL,
    [GraderId] uniqueidentifier NOT NULL,
    [GradedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AssignmentGrades] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_AssignmentGrades_Marks] CHECK ([Marks] >= 0),
    CONSTRAINT [FK_AssignmentGrades_AspNetUsers_GraderId] FOREIGN KEY ([GraderId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssignmentGrades_AssignmentSubmissions_SubmissionId] FOREIGN KEY ([SubmissionId]) REFERENCES [AssignmentSubmissions] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_AssignmentGrades_GraderId] ON [AssignmentGrades] ([GraderId]);

CREATE UNIQUE INDEX [IX_AssignmentGrades_SubmissionId] ON [AssignmentGrades] ([SubmissionId]);

CREATE UNIQUE INDEX [IX_AssignmentSubmissions_AssignmentId_UserId] ON [AssignmentSubmissions] ([AssignmentId], [UserId]);

CREATE UNIQUE INDEX [IX_AssignmentSubmissions_AttachmentId] ON [AssignmentSubmissions] ([AttachmentId]) WHERE [AttachmentId] IS NOT NULL;

CREATE INDEX [IX_AssignmentSubmissions_UserId] ON [AssignmentSubmissions] ([UserId]);

CREATE INDEX [IX_LessonAssignments_LessonId] ON [LessonAssignments] ([LessonId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261004061629_AddLessonAssignments', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [OrganizationInvitations] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [InviterId] uniqueidentifier NOT NULL,
    [Email] nvarchar(256) NOT NULL,
    [NormalizedEmail] nvarchar(256) NOT NULL,
    [Role] int NOT NULL,
    [TokenHash] nvarchar(64) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    [AcceptedAt] datetimeoffset NULL,
    [RevokedAt] datetimeoffset NULL,
    [DeliveredAt] datetimeoffset NULL,
    [Pending] bit NOT NULL,
    [Version] rowversion NOT NULL,
    CONSTRAINT [PK_OrganizationInvitations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrganizationInvitations_AspNetUsers_InviterId] FOREIGN KEY ([InviterId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrganizationInvitations_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_OrganizationInvitations_InviterId] ON [OrganizationInvitations] ([InviterId]);

CREATE UNIQUE INDEX [IX_OrganizationInvitations_OrganizationId_NormalizedEmail] ON [OrganizationInvitations] ([OrganizationId], [NormalizedEmail]) WHERE [Pending] = 1;

CREATE UNIQUE INDEX [IX_OrganizationInvitations_TokenHash] ON [OrganizationInvitations] ([TokenHash]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261006122454_AddOrganizationInvitations', N'10.0.11');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [AuditEvents] (
    [Id] uniqueidentifier NOT NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [TimestampUtc] datetimeoffset NOT NULL,
    [ActorUserId] uniqueidentifier NULL,
    [OrganizationId] uniqueidentifier NULL,
    [Scope] nvarchar(20) NOT NULL,
    [Action] nvarchar(64) NOT NULL,
    [ResourceType] nvarchar(32) NOT NULL,
    [ResourceId] uniqueidentifier NOT NULL,
    [Outcome] nvarchar(16) NOT NULL,
    [CorrelationId] nvarchar(32) NOT NULL,
    [Metadata] nvarchar(512) NOT NULL,
    CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_AuditEvents_Scope] CHECK (([Scope] = 'organization' AND [OrganizationId] IS NOT NULL) OR ([Scope] IN ('platform','account') AND [OrganizationId] IS NULL))
);

CREATE INDEX [IX_AuditEvents_ActorUserId_TimestampUtc_Id] ON [AuditEvents] ([ActorUserId], [TimestampUtc], [Id]);

CREATE UNIQUE INDEX [IX_AuditEvents_OperationId_Action_ResourceType_ResourceId] ON [AuditEvents] ([OperationId], [Action], [ResourceType], [ResourceId]);

CREATE INDEX [IX_AuditEvents_ResourceId_TimestampUtc_Id] ON [AuditEvents] ([ResourceId], [TimestampUtc], [Id]);

CREATE INDEX [IX_AuditEvents_Scope_OrganizationId_TimestampUtc_Id] ON [AuditEvents] ([Scope], [OrganizationId], [TimestampUtc], [Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261006135202_AddAuditEvents', N'10.0.11');

COMMIT;
GO

