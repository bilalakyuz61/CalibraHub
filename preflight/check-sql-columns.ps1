# =============================================================================
# CalibraHub preflight -- inline SQL column-name validator (against live schema)
#
# WHY: The C# compiler does not check inline SQL strings. A query may write
# it.[MaterialCode] while the real Items column is Code -> runtime
# "Invalid column name" -> usually lost in a silent catch. On 2026-07-20 this
# happened in FulfillmentLedger (review caught it before deploy).
#
# HOW: extracts alias.[Column] references from code SQL; if a [Column] is
# neither in the live schema (table/view column) NOR in the code's own
# AS-[alias] definitions -> SUSPECT. Everything "known" (schema columns +
# AS aliases + SQL keywords) is whitelisted; unknown bracketed identifiers
# are reported.
#
# Goal: zero false-negatives (never miss an invented column). May have
# false-positives (rare computed alias) -- output is a review list.
#
# RUN: pwsh preflight/check-sql-columns.ps1   (BEFORE deploy)
# =============================================================================
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$srcDir = Join-Path $repo "src"

# -- 1) Live schema: all table/view column names + table/view names -----------
Add-Type -AssemblyName System.Security
$cfgPath = Join-Path $repo "src\CalibraHub.Web\appsettings.json"
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
$enc = $cfg.CalibraDatabase.ConnectionString
if ($enc -like "dpapi:*") {
    $bytes = [Convert]::FromBase64String($enc.Substring(6))
    $plain = [System.Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $cs = [System.Text.Encoding]::UTF8.GetString($plain)
} else { $cs = $enc }
$keep = @()
foreach ($part in ($cs -split ';')) {
    if ($part.Trim() -eq '') { continue }
    $k = ($part -split '=')[0].Trim()
    if ($k -match 'Trust Server Cert|TrustServerCert|Encrypt|Multiple Active') { continue }
    $keep += $part
}
$conn = New-Object System.Data.SqlClient.SqlConnection (($keep -join ';') + ';Encrypt=False')
$conn.Open()
$known = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
$c = $conn.CreateCommand()
$c.CommandText = "SELECT DISTINCT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS"
$r = $c.ExecuteReader(); while ($r.Read()) { [void]$known.Add($r.GetString(0)) }; $r.Close()
$c.CommandText = "SELECT DISTINCT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES UNION SELECT DISTINCT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS"
$r = $c.ExecuteReader(); while ($r.Read()) { [void]$known.Add($r.GetString(0)) }; $r.Close()
$conn.Close()
Write-Output ("Live schema: " + $known.Count + " distinct column/table/view names loaded.")

# -- 2) SQL keywords + the code's own AS-alias definitions -> known set --------
$sqlKeywords = @(
  'SELECT','FROM','WHERE','JOIN','INNER','LEFT','RIGHT','OUTER','ON','AND','OR','AS','GROUP','BY',
  'ORDER','HAVING','UNION','ALL','DISTINCT','TOP','INSERT','INTO','VALUES','UPDATE','SET','DELETE',
  'CASE','WHEN','THEN','ELSE','END','NULL','NOT','IN','EXISTS','ISNULL','COALESCE','SUM','COUNT','MAX',
  'MIN','AVG','CAST','CONVERT','WITH','OVER','PARTITION','ROW_NUMBER','DESC','ASC','LIKE','BETWEEN',
  'CROSS','APPLY','MERGE','OUTPUT','DECLARE','BEGIN','COMMIT','ROLLBACK','TRAN','TRANSACTION','IDENTITY',
  'PRIMARY','KEY','FOREIGN','REFERENCES','CONSTRAINT','INDEX','CREATE','ALTER','DROP','TABLE','VIEW',
  'DATETIME','INT','NVARCHAR','DECIMAL','BIT','BIGINT','TINYINT','SYSUTCDATETIME','GETDATE','NEWID',
  'DATABASE','SCHEMA','OBJECT_ID','TRY_CAST','FORMAT','LEN','TRIM','LOWER','UPPER','SUBSTRING'
)
foreach ($kw in $sqlKeywords) { [void]$known.Add($kw) }

# System catalog columns (sys.* and INFORMATION_SCHEMA.*) are legitimate but not
# in INFORMATION_SCHEMA.COLUMNS, so whitelist them to avoid false positives.
$catalogCols = @(
  # sys.objects / sys.tables / sys.columns / sys.indexes / sys.foreign_keys / sys.types
  'object_id','schema_id','parent_object_id','default_object_id','column_id','index_id',
  'parent_column_id','referenced_object_id','user_type_id','system_type_id','max_length',
  'precision','scale','is_nullable','is_identity','is_computed','delete_referential_action',
  'update_referential_action','type','type_desc','is_unique','is_primary_key','key_ordinal',
  'referenced_column_id','constraint_object_id','constraint_column_id','definition','rows',
  # INFORMATION_SCHEMA.COLUMNS / TABLES
  'COLUMN_NAME','DATA_TYPE','ORDINAL_POSITION','TABLE_SCHEMA','TABLE_NAME','IS_NULLABLE',
  'CHARACTER_MAXIMUM_LENGTH','COLUMN_DEFAULT','NUMERIC_PRECISION','NUMERIC_SCALE','TABLE_TYPE'
)
foreach ($cc in $catalogCols) { [void]$known.Add($cc) }

$csFiles = Get-ChildItem -Path $srcDir -Recurse -Filter *.cs -File
$allText = ($csFiles | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
[regex]::Matches($allText, '(?i)\bAS\s+\[([A-Za-z_][A-Za-z0-9_]*)\]') | ForEach-Object { [void]$known.Add($_.Groups[1].Value) }
[regex]::Matches($allText, '(?i)\bAS\s+([A-Za-z_][A-Za-z0-9_]*)\b')   | ForEach-Object { [void]$known.Add($_.Groups[1].Value) }

# Known table/view names for FROM/JOIN validation (schema-qualified tables only).
$knownTables = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
$conn2 = New-Object System.Data.SqlClient.SqlConnection (($keep -join ';') + ';Encrypt=False'); $conn2.Open()
$c2 = $conn2.CreateCommand()
$c2.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES UNION SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS"
$r2 = $c2.ExecuteReader(); while ($r2.Read()) { [void]$knownTables.Add($r2.GetString(0)) }; $r2.Close(); $conn2.Close()
# sys.* catalog objects referenced via FROM/JOIN are legit — whitelist common ones.
foreach ($sysT in @('columns','tables','objects','indexes','foreign_keys','types','all_columns',
    'schemas','sql_modules','parameters','key_constraints','index_columns','dm_exec_describe_first_result_set',
    'COLUMNS','TABLES','VIEWS','KEY_COLUMN_USAGE','TABLE_CONSTRAINTS',
    'dbo')) { [void]$knownTables.Add($sysT) }   # 'dbo' = schema in 3-part [db].[dbo].[table] refs (regex artifact)

# -- 3) extract alias.[Column] AND FROM/JOIN [.].[Table] refs, report unknowns -
$suspects = @()
$tableSuspects = @()
foreach ($f in $csFiles) {
    $rel = $f.FullName.Substring($repo.Length + 1)
    $ln = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $ln++
        # 3a) alias.[Column]
        foreach ($m in [regex]::Matches($line, '\b[A-Za-z_][A-Za-z0-9_]*\.\[([A-Za-z_][A-Za-z0-9_]*)\]')) {
            $col = $m.Groups[1].Value
            if (-not $known.Contains($col)) {
                $suspects += [pscustomobject]@{ Col = $col; Ref = ($rel + ":" + $ln) }
            }
        }
        # 3b) FROM/JOIN [{s}].[Table] or [schema].[Table] — validate physical table name.
        # ([{s}] interpolation is skipped; only the trailing .[Table] segment is checked.)
        foreach ($m in [regex]::Matches($line, '(?i)\b(?:FROM|JOIN)\s+\[[^\]]+\]\.\[([A-Za-z_][A-Za-z0-9_]*)\]')) {
            $tbl = $m.Groups[1].Value
            if (-not $knownTables.Contains($tbl)) {
                $tableSuspects += [pscustomobject]@{ Tbl = $tbl; Ref = ($rel + ":" + $ln) }
            }
        }
    }
}

Write-Output ""
Write-Output "----------------------------------------------------------"
Write-Output "  SQL COLUMN MISMATCH -- unknown alias.[Column] references"
Write-Output "----------------------------------------------------------"
if ($suspects.Count -eq 0) {
    Write-Output "  CLEAN -- every alias.[Column] is in the schema or an AS-alias."
} else {
    $grp = $suspects | Group-Object Col | Sort-Object Count -Descending
    foreach ($g in $grp) {
        Write-Output ("  [" + $g.Name + "]  (" + $g.Count + " refs) NOT in schema/alias:")
        $g.Group | Select-Object -First 5 | ForEach-Object { Write-Output ("      " + $_.Ref) }
        if ($g.Count -gt 5) { Write-Output ("      ... +" + ($g.Count - 5) + " more") }
    }
    Write-Output ""
    Write-Output ("  " + $grp.Count + " distinct suspect column names. Each is either a real invented column (Invalid column name risk) or a rare computed-alias false positive.")
}
Write-Output ""
Write-Output "----------------------------------------------------------"
Write-Output "  SQL TABLE MISMATCH -- FROM/JOIN [.].[Table] not in schema"
Write-Output "----------------------------------------------------------"
if ($tableSuspects.Count -eq 0) {
    Write-Output "  CLEAN -- every schema-qualified FROM/JOIN table exists."
} else {
    $tgrp = $tableSuspects | Group-Object Tbl | Sort-Object Count -Descending
    foreach ($g in $tgrp) {
        Write-Output ("  [" + $g.Name + "]  (" + $g.Count + " refs) NO SUCH TABLE/VIEW:")
        $g.Group | Select-Object -First 5 | ForEach-Object { Write-Output ("      " + $_.Ref) }
    }
    Write-Output ("  " + $tgrp.Count + " distinct suspect table names (invented table -> Invalid object name).")
}
Write-Output "----------------------------------------------------------"
if ($suspects.Count -gt 0 -or $tableSuspects.Count -gt 0) { exit 1 } else { exit 0 }
