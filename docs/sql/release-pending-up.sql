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

