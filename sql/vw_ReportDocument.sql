/* ═══════════════════════════════════════════════════════════════════════════
   vw_ReportDocument.sql — FastReport dizaynci icin TEK ortak belge view'i

   NOT: Uygulama startup'inda otomatik olarak CalibraDatabaseInitializer
   uzerinden her company DB'ye kurulur (Program.cs — EnsureReportDocumentViewAsync).
   Elle calistirma gerekmez; bu dosya sadece referans/dokumantasyon amaclidir.
   Manuel calistirmak istersen:
       sqlcmd -S <server> -d <company_db> -i sql\vw_ReportDocument.sql

   YAPI:
     Grain = bir satir = bir DocumentLine kaydi. Belge ustu bilgiler her
     satirda tekrarlanir → FastReport GroupHeader ile [BelgeId] grupla.

   KOLON BASLIKLARI (Turkce):
     Belge*     → belge header (BelgeNo, BelgeTarihi, GenelToplam...)
     Cari*      → cari (CariKodu, CariUnvani, CariVergiNo...)
     Temsilci*  → satis temsilcisi
     Sirket*    → kalibrasyon/kurumsal kimlik (master DB'den)
     UstAlan_*  → ust bilgi widget'lari (dinamik, v_Flat_SALES_QUOTE_EDIT)
     Kalem*     → satir (SiraNo, Miktar, BirimFiyat, SatirToplami...)
     Malzeme*   → kalem malzemesi
     Birim*     → olcu birimi
     Lokasyon*  → depo/raf
     KalemAlan_* → satir widget'lari (dinamik, v_Flat_SALES_QUOTE_LINES)

   DINAMIK WIDGET KOLONLARI:
     Admin panelde yeni widget eklendiginde v_Flat_* view'lari otomatik
     yenilenir. Ardindan:
         EXEC [dbo].[sp_Report_RebuildDocumentView];
     komutu ile vw_ReportDocument yeni widget kolonlariyla yeniden uretilir.

   COMPANY (Kalibrasyon):
     Master DB'dedir. 3-parcali isim: [<MasterDbAdi>].[dbo].[Company].
     Uygulama startup'inda connection string'den cozulup gecirilir
     (CalibraDatabaseInitializer.EnsureReportDocumentViewAsync).
     Manuel calistiracaksan asagidaki @MasterDbName'i kendi master DB
     adina gore guncelle.
   ═══════════════════════════════════════════════════════════════════════════ */

SET NOCOUNT ON;
GO


/* ═══════════════════════════════════════════════════════════════════════════
   STORED PROC: sp_Report_RebuildDocumentView
   ═══════════════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE [dbo].[sp_Report_RebuildDocumentView]
AS
BEGIN
    SET NOCOUNT ON;

    /* Master DB adi — kendi kurulumuna gore guncelle. */
    DECLARE @MasterDbName SYSNAME = N'CalibraHub';

    DECLARE @DocCols TABLE (name SYSNAME PRIMARY KEY);
    INSERT INTO @DocCols
    SELECT c.[name]
    FROM sys.columns c
    INNER JOIN sys.objects o ON o.[object_id] = c.[object_id]
    WHERE o.[name] = N'Document' AND o.[schema_id] = SCHEMA_ID(N'dbo');

    DECLARE @LineCols TABLE (name SYSNAME PRIMARY KEY);
    INSERT INTO @LineCols
    SELECT c.[name]
    FROM sys.columns c
    INNER JOIN sys.objects o ON o.[object_id] = c.[object_id]
    WHERE o.[name] = N'DocumentLine' AND o.[schema_id] = SCHEMA_ID(N'dbo');

    /* Ust bilgi widget kolonlari — v_Flat_SALES_QUOTE_EDIT'ten. Alias: UstAlan_* */
    DECLARE @HwColsSql NVARCHAR(MAX) = N'';
    SELECT @HwColsSql = @HwColsSql + N',    hw.[' + REPLACE(c.[name], N']', N']]') + N']'
                      + N' AS [UstAlan_' + REPLACE(c.[name], N']', N']]') + N']' + CHAR(13) + CHAR(10)
    FROM sys.columns c
    INNER JOIN sys.objects o ON o.[object_id] = c.[object_id]
    WHERE o.[schema_id] = SCHEMA_ID(N'dbo')
      AND o.[name]      = N'v_Flat_SALES_QUOTE_EDIT'
      AND o.[type]      = N'V '
      AND c.[name] NOT IN (SELECT name FROM @DocCols);

    /* Kalem widget kolonlari — v_Flat_SALES_QUOTE_LINES'tan. Alias: KalemAlan_* */
    DECLARE @LwColsSql NVARCHAR(MAX) = N'';
    SELECT @LwColsSql = @LwColsSql + N',    lw.[' + REPLACE(c.[name], N']', N']]') + N']'
                      + N' AS [KalemAlan_' + REPLACE(c.[name], N']', N']]') + N']' + CHAR(13) + CHAR(10)
    FROM sys.columns c
    INNER JOIN sys.objects o ON o.[object_id] = c.[object_id]
    WHERE o.[schema_id] = SCHEMA_ID(N'dbo')
      AND o.[name]      = N'v_Flat_SALES_QUOTE_LINES'
      AND o.[type]      = N'V '
      AND c.[name] NOT IN (SELECT name FROM @LineCols);

    DECLARE @HasHwView BIT = CASE WHEN OBJECT_ID(N'dbo.v_Flat_SALES_QUOTE_EDIT', N'V')  IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @HasLwView BIT = CASE WHEN OBJECT_ID(N'dbo.v_Flat_SALES_QUOTE_LINES', N'V') IS NOT NULL THEN 1 ELSE 0 END;

    DECLARE @HwJoin NVARCHAR(MAX) = CASE WHEN @HasHwView = 1
        THEN N'LEFT JOIN [dbo].[v_Flat_SALES_QUOTE_EDIT]  hw ON hw.[id] = d.[id]' + CHAR(13) + CHAR(10)
        ELSE N'' END;
    DECLARE @LwJoin NVARCHAR(MAX) = CASE WHEN @HasLwView = 1
        THEN N'LEFT JOIN [dbo].[v_Flat_SALES_QUOTE_LINES] lw ON lw.[id] = dl.[id]' + CHAR(13) + CHAR(10)
        ELSE N'' END;

    IF @HasHwView = 0 SET @HwColsSql = N'';
    IF @HasLwView = 0 SET @LwColsSql = N'';

    DECLARE @Sql NVARCHAR(MAX) = N'
CREATE OR ALTER VIEW [dbo].[vw_ReportDocument]
AS
SELECT
    d.[id]                                 AS BelgeId,
    d.[document_number]                    AS BelgeNo,
    d.[document_type_id]                   AS BelgeTurId,
    dt.[code]                              AS BelgeTurKodu,
    dt.[name]                              AS BelgeTurAdi,
    d.[company_id]                         AS BelgeSirketId,
    d.[document_date]                      AS BelgeTarihi,
    d.[valid_until]                        AS GecerlilikTarihi,
    d.[currency]                           AS ParaBirimi,
    d.[sub_total]                          AS AraToplam,
    d.[discount_rate]                      AS IskontoOrani,
    d.[discount_amount]                    AS IskontoTutari,
    d.[tax_rate]                           AS KdvOrani,
    d.[tax_amount]                         AS KdvTutari,
    d.[grand_total]                        AS GenelToplam,
    d.[payment_terms]                      AS OdemeKosullari,
    d.[delivery_terms]                     AS TeslimKosullari,
    d.[delivery_address]                   AS TeslimatAdresi,
    d.[status]                             AS BelgeDurumu,
    d.[revision_no]                        AS RevizyonNo,
    d.[notes]                              AS BelgeNotu,
    d.[created_at]                         AS OlusturulmaTarihi,
    d.[updated_at]                         AS GuncellenmeTarihi,

    d.[contact_id]                         AS CariId,
    c.[AccountCode]                        AS CariKodu,
    c.[AccountTitle]                       AS CariUnvani,
    c.[TaxOffice]                          AS CariVergiDairesi,
    c.[TaxNumber]                          AS CariVergiNo,
    c.[Phone]                              AS CariTelefon,
    c.[Mobile]                             AS CariCep,
    c.[Email]                              AS CariEposta,
    c.[Website]                            AS CariWebSitesi,
    c.[Address]                            AS CariAdres,
    c.[PostalCode]                         AS CariPostaKodu,
    c.[City]                               AS CariSehir,
    c.[District]                           AS CariIlce,

    d.[sales_rep_id]                       AS TemsilciId,
    sr.[code]                              AS TemsilciKodu,
    sr.[name]                              AS TemsilciAdi,

    comp.[name]                            AS SirketAdi,
    comp.[title]                           AS SirketUnvani,
    comp.[address]                         AS SirketAdresi,
    comp.[city]                            AS SirketSehir,
    comp.[district]                        AS SirketIlce,
    comp.[postal_code]                     AS SirketPostaKodu,
    comp.[tax_office]                      AS SirketVergiDairesi,
    comp.[tax_number]                      AS SirketVergiNo,
    comp.[is_e_document_approval_enabled]  AS SirketEBelgeAktif,
    CONCAT(comp.[address],
        CASE WHEN comp.[district]    IS NOT NULL THEN N'' / '' + comp.[district]    ELSE N'''' END,
        CASE WHEN comp.[city]        IS NOT NULL THEN N'' / '' + comp.[city]        ELSE N'''' END,
        CASE WHEN comp.[postal_code] IS NOT NULL THEN N'' ''   + comp.[postal_code] ELSE N'''' END
    )                                      AS SirketTamAdres,
    CONCAT(comp.[tax_office], N'' V.D. '', comp.[tax_number])
                                           AS SirketVergiSatiri,

    dl.[id]                                AS KalemId,
    dl.[LineNo]                            AS SiraNo,
    dl.[Quantity]                          AS Miktar,
    dl.[UnitPrice]                         AS BirimFiyat,
    dl.[DiscountRate]                      AS KalemIskontoOrani,
    dl.[LineTotal]                         AS SatirToplami,
    dl.[CombinationId]                     AS KombinasyonId,
    dl.[Notes]                             AS KalemNotu,
    dl.[NotesPinned]                       AS KalemNotuSabitli,

    dl.[ItemId]                            AS MalzemeId,
    i.[code]                               AS MalzemeKodu,
    i.[name]                               AS MalzemeAdi,
    i.[description]                        AS MalzemeAciklamasi,
    i.[tax_rate]                           AS MalzemeKdvOrani,

    dl.[UnitId]                            AS BirimId,
    mu.[code]                              AS BirimKodu,
    mu.[name]                              AS BirimAdi,

    dl.[LocationId]                        AS LokasyonId,
    loc.[code]                             AS LokasyonKodu,
    loc.[name]                             AS LokasyonAdi
' + @HwColsSql + @LwColsSql + N'
FROM [dbo].[Document] d
-- Revize edilmis eski satirlar (revised_from_id dolu ve > 0) raporda gorunmez;
-- yalnizca orijinal / aktif satirlar (NULL veya 0) JOIN e girer. ON icine konuyor
-- ki revisyon olmayan belgeler de gorunmeye devam etsin (LEFT JOIN korunsun).
LEFT JOIN [dbo].[DocumentLine]   dl   ON dl.[DocumentId] = d.[id]
                                      AND (dl.[RevisedFromId] IS NULL OR dl.[RevisedFromId] = 0)
LEFT JOIN [dbo].[Contact]        c    ON c.[Id]           = d.[contact_id]
LEFT JOIN [dbo].[sales_reps]     sr   ON sr.[id]          = d.[sales_rep_id]
LEFT JOIN [dbo].[document_types] dt   ON dt.[id]          = d.[document_type_id]
LEFT JOIN [dbo].[Items]          i    ON i.[id]           = dl.[ItemId]
LEFT JOIN [dbo].[measure_units]  mu   ON mu.[id]          = dl.[UnitId]
LEFT JOIN [dbo].[locations]      loc  ON loc.[id]         = dl.[LocationId]
' + @HwJoin + @LwJoin + N'
LEFT JOIN [' + @MasterDbName + N'].[dbo].[Company] comp ON comp.[id] = d.[company_id]
-- Sadece aktif belgeler: silinmis/pasif (IsActive = 0) Document kayitlari raporda gorunmez.
WHERE d.[IsActive] = 1;';

    EXEC sp_executesql @Sql;
END;
GO


/* Ilk build (widget eklediginde yeniden calistir) */
EXEC [dbo].[sp_Report_RebuildDocumentView];
GO


/* ═══════════════════════════════════════════════════════════════════════════
   HIZLI TEST
   SELECT * FROM dbo.vw_ReportDocument ORDER BY BelgeId DESC, SiraNo;
   SELECT * FROM dbo.vw_ReportDocument WHERE BelgeId = 1 ORDER BY SiraNo;
   SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_NAME = 'vw_ReportDocument' ORDER BY ORDINAL_POSITION;
   ═══════════════════════════════════════════════════════════════════════════ */
