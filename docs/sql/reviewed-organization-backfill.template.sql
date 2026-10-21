-- REVIEWED DEMO BACKFILL. Not executed. Requires the Organization foundation schema.
-- Run the entire batch in a dedicated session with application writes stopped.
-- Mapping approval is recorded in docs/organization-foundation.md; execution is separate.
-- Default is a transactional dry run. Set @Commit = 1 ONLY after execution authorization.
-- No GO separators: TRY/CATCH and the transaction must remain one batch.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Commit bit = 0;
IF @@TRANCOUNT <> 0 THROW 51000, 'Use a dedicated session with no existing transaction.', 1;

DECLARE @DemoId uniqueidentifier;
DECLARE @Student2 uniqueidentifier = '60a4c3be-0e91-47be-a3ac-def38822b6ec';
DECLARE @Instructor uniqueidentifier = '0cf651c4-e112-4111-955a-0f44153700cb';
DECLARE @StudentRole uniqueidentifier, @SuperAdminRole uniqueidentifier;
DECLARE @Courses TABLE (CourseId uniqueidentifier NOT NULL PRIMARY KEY);
INSERT @Courses VALUES
('8d37acf3-2fb2-4eff-915e-a63dbd495ca3'),
('0dea66b2-f17b-42a7-afb7-6ddef2ef8e5a');
DECLARE @Members TABLE (UserId uniqueidentifier NOT NULL PRIMARY KEY, Role int NOT NULL);
INSERT @Members VALUES
('0cf651c4-e112-4111-955a-0f44153700cb', 2), -- Instructor
('2452b8c2-799c-48fa-854c-36ccf00e5bf8', 1), -- Student
('40002941-c2cc-4575-8ef7-afa68bc73929', 3), -- Admin
('e8b90eab-54ac-48e7-b71b-bf778449fb7e', 3); -- Admin
DECLARE @Roles TABLE (Name nvarchar(256), NormalizedName nvarchar(256));
INSERT @Roles VALUES (N'Student', N'STUDENT'), (N'Instructor', N'INSTRUCTOR'),
                     (N'Admin', N'ADMIN'), (N'SuperAdmin', N'SUPERADMIN');

BEGIN TRY
    BEGIN TRANSACTION;
    -- Lock reviewed sets through completion. This is a maintenance-window batch.
    IF EXISTS (SELECT 1 FROM @Members m LEFT JOIN dbo.AspNetUsers u WITH (UPDLOCK, HOLDLOCK)
               ON u.Id = m.UserId WHERE u.Id IS NULL)
       OR NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Student2)
        THROW 51000, 'An expected user ID is missing.', 1;

    -- Four exact role definitions; use existing IDs, never create Identity roles.
    IF (SELECT COUNT_BIG(*) FROM dbo.AspNetRoles WITH (UPDLOCK, HOLDLOCK)) <> 4
       OR EXISTS (SELECT 1 FROM @Roles expected WHERE
           (SELECT COUNT_BIG(*) FROM dbo.AspNetRoles r WITH (UPDLOCK, HOLDLOCK)
            WHERE r.Name COLLATE Latin1_General_100_BIN2 = expected.Name
              AND r.NormalizedName COLLATE Latin1_General_100_BIN2 = expected.NormalizedName) <> 1)
        THROW 51000, 'Expected Student, Instructor, Admin and SuperAdmin role definitions differ.', 1;
    SELECT @StudentRole = Id FROM dbo.AspNetRoles WHERE NormalizedName = N'STUDENT';
    SELECT @SuperAdminRole = Id FROM dbo.AspNetRoles WHERE NormalizedName = N'SUPERADMIN';

    -- Accept the original Student-only state or the completed SuperAdmin-only state.
    IF (SELECT COUNT_BIG(*) FROM dbo.AspNetUserRoles WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @Student2) <> 1
       OR EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WITH (UPDLOCK, HOLDLOCK)
                  WHERE UserId = @Student2 AND RoleId NOT IN (@StudentRole, @SuperAdminRole))
        THROW 51000, 'student2 must have exactly Student or exactly SuperAdmin globally.', 1;

    SELECT @DemoId = Id FROM dbo.Organizations WITH (UPDLOCK, HOLDLOCK) WHERE Code = N'DEMO';
    IF EXISTS (SELECT 1 FROM dbo.Organizations WHERE Id = @DemoId
               AND (Code COLLATE Latin1_General_100_BIN2 <> N'DEMO'
                 OR Name COLLATE Latin1_General_100_BIN2 <> N'Enterprise LMS Demo' OR IsActive <> 1))
        THROW 51000, 'Existing DEMO organization name, code or active state differs.', 1;
    IF @DemoId IS NULL SET @DemoId = NEWID();

    IF (SELECT COUNT_BIG(*) FROM dbo.Courses WITH (UPDLOCK, HOLDLOCK)) <> 2
       OR EXISTS (SELECT 1 FROM @Courses m LEFT JOIN dbo.Courses c WITH (UPDLOCK, HOLDLOCK)
                  ON c.Id = m.CourseId WHERE c.Id IS NULL OR c.InstructorId <> @Instructor
                  OR (c.OrganizationId IS NOT NULL AND c.OrganizationId <> @DemoId))
        THROW 51000, 'Expected exactly the two approved courses, approved instructor and NULL/DEMO ownership.', 1;
    IF EXISTS (SELECT 1 FROM dbo.OrganizationMemberships existing WITH (UPDLOCK, HOLDLOCK)
               LEFT JOIN @Members m ON m.UserId = existing.UserId
               WHERE existing.OrganizationId = @DemoId AND (m.UserId IS NULL OR existing.Role <> m.Role))
        THROW 51000, 'DEMO contains an unexpected member or conflicting role (including student2).', 1;

    -- BEFORE: safe identity fields, all global assignments for the five reviewed users,
    -- course ownership/instructors, and DEMO memberships. Never select credentials.
    SELECT N'BEFORE' AS Phase, Id, Code, Name, IsActive FROM dbo.Organizations WHERE Id = @DemoId;
    SELECT N'BEFORE' AS Phase, c.Id, c.Code, c.Title, c.InstructorId, c.OrganizationId
    FROM dbo.Courses c JOIN @Courses m ON m.CourseId = c.Id;
    SELECT N'BEFORE' AS Phase, u.Id AS UserId, u.UserName, r.Id AS RoleId, r.Name AS GlobalRole
    FROM dbo.AspNetUsers u LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
    LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
    WHERE u.Id IN (SELECT UserId FROM @Members) OR u.Id = @Student2;
    SELECT N'BEFORE' AS Phase, Id, OrganizationId, UserId, Role
    FROM dbo.OrganizationMemberships WHERE OrganizationId = @DemoId;

    IF NOT EXISTS (SELECT 1 FROM dbo.Organizations WHERE Id = @DemoId)
        INSERT dbo.Organizations (Id, Code, Name, IsActive)
        VALUES (@DemoId, N'DEMO', N'Enterprise LMS Demo', 1);

    INSERT dbo.OrganizationMemberships (Id, OrganizationId, UserId, Role)
    SELECT NEWID(), @DemoId, m.UserId, m.Role FROM @Members m
    WHERE NOT EXISTS (SELECT 1 FROM dbo.OrganizationMemberships existing
                      WHERE existing.OrganizationId = @DemoId AND existing.UserId = m.UserId);

    UPDATE c SET OrganizationId = @DemoId
    OUTPUT inserted.Id AS CourseId, deleted.OrganizationId AS PreviousOrganizationId,
           inserted.OrganizationId AS AssignedOrganizationId
    FROM dbo.Courses c JOIN @Courses m ON m.CourseId = c.Id
    WHERE c.OrganizationId IS NULL;

    DELETE FROM dbo.AspNetUserRoles WHERE UserId = @Student2 AND RoleId = @StudentRole;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @Student2 AND RoleId = @SuperAdminRole)
        INSERT dbo.AspNetUserRoles (UserId, RoleId) VALUES (@Student2, @SuperAdminRole);

    -- AFTER assertions precede the commit switch; repeated successful runs are no-ops.
    IF (SELECT COUNT_BIG(*) FROM dbo.Courses WHERE OrganizationId = @DemoId) <> 2
       OR EXISTS (SELECT 1 FROM @Courses m LEFT JOIN dbo.Courses c ON c.Id = m.CourseId
                  WHERE c.Id IS NULL OR c.OrganizationId IS NULL OR c.OrganizationId <> @DemoId
                     OR c.InstructorId <> @Instructor)
        THROW 51000, 'Course postcondition failed.', 1;
    IF (SELECT COUNT_BIG(*) FROM dbo.OrganizationMemberships WHERE OrganizationId = @DemoId) <> 4
       OR EXISTS (SELECT 1 FROM @Members m LEFT JOIN dbo.OrganizationMemberships existing
                  ON existing.OrganizationId = @DemoId AND existing.UserId = m.UserId
                  WHERE existing.Id IS NULL OR existing.Role <> m.Role)
        THROW 51000, 'DEMO membership postcondition failed.', 1;
    IF (SELECT COUNT_BIG(*) FROM dbo.AspNetUserRoles WHERE UserId = @Student2) <> 1
       OR NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @Student2 AND RoleId = @SuperAdminRole)
       OR EXISTS (SELECT 1 FROM dbo.OrganizationMemberships WHERE OrganizationId = @DemoId AND UserId = @Student2)
        THROW 51000, 'student2 postcondition failed.', 1;

    SELECT N'AFTER (inside transaction)' AS Phase, Id, Code, Name, IsActive FROM dbo.Organizations WHERE Id = @DemoId;
    SELECT N'AFTER (inside transaction)' AS Phase, c.Id, c.Code, c.Title, c.InstructorId, c.OrganizationId
    FROM dbo.Courses c JOIN @Courses m ON m.CourseId = c.Id;
    SELECT N'AFTER (inside transaction)' AS Phase, u.Id AS UserId, u.UserName, r.Id AS RoleId, r.Name AS GlobalRole
    FROM dbo.AspNetUsers u LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
    LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
    WHERE u.Id IN (SELECT UserId FROM @Members) OR u.Id = @Student2;
    SELECT N'AFTER (inside transaction)' AS Phase, Id, OrganizationId, UserId, Role
    FROM dbo.OrganizationMemberships WHERE OrganizationId = @DemoId;
    -- Read-only access-gap report: history is preserved even without learner membership.
    SELECT e.Id AS EnrollmentIdWithoutStudentMembership, e.CourseId, e.UserId, c.OrganizationId
    FROM dbo.CourseEnrollments e JOIN dbo.Courses c ON c.Id = e.CourseId
    WHERE c.OrganizationId = @DemoId AND NOT EXISTS
        (SELECT 1 FROM dbo.OrganizationMemberships m
         WHERE m.OrganizationId = c.OrganizationId AND m.UserId = e.UserId AND m.Role = 1);

    IF @Commit = 1
    BEGIN
        COMMIT TRANSACTION;
        SELECT N'COMMITTED' AS Outcome;
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        SELECT N'DRY RUN: all backfill writes rolled back' AS Outcome;
    END;
END TRY
BEGIN CATCH
    -- Handles both committable and doomed transactions; rollback all writes, then
    -- rethrow the original error. Never commit a partially completed backfill.
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
-- A compile error/client cancellation may bypass CATCH. In that same dedicated
-- session run IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION; or disconnect the session.
-- AFTER result sets above describe tentative state, not proof of a commit.
