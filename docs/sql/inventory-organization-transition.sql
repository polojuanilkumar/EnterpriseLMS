-- READ ONLY. Do not execute without authorization for the intended database.
-- Compatible with the pre-Organization schema and the additive foundation schema.
-- No password hashes, security stamps, tokens, or connection strings are selected.

SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;

SELECT c.Id AS CourseId, c.Code, c.Title, c.InstructorId, c.Status, c.IsPublished,
       (SELECT COUNT_BIG(*) FROM dbo.CourseEnrollments e WHERE e.CourseId = c.Id) AS EnrollmentCount,
       (SELECT COUNT_BIG(*) FROM dbo.CourseSections s WHERE s.CourseId = c.Id) AS SectionCount
FROM dbo.Courses c ORDER BY c.Code, c.Id;

SELECT Code, COUNT_BIG(*) AS CourseCount
FROM dbo.Courses GROUP BY Code HAVING COUNT_BIG(*) > 1;

-- Identity role assignments are evidence for human review, NOT membership assignments.
SELECT u.Id AS UserId, u.UserName, u.EmailConfirmed, u.LockoutEnabled, u.LockoutEnd,
       r.Id AS GlobalRoleId, r.Name AS GlobalRole, r.NormalizedName
FROM dbo.AspNetUsers u
LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
ORDER BY u.Id, r.NormalizedName;

SELECT r.Id, r.Name, r.NormalizedName, COUNT_BIG(ur.UserId) AS AssignmentCount
FROM dbo.AspNetRoles r LEFT JOIN dbo.AspNetUserRoles ur ON ur.RoleId = r.Id
GROUP BY r.Id, r.Name, r.NormalizedName ORDER BY r.NormalizedName;

-- Historic relationships guide review; neither proves an organization role.
SELECT e.Id AS EnrollmentId, e.CourseId, e.UserId, e.Status, e.EnrolledAt,
       e.CompletedAt, e.ProgressPercentage
FROM dbo.CourseEnrollments e ORDER BY e.CourseId, e.UserId, e.Id;

SELECT c.Id AS CourseId, c.InstructorId AS MissingInstructorId
FROM dbo.Courses c LEFT JOIN dbo.AspNetUsers u ON u.Id = c.InstructorId WHERE u.Id IS NULL;
SELECT e.Id AS EnrollmentId, e.CourseId, e.UserId
FROM dbo.CourseEnrollments e
LEFT JOIN dbo.Courses c ON c.Id = e.CourseId
LEFT JOIN dbo.AspNetUsers u ON u.Id = e.UserId
WHERE c.Id IS NULL OR u.Id IS NULL;

SELECT 'Courses' AS Entity, COUNT_BIG(*) AS RowCount FROM dbo.Courses
UNION ALL SELECT 'CourseEnrollments', COUNT_BIG(*) FROM dbo.CourseEnrollments
UNION ALL SELECT 'LessonProgresses', COUNT_BIG(*) FROM dbo.LessonProgresses
UNION ALL SELECT 'QuizAttempts', COUNT_BIG(*) FROM dbo.QuizAttempts
UNION ALL SELECT 'QuizAttemptAnswers', COUNT_BIG(*) FROM dbo.QuizAttemptAnswers
UNION ALL SELECT 'QuizAttemptAnswerOptions', COUNT_BIG(*) FROM dbo.QuizAttemptAnswerOptions;

SELECT i.name, i.is_unique, i.filter_definition, c.name AS ColumnName, ic.key_ordinal
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(N'dbo.Courses') AND ic.key_ordinal > 0
ORDER BY i.name, ic.key_ordinal;

-- Dynamic SELECT only: avoid binding columns/tables absent before foundation deployment.
IF COL_LENGTH(N'dbo.Courses', N'OrganizationId') IS NOT NULL
    EXEC(N'SELECT Id AS CourseId, Code, InstructorId, OrganizationId FROM dbo.Courses ORDER BY OrganizationId, Code;');
IF OBJECT_ID(N'dbo.Organizations', N'U') IS NOT NULL
    EXEC(N'SELECT Id, Code, Name, IsActive FROM dbo.Organizations ORDER BY Code;');
IF OBJECT_ID(N'dbo.OrganizationMemberships', N'U') IS NOT NULL
    EXEC(N'SELECT Id, OrganizationId, UserId, Role FROM dbo.OrganizationMemberships ORDER BY OrganizationId, UserId;');
