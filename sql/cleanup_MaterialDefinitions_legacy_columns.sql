-- ============================================================
-- MaterialDefinitions tablosundan legacy ERP kolonlarini kaldir
-- Calistirmadan once yedek almaniz onerilir.
-- ============================================================

DECLARE @schema NVARCHAR(128) = N'dbo'; -- gerekirse degistirin

-- Helper: kolon varsa dusurmek icin
DECLARE @sql NVARCHAR(MAX) = N'';

-- [IsActive]  (yeni: [is_active])
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'IsActive')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [IsActive];' + CHAR(13);

-- [MaterialCode]
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'MaterialCode')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [MaterialCode];' + CHAR(13);

-- [MaterialName]
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'MaterialName')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [MaterialName];' + CHAR(13);

-- [MaterialDescription]
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'MaterialDescription')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [MaterialDescription];' + CHAR(13);

-- [BaseUnitId]
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'BaseUnitId')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [BaseUnitId];' + CHAR(13);

-- [HasSerialNo]
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'HasSerialNo')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [HasSerialNo];' + CHAR(13);

-- [HasLotNo]
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'HasLotNo')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [HasLotNo];' + CHAR(13);

-- [CreatedDate]
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'CreatedDate')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [CreatedDate];' + CHAR(13);

-- [ModifiedDate]
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[MaterialDefinitions]') AND name = N'ModifiedDate')
    SET @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[MaterialDefinitions] DROP COLUMN [ModifiedDate];' + CHAR(13);

IF LEN(@sql) > 0
BEGIN
    PRINT N'Asagidaki komutlar calistirilacak:';
    PRINT @sql;
    EXEC sp_executesql @sql;
    PRINT N'Legacy kolonlar kaldirildi.';
END
ELSE
    PRINT N'Kaldirilacak legacy kolon bulunamadi, tablo zaten temiz.';
