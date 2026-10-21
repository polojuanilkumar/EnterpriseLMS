BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM [Organizations])
    OR EXISTS (SELECT 1 FROM [OrganizationMemberships])
    OR EXISTS (SELECT 1 FROM [Courses] WHERE [OrganizationId] IS NOT NULL)
    THROW 51000, 'Organization data exists. Review and export it before rolling back the foundation.', 1;

ALTER TABLE [Courses] DROP CONSTRAINT [FK_Courses_Organizations_OrganizationId];

DROP TABLE [OrganizationMemberships];

DROP TABLE [Organizations];

DROP INDEX [IX_Courses_Code] ON [Courses];

DROP INDEX [IX_Courses_OrganizationId_Code] ON [Courses];

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Courses]') AND [c].[name] = N'OrganizationId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [Courses] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [Courses] DROP COLUMN [OrganizationId];

CREATE UNIQUE INDEX [IX_Courses_Code] ON [Courses] ([Code]);

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20260928081231_AddOrganizationFoundation';

COMMIT;
GO

