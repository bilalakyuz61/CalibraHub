-- ─────────────────────────────────────────────────────────────────────────
--  Satış Teklifi — Standart Tasarım Seed (vw_ReportDocument + vw_DocumentCombination)
--
--  Bu script:
--    • DocLayout tablosuna "Standart Satış Teklifi" tasarımını ekler/günceller
--    • DocLayoutDs tablosuna 3 veri kaynağı bağlantısı oluşturur:
--        - Belge        → vw_ReportDocument      (master + line + cari + sirket + widget JOIN)
--        - Kalem        → vw_ReportDocument      (aynı zengin view, detail iteration için)
--        - Kombinasyon  → vw_DocumentCombination (kombinasyon özellik/değer detayları)
--    • Veri kaynakları @DocumentId parametresine göre filtrelenir
--    • IsDefault=1 set edip diğer Satış Teklifi tasarımlarının IsDefault'ını temizler
--
--  vw_ReportDocument Türkçe kolonlar kullanır:
--    Master:  BelgeId, BelgeNo, BelgeTarihi, GecerlilikTarihi, ParaBirimi,
--             GenelToplam, OdemeKosullari, BelgeNotu
--    Cari:    CariUnvani, CariKodu, CariAdres, CariVergiSatiri, CariTamAdres
--    Şirket:  SirketAdi, SirketUnvani, SirketAdresi, SirketVergiSatiri
--    Kalem:   KalemId, SiraNo, Miktar, BirimFiyat, SatirToplami, MalzemeKodu,
--             MalzemeAdi, BirimAdi, KombinasyonOzet
-- ─────────────────────────────────────────────────────────────────────────

DECLARE @Schema       NVARCHAR(50)     = N'dbo';
DECLARE @OwnerUserId  UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
DECLARE @Code         NVARCHAR(60)     = N'STD_SALES_QUOTE_v1';
DECLARE @Name         NVARCHAR(200)    = N'Standart Satış Teklifi';
DECLARE @DocType      NVARCHAR(60)     = N'sales_quote';

DECLARE @LayoutJson NVARCHAR(MAX) = N'{
  "pageWidth": 210, "pageHeight": 297,
  "margins": { "top": 15, "bottom": 15, "left": 15, "right": 15 },
  "bands": [
    {
      "id": "b_pageheader", "type": "PageHeader", "height": 18,
      "repeatOnEveryPage": true, "canGrow": false, "dataAlias": null,
      "elements": [
        { "id": "el_title", "kind": "Label", "x": 0, "y": 4, "w": 180, "h": 10,
          "text": "SATIŞ TEKLİFİ", "zIndex": 0,
          "style": { "fontSize": 20, "bold": true, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#1E3A8A", "bgColor": "transparent", "border": false } },
        { "id": "el_subline", "kind": "Label", "x": 0, "y": 15, "w": 180, "h": 1,
          "text": " ", "zIndex": 0,
          "style": { "fontSize": 8, "bold": false, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#1E3A8A", "bgColor": "#1E3A8A", "border": false } }
      ]
    },
    {
      "id": "b_docheader", "type": "DocumentHeader", "height": 34,
      "repeatOnEveryPage": false, "canGrow": false, "dataAlias": "Belge",
      "elements": [
        { "id": "el_lbl_cari", "kind": "Label", "x": 0,  "y": 2,  "w": 35, "h": 6,
          "text": "Cari Adı:", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#374151", "bgColor": "transparent", "border": false } },
        { "id": "el_val_cari", "kind": "BoundField", "x": 35, "y": 2,  "w": 145, "h": 6,
          "text": null, "binding": { "alias": "Belge", "col": "CariUnvani" }, "format": null, "zIndex": 0,
          "style": { "fontSize": 10, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false } },

        { "id": "el_lbl_no", "kind": "Label", "x": 0,  "y": 10, "w": 35, "h": 6,
          "text": "Belge No:", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#374151", "bgColor": "transparent", "border": false } },
        { "id": "el_val_no", "kind": "BoundField", "x": 35, "y": 10, "w": 60, "h": 6,
          "text": null, "binding": { "alias": "Belge", "col": "BelgeNo" }, "format": null, "zIndex": 0,
          "style": { "fontSize": 10, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false } },

        { "id": "el_lbl_tarih", "kind": "Label", "x": 100, "y": 10, "w": 30, "h": 6,
          "text": "Belge Tarihi:", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#374151", "bgColor": "transparent", "border": false } },
        { "id": "el_val_tarih", "kind": "BoundField", "x": 130, "y": 10, "w": 50, "h": 6,
          "text": null, "binding": { "alias": "Belge", "col": "BelgeTarihi" }, "format": "dd.MM.yyyy", "zIndex": 0,
          "style": { "fontSize": 10, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false } },

        { "id": "el_lbl_gec", "kind": "Label", "x": 0,  "y": 18, "w": 35, "h": 6,
          "text": "Geçerlilik:", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#374151", "bgColor": "transparent", "border": false } },
        { "id": "el_val_gec", "kind": "BoundField", "x": 35, "y": 18, "w": 60, "h": 6,
          "text": null, "binding": { "alias": "Belge", "col": "GecerlilikTarihi" }, "format": "dd.MM.yyyy", "zIndex": 0,
          "style": { "fontSize": 10, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false } },

        { "id": "el_hr", "kind": "Shape", "x": 0, "y": 30, "w": 180, "h": 0.5,
          "text": null, "zIndex": 0,
          "style": { "fontSize": 10, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#1E3A8A", "bgColor": "#1E3A8A", "border": false } }
      ]
    },
    {
      "id": "b_tableheader", "type": "TableHeader", "height": 8,
      "repeatOnEveryPage": true, "canGrow": false, "dataAlias": null,
      "elements": [
        { "id": "el_th_kod", "kind": "Label", "x": 0,   "y": 0, "w": 25, "h": 8,
          "text": "Stok Kodu", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#FFFFFF", "bgColor": "#1E3A8A", "border": false,
                     "borderTop": true, "borderRight": true, "borderBottom": true, "borderLeft": true } },
        { "id": "el_th_ad", "kind": "Label", "x": 25,  "y": 0, "w": 75, "h": 8,
          "text": "Stok Adı", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#FFFFFF", "bgColor": "#1E3A8A", "border": false,
                     "borderTop": true, "borderRight": true, "borderBottom": true, "borderLeft": false } },
        { "id": "el_th_mik", "kind": "Label", "x": 100, "y": 0, "w": 20, "h": 8,
          "text": "Miktar", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#FFFFFF", "bgColor": "#1E3A8A", "border": false,
                     "borderTop": true, "borderRight": true, "borderBottom": true, "borderLeft": false } },
        { "id": "el_th_fi", "kind": "Label", "x": 120, "y": 0, "w": 25, "h": 8,
          "text": "Birim Fiyat", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#FFFFFF", "bgColor": "#1E3A8A", "border": false,
                     "borderTop": true, "borderRight": true, "borderBottom": true, "borderLeft": false } },
        { "id": "el_th_tut", "kind": "Label", "x": 145, "y": 0, "w": 35, "h": 8,
          "text": "Tutar", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#FFFFFF", "bgColor": "#1E3A8A", "border": false,
                     "borderTop": true, "borderRight": true, "borderBottom": true, "borderLeft": false } }
      ]
    },
    {
      "id": "b_detail", "type": "Detail", "height": 7,
      "repeatOnEveryPage": false, "canGrow": true, "dataAlias": "Kalem",
      "elements": [
        { "id": "el_d_kod", "kind": "BoundField", "x": 0,  "y": 0, "w": 25, "h": 7,
          "text": null, "binding": { "alias": "Kalem", "col": "MalzemeKodu" }, "format": null, "zIndex": 0,
          "style": { "fontSize": 9, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false,
                     "borderBottom": true, "borderRight": true, "borderLeft": true } },
        { "id": "el_d_ad", "kind": "BoundField", "x": 25, "y": 0, "w": 75, "h": 7,
          "text": null, "binding": { "alias": "Kalem", "col": "MalzemeAdi" }, "format": null, "zIndex": 0,
          "style": { "fontSize": 9, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "shrink",
                     "color": "#000000", "bgColor": "transparent", "border": false,
                     "borderBottom": true, "borderRight": true } },
        { "id": "el_d_mik", "kind": "BoundField", "x": 100, "y": 0, "w": 20, "h": 7,
          "text": null, "binding": { "alias": "Kalem", "col": "Miktar" }, "format": "#,##0.00", "zIndex": 0,
          "style": { "fontSize": 9, "bold": false, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false,
                     "borderBottom": true, "borderRight": true } },
        { "id": "el_d_fi", "kind": "BoundField", "x": 120, "y": 0, "w": 25, "h": 7,
          "text": null, "binding": { "alias": "Kalem", "col": "BirimFiyat" }, "format": "#,##0.00", "zIndex": 0,
          "style": { "fontSize": 9, "bold": false, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false,
                     "borderBottom": true, "borderRight": true } },
        { "id": "el_d_tut", "kind": "BoundField", "x": 145, "y": 0, "w": 35, "h": 7,
          "text": null, "binding": { "alias": "Kalem", "col": "SatirToplami" }, "format": "#,##0.00", "zIndex": 0,
          "style": { "fontSize": 9, "bold": true, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false,
                     "borderBottom": true, "borderRight": true } }
      ]
    },
    {
      "id": "b_subdetail", "type": "SubDetail", "height": 6,
      "repeatOnEveryPage": false, "canGrow": false, "dataAlias": "Kombinasyon",
      "elements": [
        { "id": "el_sd_spacer", "kind": "Label", "x": 0, "y": 0, "w": 8, "h": 6,
          "text": "↳", "zIndex": 0,
          "style": { "fontSize": 8, "bold": false, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#6B7280", "bgColor": "transparent", "border": false } },
        { "id": "el_sd_ozellik", "kind": "BoundField", "x": 10, "y": 0, "w": 50, "h": 6,
          "text": null, "binding": { "alias": "Kombinasyon", "col": "OzellikAdi" }, "format": null, "zIndex": 0,
          "style": { "fontSize": 8, "bold": false, "italic": true, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#374151", "bgColor": "transparent", "border": false } },
        { "id": "el_sd_sep", "kind": "Label", "x": 60, "y": 0, "w": 3, "h": 6,
          "text": ":", "zIndex": 0,
          "style": { "fontSize": 8, "bold": false, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#6B7280", "bgColor": "transparent", "border": false } },
        { "id": "el_sd_deger", "kind": "BoundField", "x": 65, "y": 0, "w": 115, "h": 6,
          "text": null, "binding": { "alias": "Kombinasyon", "col": "DegerAdi" }, "format": null, "zIndex": 0,
          "style": { "fontSize": 8, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "transparent", "border": false } }
      ]
    },
    {
      "id": "b_totals", "type": "TotalsBlock", "height": 28,
      "repeatOnEveryPage": false, "canGrow": false, "dataAlias": "Belge",
      "elements": [
        { "id": "el_t_pl", "kind": "Label", "x": 0, "y": 3, "w": 50, "h": 5,
          "text": "Ödeme Koşulları:", "zIndex": 0,
          "style": { "fontSize": 10, "bold": true, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#374151", "bgColor": "transparent", "border": false } },
        { "id": "el_t_pv", "kind": "BoundField", "x": 0, "y": 9, "w": 110, "h": 18,
          "text": null, "binding": { "alias": "Belge", "col": "OdemeKosullari" }, "format": null, "zIndex": 0,
          "style": { "fontSize": 9, "bold": false, "italic": false, "underline": false,
                     "align": "left", "verticalAlign": "top", "overflow": "wrap",
                     "color": "#374151", "bgColor": "transparent", "border": false,
                     "borderTop": true, "borderRight": true, "borderBottom": true, "borderLeft": true } },

        { "id": "el_t_gl", "kind": "Label", "x": 120, "y": 3, "w": 35, "h": 6,
          "text": "Genel Toplam:", "zIndex": 0,
          "style": { "fontSize": 11, "bold": true, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#1E3A8A", "bgColor": "transparent", "border": false } },
        { "id": "el_t_gv", "kind": "BoundField", "x": 120, "y": 11, "w": 60, "h": 9,
          "text": null, "binding": { "alias": "Belge", "col": "GenelToplam" }, "format": "#,##0.00", "zIndex": 0,
          "style": { "fontSize": 16, "bold": true, "italic": false, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "shrink",
                     "color": "#1E3A8A", "bgColor": "transparent", "border": false,
                     "borderBottom": true } },
        { "id": "el_t_words", "kind": "AmountInWords", "x": 120, "y": 21, "w": 60, "h": 6,
          "text": null, "binding": { "alias": "Belge", "col": "GenelToplam" }, "format": null, "zIndex": 0,
          "style": { "fontSize": 7, "bold": false, "italic": true, "underline": false,
                     "align": "right", "verticalAlign": "middle", "overflow": "wrap",
                     "color": "#6B7280", "bgColor": "transparent", "border": false } }
      ]
    },
    {
      "id": "b_signature", "type": "SignatureBlock", "height": 32,
      "repeatOnEveryPage": false, "canGrow": false, "dataAlias": null,
      "elements": [
        { "id": "el_s_hzr_line", "kind": "Label", "x": 10, "y": 20, "w": 60, "h": 0.5,
          "text": " ", "zIndex": 0,
          "style": { "fontSize": 10, "bold": false, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "#9CA3AF", "border": false } },
        { "id": "el_s_hzr_lbl", "kind": "Label", "x": 10, "y": 22, "w": 60, "h": 5,
          "text": "Hazırlayan", "zIndex": 0,
          "style": { "fontSize": 9, "bold": true, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#374151", "bgColor": "transparent", "border": false } },

        { "id": "el_s_ony_line", "kind": "Label", "x": 110, "y": 20, "w": 60, "h": 0.5,
          "text": " ", "zIndex": 0,
          "style": { "fontSize": 10, "bold": false, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#000000", "bgColor": "#9CA3AF", "border": false } },
        { "id": "el_s_ony_lbl", "kind": "Label", "x": 110, "y": 22, "w": 60, "h": 5,
          "text": "Onaylayan", "zIndex": 0,
          "style": { "fontSize": 9, "bold": true, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#374151", "bgColor": "transparent", "border": false } }
      ]
    },
    {
      "id": "b_pagefooter", "type": "PageFooter", "height": 10,
      "repeatOnEveryPage": true, "canGrow": false, "dataAlias": null,
      "elements": [
        { "id": "el_pf_line", "kind": "Label", "x": 0, "y": 0, "w": 180, "h": 0.3,
          "text": " ", "zIndex": 0,
          "style": { "fontSize": 8, "bold": false, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#9CA3AF", "bgColor": "#9CA3AF", "border": false } },
        { "id": "el_pf_no", "kind": "PageNumber", "x": 80, "y": 3, "w": 20, "h": 5,
          "text": null, "zIndex": 0,
          "style": { "fontSize": 9, "bold": false, "italic": false, "underline": false,
                     "align": "center", "verticalAlign": "middle", "overflow": "ellipsis",
                     "color": "#6B7280", "bgColor": "transparent", "border": false } }
      ]
    }
  ]
}';

-- ───────────────── INSERT (yoksa) / UPDATE (varsa) DocLayout ─────────────────

DECLARE @sql NVARCHAR(MAX) = N'
DECLARE @LayoutId INT;

IF EXISTS (SELECT 1 FROM [' + @Schema + N'].[DocLayout] WHERE [Code] = @Code)
BEGIN
    UPDATE [' + @Schema + N'].[DocLayout]
    SET [Name]        = @Name,
        [DocType]     = @DocType,
        [Description] = N''Standart satis teklifi tasarimi (vw_ReportDocument + vw_DocumentCombination)'',
        [LayoutJson]  = @LayoutJson,
        [PageW]       = 210,
        [PageH]       = 297,
        [MarginTop]   = 15,
        [MarginBot]   = 15,
        [MarginLeft]  = 15,
        [MarginRight] = 15,
        [IsDefault]   = 1,
        [UpdatedAt]   = SYSUTCDATETIME()
    WHERE [Code] = @Code;

    SELECT @LayoutId = [Id] FROM [' + @Schema + N'].[DocLayout] WHERE [Code] = @Code;
END
ELSE
BEGIN
    INSERT INTO [' + @Schema + N'].[DocLayout]
        ([Code],[Name],[DocType],[Description],[LayoutJson],
         [PageW],[PageH],[MarginTop],[MarginBot],[MarginLeft],[MarginRight],
         [OwnerUserId],[IsDefault])
    VALUES
        (@Code, @Name, @DocType, N''Standart satis teklifi tasarimi (vw_ReportDocument + vw_DocumentCombination)'', @LayoutJson,
         210, 297, 15, 15, 15, 15,
         @OwnerUserId, 1);

    SET @LayoutId = SCOPE_IDENTITY();
END;

-- IsDefault singleton: bu DocType''taki diğer aktif layout''ların IsDefault=0
UPDATE [' + @Schema + N'].[DocLayout]
SET [IsDefault] = 0
WHERE [DocType] = @DocType AND [Id] <> @LayoutId AND [IsActive] = 1;

-- Veri kaynaklarını sıfırdan yaz (mevcutları sil + yeni 3 kayıt ekle)
DELETE FROM [' + @Schema + N'].[DocLayoutDs] WHERE [LayoutId] = @LayoutId;

-- @DocumentId parametresi DocDesignerService tarafindan otomatik bind edilir.
-- vw_ReportDocument zengin rapor view: belge + kalem + cari + sirket + widget JOIN.
-- vw_DocumentCombination ise kombinasyon ozellik/deger detaylarini verir.
INSERT INTO [' + @Schema + N'].[DocLayoutDs]
    ([LayoutId],[Alias],[Role],[ViewId],[AdHocSql],[JoinOn],[ParentAlias],[Ordinal])
VALUES
    (@LayoutId, N''Belge'',       N''master'',    NULL, N''SELECT * FROM [' + @Schema + N'].[vw_ReportDocument]      WHERE [BelgeId] = @DocumentId'', NULL, NULL, 0),
    (@LayoutId, N''Kalem'',       N''detail'',    NULL, N''SELECT * FROM [' + @Schema + N'].[vw_ReportDocument]      WHERE [BelgeId] = @DocumentId'', NULL, NULL, 1),
    (@LayoutId, N''Kombinasyon'', N''subdetail'', NULL, N''SELECT * FROM [' + @Schema + N'].[vw_DocumentCombination] WHERE [BelgeId] = @DocumentId'', NULL, NULL, 2);

PRINT N''✓ Tasarım kaydedildi: '' + @Name + N'' (LayoutId='' + CAST(@LayoutId AS NVARCHAR(10)) + N'')'';
PRINT N''✓ 3 veri kaynağı bağlandı: Belge, Kalem, Kombinasyon'';
PRINT N''→ /DocDesigner listesinden açıp düzenleyebilir, satış teklifi PDF basabilirsin.'';
';

EXEC sp_executesql @sql,
    N'@Code NVARCHAR(60), @Name NVARCHAR(200), @DocType NVARCHAR(60),
      @LayoutJson NVARCHAR(MAX), @OwnerUserId UNIQUEIDENTIFIER',
    @Code = @Code, @Name = @Name, @DocType = @DocType,
    @LayoutJson = @LayoutJson, @OwnerUserId = @OwnerUserId;
