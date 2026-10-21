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

