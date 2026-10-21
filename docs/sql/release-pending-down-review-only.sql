BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM [AuditEvents]) THROW 51000, 'Audit history must be retained. Downgrade blocked.', 1;

DROP TABLE [AuditEvents];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261006135202_AddAuditEvents';

COMMIT;
GO

BEGIN TRANSACTION;
DROP TABLE [OrganizationInvitations];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261006122454_AddOrganizationInvitations';

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM [AssignmentSubmissions]) OR EXISTS (SELECT 1 FROM [AssignmentGrades]) THROW 51000, 'Assignment rollback is blocked while submitted work or grades exist.', 1;

DROP TABLE [AssignmentGrades];

DROP TABLE [AssignmentSubmissions];

DROP TABLE [LessonAssignments];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004061629_AddLessonAssignments';

COMMIT;
GO

