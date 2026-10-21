-- REVIEW ONLY. No connection was opened and this script was not executed.
-- An operator must select/verify the intended environment independently before running.
-- Run the history query first; run later queries only AFTER the corresponding tables exist.
-- Results contain only schema identifiers, internal IDs and aggregates, never secrets/text/emails.
SELECT [MigrationId], [ProductVersion] FROM [__EFMigrationsHistory] ORDER BY [MigrationId];

SELECT COUNT_BIG(*) AS UnassignedLegacyCourses FROM [Courses] WHERE [OrganizationId] IS NULL;
SELECT [OrganizationId], [Code], COUNT_BIG(*) AS DuplicateCount
FROM [Courses] GROUP BY [OrganizationId], [Code] HAVING COUNT_BIG(*) > 1;
SELECT [CourseId], [UserId], COUNT_BIG(*) AS DuplicateCount
FROM [CourseEnrollments] GROUP BY [CourseId], [UserId] HAVING COUNT_BIG(*) > 1;
SELECT [QuizId], [UserId], COUNT_BIG(*) AS DuplicateStartedAttempts
FROM [QuizAttempts] WHERE [Status] = 1 GROUP BY [QuizId], [UserId] HAVING COUNT_BIG(*) > 1;

-- OrganizationRole: Student=1, Instructor=2, Admin=3. Enrollment Active=1.
SELECT c.[Id] AS CourseId, c.[OrganizationId], c.[InstructorId]
FROM [Courses] c LEFT JOIN [OrganizationMemberships] m
 ON m.[OrganizationId] = c.[OrganizationId] AND m.[UserId] = c.[InstructorId] AND m.[Role] = 2
WHERE c.[OrganizationId] IS NOT NULL AND m.[Id] IS NULL;
SELECT e.[Id] AS EnrollmentId, c.[OrganizationId], e.[UserId]
FROM [CourseEnrollments] e JOIN [Courses] c ON c.[Id] = e.[CourseId]
LEFT JOIN [OrganizationMemberships] m
 ON m.[OrganizationId] = c.[OrganizationId] AND m.[UserId] = e.[UserId] AND m.[Role] = 1
WHERE c.[OrganizationId] IS NOT NULL AND e.[Status] = 1 AND m.[Id] IS NULL;
SELECT [OrganizationId], COUNT_BIG(*) AS AdminCount
FROM [OrganizationMemberships] WHERE [Role] = 3 GROUP BY [OrganizationId];

SELECT [AssignmentId], [UserId], COUNT_BIG(*) AS DuplicateCount
FROM [AssignmentSubmissions] GROUP BY [AssignmentId], [UserId] HAVING COUNT_BIG(*) > 1;
SELECT [SubmissionId], COUNT_BIG(*) AS DuplicateCount
FROM [AssignmentGrades] GROUP BY [SubmissionId] HAVING COUNT_BIG(*) > 1;
SELECT COUNT_BIG(*) AS InvalidGrades
FROM [AssignmentGrades] g JOIN [AssignmentSubmissions] s ON s.[Id] = g.[SubmissionId]
WHERE g.[Marks] < 0 OR g.[Marks] > s.[MaximumMarksSnapshot];
SELECT COUNT_BIG(*) AS PendingInvitations, SUM(CASE WHEN [DeliveredAt] IS NULL THEN 1 ELSE 0 END) AS DeliveryUnconfirmed
FROM [OrganizationInvitations] WHERE [Pending] = 1;
SELECT [OrganizationId], COUNT_BIG(*) AS DuplicatePendingGroups
FROM (SELECT [OrganizationId], [NormalizedEmail] FROM [OrganizationInvitations]
 WHERE [Pending] = 1 GROUP BY [OrganizationId], [NormalizedEmail] HAVING COUNT_BIG(*) > 1) duplicates
GROUP BY [OrganizationId];
SELECT COUNT_BIG(*) AS InvalidAuditScopeOrMetadata FROM [AuditEvents]
WHERE ([Scope] = 'organization' AND [OrganizationId] IS NULL)
 OR ([Scope] IN ('platform','account') AND [OrganizationId] IS NOT NULL)
 OR [Scope] NOT IN ('organization','platform','account') OR ISJSON([Metadata]) <> 1 OR DATALENGTH([Metadata]) > 1024;
SELECT [OperationId], [Action], [ResourceType], [ResourceId], COUNT_BIG(*) AS DuplicateCount
FROM [AuditEvents] GROUP BY [OperationId], [Action], [ResourceType], [ResourceId] HAVING COUNT_BIG(*) > 1;

SELECT t.[name] AS TableName, i.[name] AS IndexName, i.[is_unique], i.[filter_definition]
FROM sys.indexes i JOIN sys.tables t ON t.[object_id] = i.[object_id]
WHERE t.[name] IN ('LessonAssignments','AssignmentSubmissions','AssignmentGrades','OrganizationInvitations','AuditEvents')
AND i.[name] IS NOT NULL ORDER BY t.[name], i.[name];
