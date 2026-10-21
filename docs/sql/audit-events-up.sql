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

