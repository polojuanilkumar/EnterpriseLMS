-- READ ONLY. Prepared for an authorized operator; not executed by this batch.
-- SQL Server, default dbo schema. Verify the target database/schema first.
-- Run during a quiet window or on a consistent restored copy. Do not use NOLOCK.
-- These two result sets are separate reads; stop enrollment writes for a final
-- pre-migration check so the results cannot change between reads and deployment.

SELECT CourseId, UserId, COUNT_BIG(*) AS EnrollmentCount
FROM dbo.CourseEnrollments
GROUP BY CourseId, UserId
HAVING COUNT_BIG(*) > 1
ORDER BY CourseId, UserId;

WITH EnrollmentCounts AS
(
    SELECT Id, CourseId, UserId, Status, EnrolledAt, CompletedAt,
           ProgressPercentage,
           COUNT_BIG(*) OVER (PARTITION BY CourseId, UserId) AS EnrollmentCount
    FROM dbo.CourseEnrollments
)
SELECT CourseId, UserId, EnrollmentCount, Id, Status,
       EnrolledAt, CompletedAt, ProgressPercentage
FROM EnrollmentCounts
WHERE EnrollmentCount > 1
ORDER BY CourseId, UserId, EnrolledAt, Id;

-- Inventory actual incoming foreign keys before designing any remediation.
-- This does not discover external references or application-only references.
SELECT fk.name AS ForeignKeyName,
       OBJECT_SCHEMA_NAME(fk.parent_object_id) AS ReferencingSchema,
       OBJECT_NAME(fk.parent_object_id) AS ReferencingTable,
       pc.name AS ReferencingColumn,
       rc.name AS ReferencedColumn
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS pc
  ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns AS rc
  ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE fk.referenced_object_id = OBJECT_ID(N'dbo.CourseEnrollments')
ORDER BY ReferencingSchema, ReferencingTable, ForeignKeyName, fkc.constraint_column_id;
