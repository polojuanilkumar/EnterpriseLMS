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

