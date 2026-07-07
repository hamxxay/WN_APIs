-- ============================================================
-- WorkNest Schema Discovery
-- Run this FIRST to confirm exact column names.
-- Then verify MissingStoredProcedures.sql matches.
-- ============================================================

SELECT
    t.name  AS TableName,
    c.name  AS ColumnName,
    tp.name AS DataType,
    c.is_nullable,
    c.column_id
FROM sys.tables     t
JOIN sys.columns    c  ON c.object_id  = t.object_id
JOIN sys.types      tp ON tp.user_type_id = c.user_type_id
WHERE t.name IN (
    'WN_Users',
    'WN_Spaces',
    'WN_Bookings',
    'WN_Payments',
    'WN_Memberships',
    'WN_Locations',
    'WN_SpaceTypes',
    'WN_PricingPlans'
)
ORDER BY t.name, c.column_id;
