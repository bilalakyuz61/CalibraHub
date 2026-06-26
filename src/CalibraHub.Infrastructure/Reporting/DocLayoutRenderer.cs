using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QRCoder;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;
using ZXing.SkiaSharp.Rendering;

namespace CalibraHub.Infrastructure.Reporting;

/// <summary>
/// LayoutJson → QuestPDF (PDF) veya basit HTML (önizleme) dönüştürücü.
/// Renderer salt veri alır — SQL çalıştırmaz; orchestration DocDesignerService'dedir.
/// </summary>
public sealed class DocLayoutRenderer : IDocLayoutRenderer
{
    public byte[] RenderPdf(string layoutJson, IReadOnlyDictionary<string, ReportRawResult> data,
                            IReadOnlyList<DataSourceMeta>? sources = null)
    {
        var joinMeta = BuildJoinMeta(sources);
        // joinMeta'yı tüm closure'lara ulaşacak şekilde captured-variable olarak tut.
        // DocLayoutRenderer.RenderPdf metodundaki tüm local fonksiyonlar bu sözlüğü
        // kullanır (hardcoded "KalemId" yerine).
        _ = joinMeta;
        QuestPDF.Settings.License = LicenseType.Community;
        // Debugging açıkken QuestPDF "conflicting size constraints" gibi hatalara
        // hangi element/band'ın sebep olduğunu açıkça gösterir. Production'da da
        // açık bırakmak güvenli — sadece exception mesajı genişler, render etkilenmez.
        QuestPDF.Settings.EnableDebugging = true;

        var doc = ParseLayout(layoutJson);
        var bands = doc.Bands;

        var pageHeader   = bands.FirstOrDefault(b => b.Type == "PageHeader");
        var docHeader    = bands.FirstOrDefault(b => b.Type == "DocumentHeader");
        var tableHeader  = bands.FirstOrDefault(b => b.Type == "TableHeader");
        // Detail VE SubDetail ayrı tablolar olarak render edilir. Her ikisi de
        // tasarımda varsa kullanıcının bantları (vw_ReportDocument hareketleri,
        // vw_DocumentCombination özellikleri vb.) ayrı tablolar olarak alt alta görünür.
        var detail       = bands.FirstOrDefault(b => b.Type == "Detail");
        var subDetail    = bands.FirstOrDefault(b => b.Type == "SubDetail");
        var subDetailHeader = bands.FirstOrDefault(b => b.Type == "SubDetailHeader");
        var subDetailFooter = bands.FirstOrDefault(b => b.Type == "SubDetailFooter");
        var totalsBand   = bands.FirstOrDefault(b => b.Type == "TotalsBlock");
        var sigBand      = bands.FirstOrDefault(b => b.Type == "SignatureBlock");
        var pageFooter   = bands.FirstOrDefault(b => b.Type == "PageFooter");

        var contentWidth = doc.PageWidth - doc.Margins.Left - doc.Margins.Right;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size((float)doc.PageWidth, (float)doc.PageHeight, Unit.Millimetre);
                page.MarginTop((float)doc.Margins.Top, Unit.Millimetre);
                page.MarginBottom((float)doc.Margins.Bottom, Unit.Millimetre);
                page.MarginLeft((float)doc.Margins.Left, Unit.Millimetre);
                page.MarginRight((float)doc.Margins.Right, Unit.Millimetre);

                // Tüm bantlar SIRAYLA Column akışında — kullanıcının tasarımdaki dikey
                // sıralaması birebir korunur. page.Header()/page.Footer() özel slot'ları
                // kullanılmıyor: o slot'lar her sayfaya tekrar bastırır ve designer'da
                // konumlanan Y'den bağımsız olarak page kenarlarına yapışır.
                page.Content().Column(col =>
                {
                    col.Spacing(0);

                    // PageHeader artık Column içinde (designer canvas'taki ilk bant)
                    if (pageHeader != null)
                        col.Item().Height((float)pageHeader.Height, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, pageHeader, data, contentWidth));

                    if (docHeader != null)
                        col.Item().Height((float)docHeader.Height, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, docHeader, data, contentWidth));

                    // TableHeader: Detail ve SubDetail yoksa standalone band olarak render et
                    // (kullanıcı cari isim gibi statik içerik koyabilir; aksi halde kaybolur)
                    if (tableHeader != null && detail == null && subDetail == null)
                        col.Item().Height((float)tableHeader.Height, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, tableHeader, data, contentWidth));

                    // Alt detay başlığı SubDetail tablosuna gömülür — standalone render etme
                    // (sadece SubDetail yoksa veya başka durumda statik blok olarak render edilir).
                    if (subDetailHeader != null && subDetail == null)
                        col.Item().Height((float)subDetailHeader.Height, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, subDetailHeader, data, contentWidth));

                    // ── Detail (ana hareket tablosu) ─────────────────────────────────────
                    // Aynı render mantığı SubDetail için de çalışsın diye local fonksiyon.
                    void RenderDetailTable(LayoutBand band, LayoutBand? hdrBand)
                    {
                        var aliasResolved = !string.IsNullOrWhiteSpace(band.DataAlias)
                            ? band.DataAlias!
                            : band.Elements
                                .Select(e => e.Binding?.Alias)
                                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                              ?? band.Type;
                        data.TryGetValue(aliasResolved, out var detailData);

                        var colDefs = BuildColumnDefs(hdrBand, band, contentWidth);

                        // Toplam mm > içerik genişliği ise orantılı küçült
                        var totalColMm = colDefs.Sum(c => c.WidthMm);
                        var availMm = (double)contentWidth;
                        if (totalColMm > availMm && totalColMm > 0)
                        {
                            var scale = availMm / totalColMm;
                            colDefs = colDefs.Select(c => c with { WidthMm = c.WidthMm * scale }).ToList();
                        }

                        var effectiveTableHeader = hdrBand;
                        var detailBand = band;
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                // ConstantColumn → tasarımdaki gerçek mm genişlikleri korunur
                                foreach (var cd in colDefs)
                                    cols.ConstantColumn((float)cd.WidthMm, Unit.Millimetre);
                            });

                            if (effectiveTableHeader != null)
                            {
                                // Header element listesini X sırasında bir kez al ve manuel
                                // sayaçla ilerle — colDefs.IndexOf spacer kolonlarla yanlış
                                // index üretiyordu, header etiketleri kayıyordu.
                                var headerElemsOrdered = effectiveTableHeader.Elements.OrderBy(e => e.X).ToList();
                                int headerElemIdx = 0;
                                table.Header(header =>
                                {
                                    foreach (var cd in colDefs)
                                    {
                                        // Spacer kolon → header'da görünmeyen boş hücre
                                        if (cd.IsSpacer)
                                        {
                                            header.Cell().Text(""); // empty cell to consume the column slot
                                            continue;
                                        }

                                        var el = headerElemIdx < headerElemsOrdered.Count
                                            ? headerElemsOrdered[headerElemIdx++]
                                            : null;
                                        var hs = el?.Style;
                                        var hLegacyAll = hs?.Border == true;
                                        var hbT = hs?.BorderTop    ?? hLegacyAll;
                                        var hbR = hs?.BorderRight  ?? hLegacyAll;
                                        var hbB = hs?.BorderBottom ?? hLegacyAll;
                                        var hbL = hs?.BorderLeft   ?? hLegacyAll;
                                        var hOverflow = hs?.Overflow ?? "ellipsis";

                                        IContainer hcell = header.Cell();
                                        // Header arkaplanı: kullanıcı verdiyse onu, yoksa hafif gri ayraç
                                        if (!string.IsNullOrWhiteSpace(hs?.BgColor) &&
                                            !hs.BgColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                                            hcell = hcell.Background(SafeColor(hs.BgColor, "#EEEEEE"));
                                        else
                                            hcell = hcell.Background("#FFEEEEEE");
                                        // Kenarlık: yalnızca kullanıcı tanımladıysa
                                        if (hbT) hcell = hcell.BorderTop(0.5f).BorderColor(Colors.Grey.Darken1);
                                        if (hbR) hcell = hcell.BorderRight(0.5f).BorderColor(Colors.Grey.Darken1);
                                        if (hbB) hcell = hcell.BorderBottom(0.5f).BorderColor(Colors.Grey.Darken1);
                                        if (hbL) hcell = hcell.BorderLeft(0.5f).BorderColor(Colors.Grey.Darken1);

                                        // Header label çözümü: el.Text > binding'in resolved değeri > kolon adı
                                        // (kullanıcı BoundField koyduysa veriden çek; aksi halde col name)
                                        string headerText = el?.Text
                                            ?? (el?.Binding != null ? ResolveFieldRaw(el, data) : null)
                                            ?? cd.ColName
                                            ?? "";
                                        if (string.IsNullOrEmpty(headerText) && el?.Binding != null)
                                            headerText = el.Binding.Col;   // resolve null döndüyse en azından col name

                                        hcell.Padding(1, Unit.Millimetre).Text(t =>
                                        {
                                            if (!string.Equals(hOverflow, "wrap", StringComparison.OrdinalIgnoreCase))
                                                t.ClampLines(1);
                                            switch (hs?.Align?.ToLowerInvariant())
                                            {
                                                case "right":   t.AlignRight();  break;
                                                case "center":  t.AlignCenter(); break;
                                                case "justify": t.Justify();     break;
                                                default:        t.AlignLeft();   break;
                                            }
                                            var span = t.Span(headerText);
                                            span.FontSize(hs?.FontSize ?? 9);
                                            span.Bold();
                                            if (!string.IsNullOrEmpty(hs?.Color)) span.FontColor(SafeColor(hs.Color, Colors.Black));
                                        });
                                    }
                                });
                            }

                            if (detailData != null)
                            {
                                var colIndex = BuildColIndex(detailData);
                                // Zebra: detail/subdetail bantta enabled ise çift sıralara
                                // (rowIndex % 2 == 1) hafif arka plan rengi uygulanır.
                                var zebraOn   = detailBand.ZebraEnabled;
                                var zebraHex  = string.IsNullOrWhiteSpace(detailBand.ZebraColor)
                                    ? "#F3F4F6"   // hafif gri default (Tailwind gray-100)
                                    : detailBand.ZebraColor!;

                                // ── Master-Detail-SubDetail nesting hazırlığı ──────────────
                                // Çoklu kolon eşleme destekli (UI'dan gelen joinPairs).
                                // Composite key = "val1||val2||..." formatında oluşur.
                                Dictionary<string, List<IReadOnlyList<object?>>>? subByKey = null;
                                List<LayoutElement>? subElements = null;
                                int[]? parentJoinColIdxs = null;
                                LayoutBand? childBand = null;
                                if (detailBand.Type == "Detail" && subDetail != null)
                                {
                                    var subAlias = !string.IsNullOrWhiteSpace(subDetail.DataAlias)
                                        ? subDetail.DataAlias!
                                        : subDetail.Elements.Select(e => e.Binding?.Alias)
                                            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                                          ?? "SubDetail";

                                    var parentAliasOfDetail = !string.IsNullOrWhiteSpace(detailBand.DataAlias)
                                        ? detailBand.DataAlias!
                                        : detailBand.Elements.Select(e => e.Binding?.Alias)
                                            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                                          ?? "Detail";

                                    if (joinMeta.TryGetValue(subAlias, out var meta) &&
                                        string.Equals(meta.ParentAlias, parentAliasOfDetail, StringComparison.OrdinalIgnoreCase) &&
                                        data.TryGetValue(subAlias, out var subData))
                                    {
                                        // Parent ve child taraflarında her kolon için index map'i
                                        parentJoinColIdxs = meta.Pairs
                                            .Select(p => colIndex.TryGetValue(p.ParentCol, out var i) ? i : -1)
                                            .ToArray();
                                        var subColIdx = BuildColIndex(subData);
                                        var subKeyIdxs = meta.Pairs
                                            .Select(p => subColIdx.TryGetValue(p.ChildCol, out var i) ? i : -1)
                                            .ToArray();

                                        // En az bir kolon her iki view'da da bulunmalı
                                        if (parentJoinColIdxs.All(i => i >= 0) && subKeyIdxs.All(i => i >= 0))
                                        {
                                            subByKey = new Dictionary<string, List<IReadOnlyList<object?>>>();
                                            foreach (var subRow in subData.Rows)
                                            {
                                                var key = BuildCompositeKey(subRow, subKeyIdxs);
                                                if (!subByKey.TryGetValue(key, out var list))
                                                {
                                                    list = new List<IReadOnlyList<object?>>();
                                                    subByKey[key] = list;
                                                }
                                                list.Add(subRow);
                                            }
                                            subElements = subDetail.Elements.OrderBy(e => e.X).ToList();
                                            childBand = subDetail;
                                        }
                                        else
                                        {
                                            parentJoinColIdxs = null;
                                        }
                                    }
                                }

                                int rowIdx = 0;
                                foreach (var row in detailData.Rows)
                                {
                                    var isZebraRow = zebraOn && (rowIdx % 2 == 1);
                                    foreach (var cd in colDefs)
                                    {
                                        // Spacer kolon → boş hücre (border, bg, content yok).
                                        // Zebra çift sırasında ise spacer'a da aynı bg uygulanır,
                                        // aksi halde stripe satırın ortasında kesik görünür.
                                        if (cd.IsSpacer)
                                        {
                                            // Cell() ITableCellContainer döner; Background sonrası
                                            // IContainer'a düşer — Text bunun üstünden çağrılır.
                                            IContainer spacerCell = table.Cell();
                                            if (isZebraRow)
                                                spacerCell = spacerCell.Background(SafeColor(zebraHex, Colors.Transparent));
                                            spacerCell.Text("");
                                            continue;
                                        }

                                        var cellVal = cd.ColName != null && colIndex.TryGetValue(cd.ColName, out var ci)
                                            ? FormatValue(row.Count > ci ? row[ci] : null, cd.Format)
                                            : "";

                                        IContainer cell = table.Cell();
                                        // Kenarlık: KULLANICI BU AYARI AÇTIYSA çiz; otomatik kenarlık yok
                                        if (cd.BorderTop)    cell = cell.BorderTop(0.5f).BorderColor(Colors.Grey.Darken1);
                                        if (cd.BorderRight)  cell = cell.BorderRight(0.5f).BorderColor(Colors.Grey.Darken1);
                                        if (cd.BorderBottom) cell = cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Darken1);
                                        if (cd.BorderLeft)   cell = cell.BorderLeft(0.5f).BorderColor(Colors.Grey.Darken1);
                                        // Arkaplan önceliği: kullanıcı kolona özel bgColor verdiyse o;
                                        // yoksa zebra çift sıraysa zebra rengi; yoksa şeffaf.
                                        if (!string.IsNullOrWhiteSpace(cd.BgColor) &&
                                            !cd.BgColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                                        {
                                            cell = cell.Background(SafeColor(cd.BgColor, Colors.Transparent));
                                        }
                                        else if (isZebraRow)
                                        {
                                            cell = cell.Background(SafeColor(zebraHex, Colors.Transparent));
                                        }

                                        cell.Padding(1, Unit.Millimetre).Text(t =>
                                        {
                                            // Taşma davranışı: wrap = alt satıra geç, shrink = font küçült, diğer = tek satır kes
                                            var overflowMode = (cd.Overflow ?? "ellipsis").ToLowerInvariant();
                                            if (overflowMode != "wrap")
                                                t.ClampLines(1);

                                            // Hizalama
                                            switch (cd.Align?.ToLowerInvariant())
                                            {
                                                case "right":   t.AlignRight();  break;
                                                case "center":  t.AlignCenter(); break;
                                                case "justify": t.Justify();     break;
                                                default:        t.AlignLeft();   break;
                                            }

                                            // Shrink modunda font boyutunu metin uzunluğuna göre küçült
                                            float fs = cd.FontSize;
                                            if (overflowMode == "shrink")
                                                fs = ShrinkFontSize(cellVal, cd.WidthMm, cd.FontSize);

                                            var span = t.Span(cellVal);
                                            span.FontSize(fs);
                                            span.FontColor(SafeColor(cd.Color, Colors.Black));
                                        });
                                    }

                                    // ── Nested SubDetail rows (çoklu kolon composite key ile eşle) ──
                                    if (subByKey != null && subElements != null && parentJoinColIdxs != null)
                                    {
                                        var currentKey = BuildCompositeKey(row, parentJoinColIdxs);
                                        if (subByKey.TryGetValue(currentKey, out var matched))
                                        {
                                            var subAliasLocal = !string.IsNullOrWhiteSpace(childBand!.DataAlias)
                                                ? childBand.DataAlias!
                                                : childBand.Elements.Select(e => e.Binding?.Alias)
                                                    .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                                                  ?? "SubDetail";
                                            data.TryGetValue(subAliasLocal, out var subDataLocal);
                                            var subColIdxLocal = subDataLocal != null ? BuildColIndex(subDataLocal) : null;

                                            foreach (var subRow in matched)
                                            {
                                                // Tek geniş hücre — tüm detail kolonlarını kapsar
                                                table.Cell().ColumnSpan((uint)Math.Max(1, colDefs.Count))
                                                    .PaddingLeft(6, Unit.Millimetre)
                                                    .PaddingTop(0.5f, Unit.Millimetre)
                                                    .PaddingBottom(0.5f, Unit.Millimetre)
                                                    .Background(isZebraRow ? SafeColor(zebraHex, Colors.Transparent) : "#FFFAFAFA")
                                                    .Text(t =>
                                                    {
                                                        t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken2));
                                                        bool first = true;
                                                        foreach (var subEl in subElements)
                                                        {
                                                            var col = subEl.Binding?.Col;
                                                            if (string.IsNullOrEmpty(col)) continue;
                                                            if (subColIdxLocal == null || !subColIdxLocal.TryGetValue(col, out var ci)) continue;
                                                            var v = subRow.Count > ci ? subRow[ci] : null;
                                                            if (v == null || v is DBNull) continue;
                                                            var formatted = FormatValue(v, subEl.Format);
                                                            if (string.IsNullOrWhiteSpace(formatted)) continue;
                                                            if (!first) t.Span("  •  ");
                                                            first = false;
                                                            // Element.Text varsa label olarak kullan ("Renk:"), yoksa col adı
                                                            var label = !string.IsNullOrWhiteSpace(subEl.Text) ? subEl.Text! : col;
                                                            t.Span(label + ": ");
                                                            t.Span(formatted).Bold();
                                                        }
                                                    });
                                            }
                                        }
                                    }

                                    rowIdx++;
                                }
                            }
                        });
                    }
                    // ── Tabloları çağır ──────────────────────────────────────────────
                    // Detail varsa TableHeader'la basılır. SubDetail için:
                    //   - Detail var + UI'da parent+joinOn tanımlı → Detail render içinde
                    //     nested basıldı, standalone basma.
                    //   - Detail yok VEYA parent/joinOn tanımsız → ayrı tablo olarak bas.
                    if (detail != null)
                        RenderDetailTable(detail, tableHeader);

                    bool subIsNested = false;
                    if (detail != null && subDetail != null)
                    {
                        var subAliasResolved = !string.IsNullOrWhiteSpace(subDetail.DataAlias)
                            ? subDetail.DataAlias!
                            : subDetail.Elements.Select(e => e.Binding?.Alias)
                                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "SubDetail";
                        var detailAliasResolved = !string.IsNullOrWhiteSpace(detail.DataAlias)
                            ? detail.DataAlias!
                            : detail.Elements.Select(e => e.Binding?.Alias)
                                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "Detail";
                        if (joinMeta.TryGetValue(subAliasResolved, out var jm) &&
                            string.Equals(jm.ParentAlias, detailAliasResolved, StringComparison.OrdinalIgnoreCase))
                            subIsNested = true;
                    }
                    if (subDetail != null && !subIsNested)
                        RenderDetailTable(subDetail, subDetailHeader ?? tableHeader);

                    // Alt detay altı (varsa) — detail tablosundan sonra statik blok.
                    // PaddingTop kaldırıldı: tasarımcı bantları ardışık tasarladığı için
                    // bant aralarına yapay boşluk eklemek tasarımı kaydırır. Boşluk istenirse
                    // bant Height'ı büyütülür.
                    if (subDetailFooter != null)
                        col.Item().Height((float)subDetailFooter.Height, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, subDetailFooter, data, contentWidth));

                    if (totalsBand != null)
                        col.Item().Height((float)totalsBand.Height, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, totalsBand, data, contentWidth));

                    if (sigBand != null)
                        col.Item().Height((float)sigBand.Height, Unit.Millimetre)
                            .Element(c => RenderBandContent(c, sigBand, data, contentWidth));
                });

                // PageFooter sayfa kenarına yapışır — tasarımın niyeti sayfa altıdır,
                // imza bloğunun altına bitişik değil. page.Footer() özel slot'u her sayfanın
                // alt kenarına otomatik yerleştirir; multi-page durumda da doğru çalışır.
                if (pageFooter != null)
                    page.Footer().Height((float)pageFooter.Height, Unit.Millimetre)
                        .Element(c => RenderBandContent(c, pageFooter, data, contentWidth));
            });
        }).GeneratePdf();
    }

    public string RenderHtml(string layoutJson, IReadOnlyDictionary<string, ReportRawResult> data,
                              IReadOnlyList<DataSourceMeta>? sources = null)
    {
        var joinMeta = BuildJoinMeta(sources);
        _ = joinMeta;
        var doc = ParseLayout(layoutJson);
        var sb = new StringBuilder();

        // Kağıt + margin değerlerini layout'tan dinamik üret (önceden hardcoded 38px/57px idi)
        const double MM_TO_PX = 96.0 / 25.4;   // 1 mm ≈ 3.7795 px
        var pageWPx = (double)doc.PageWidth  * MM_TO_PX;
        var pageHPx = (double)doc.PageHeight * MM_TO_PX;
        var padTop    = (double)doc.Margins.Top    * MM_TO_PX;
        var padBottom = (double)doc.Margins.Bottom * MM_TO_PX;
        var padLeft   = (double)doc.Margins.Left   * MM_TO_PX;
        var padRight  = (double)doc.Margins.Right  * MM_TO_PX;

        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<style>*{box-sizing:border-box;margin:0;padding:0;font-family:Arial,sans-serif;font-size:10pt;}");
        sb.AppendLine($".page{{width:{pageWPx:F0}px;min-height:{pageHPx:F0}px;background:#fff;margin:20px auto;" +
                      $"padding:{padTop:F0}px {padRight:F0}px {padBottom:F0}px {padLeft:F0}px;" +
                      "box-shadow:0 2px 8px rgba(0,0,0,.15);}");
        // min-height kullan — detail tablosu N satır render edip band'ın tasarım
        // yüksekliğini aştığında alt band'larla çakışmasın (PDF'te bu durum
        // Column().Spacing(0) + MinHeight sayesinde zaten doğru çalışıyor).
        sb.AppendLine(".band{position:relative;width:100%;margin:0;}");
        sb.AppendLine(".band.flow{min-height:var(--bh);}");
        sb.AppendLine(".band.fixed{height:var(--bh);}");
        sb.AppendLine(".el{position:absolute;overflow:hidden;font-size:10pt;}");
        sb.AppendLine(".el img{width:100%;height:100%;display:block;}");
        sb.AppendLine("table.detail{width:100%;border-collapse:collapse;}");
        sb.AppendLine("table.detail th,table.detail td{border:1px solid #ddd;padding:2px 4px;font-size:9pt;}");
        sb.AppendLine("table.detail th{background:#eee;font-weight:bold;}");
        sb.AppendLine("</style></head><body><div class='page'>");

        // Table header band tespiti — Detail için TableHeader, SubDetail için SubDetailHeader
        var tableHeaderBand    = doc.Bands.FirstOrDefault(b => b.Type == "TableHeader");
        var subDetailHeaderBand = doc.Bands.FirstOrDefault(b => b.Type == "SubDetailHeader");
        var hasSubDetail       = doc.Bands.Any(b => b.Type == "SubDetail");
        var hasDetail          = doc.Bands.Any(b => b.Type == "Detail");

        foreach (var band in doc.Bands)
        {
            // Tablo başlığı bantları tabloya gömüldü → standalone render etme.
            // ANCAK detail band yoksa TableHeader standalone band olarak görünmeli
            // (kullanıcı statik içerik koymuş olabilir).
            if (band.Type == "TableHeader" && hasDetail) continue;
            if (band.Type == "SubDetailHeader" && hasSubDetail) continue;
            // SubDetail: UI'da parent+joinOn tanımlıysa Detail içinde nested basılır → atla.
            // Tanımsızsa kendi başına ayrı tablo olur.
            if (band.Type == "SubDetail" && hasDetail)
            {
                var subAlias = !string.IsNullOrWhiteSpace(band.DataAlias)
                    ? band.DataAlias!
                    : band.Elements.Select(e => e.Binding?.Alias)
                        .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "SubDetail";
                var detailBand = doc.Bands.First(b => b.Type == "Detail");
                var detailAlias = !string.IsNullOrWhiteSpace(detailBand.DataAlias)
                    ? detailBand.DataAlias!
                    : detailBand.Elements.Select(e => e.Binding?.Alias)
                        .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "Detail";
                if (joinMeta.TryGetValue(subAlias, out var jm) &&
                    string.Equals(jm.ParentAlias, detailAlias, StringComparison.OrdinalIgnoreCase))
                    continue;   // nested edildi
            }

            var heightPx = (double)band.Height * 3.78;
            // Detail/SubDetail bandı: tablo kendi yüksekliğini belirler (min-height).
            // Diğer band'lar: tasarımdaki gerçek yükseklik korunur (absolute element'ler
            // bu alana yerleşir). Aksi halde tablo 10 satıra çıkıp altındaki toplam/imza
            // bandlarıyla çakışıyor (kullanıcı raporu).
            var bandClass = (band.Type == "Detail" || band.Type == "SubDetail") ? "band flow" : "band fixed";
            sb.AppendLine($"<div class='{bandClass}' style='--bh:{heightPx:F0}px;'>");
            // NOT: Band-type etiketi (PageHeader, SubDetail vs.) son kullanıcıya
            // gösterilmemeli. Designer içinde band etiketleri React tarafında
            // ayrı render ediliyor; çıktıda yer almaz.

            if (band.Type == "Detail" || band.Type == "SubDetail")
            {
                var alias = !string.IsNullOrWhiteSpace(band.DataAlias)
                    ? band.DataAlias!
                    : band.Elements
                        .Select(e => e.Binding?.Alias)
                        .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                      ?? "Detail";
                data.TryGetValue(alias, out var detailData);
                var elements = band.Elements.OrderBy(e => e.X).ToList();

                // Effective header band: SubDetail varsa SubDetailHeader, yoksa TableHeader
                var effectiveHeader = (band.Type == "SubDetail")
                    ? (subDetailHeaderBand ?? tableHeaderBand)
                    : tableHeaderBand;

                // Tablo + kolon genişlikleri tasarım mm değerlerinden — sayfaya yayılmasın.
                // Header element'leri arasındaki X-aralıklarını SPACER kolon olarak ekle
                // (PDF render ile aynı mantık) — aksi halde tasarımdaki boşluklar render'da
                // kapanır ve "Tutar" gibi sağa yaslı kolonlar sola kayar.
                var srcElems = (effectiveHeader != null && effectiveHeader.Elements.Count > 0)
                    ? effectiveHeader.Elements.OrderBy(e => e.X).ToList()
                    : elements;

                // Slot tipi: (isSpacer, widthMm, srcElement?)
                var slots = new List<(bool IsSpacer, double WidthMm, LayoutElement? El)>();
                double srcCursor = 0;
                foreach (var srcEl in srcElems)
                {
                    var gap = srcEl.X - srcCursor;
                    if (gap > 0.1) slots.Add((true, gap, null));
                    slots.Add((false, srcEl.W, srcEl));
                    srcCursor = srcEl.X + srcEl.W;
                }

                var totalWMm = slots.Sum(s => s.WidthMm);
                var totalWPx = totalWMm * MM_TO_PX;

                sb.AppendLine($"<table class='detail' style='width:{totalWPx:F0}px;table-layout:fixed'><colgroup>");
                foreach (var slot in slots)
                    sb.AppendLine($"<col style='width:{slot.WidthMm * MM_TO_PX:F0}px' />");
                sb.AppendLine("</colgroup><thead><tr>");

                // Header: spacer slot → boş <th> (border yok); diğerleri header element'inden
                if (effectiveHeader != null && effectiveHeader.Elements.Count > 0)
                {
                    foreach (var slot in slots)
                    {
                        if (slot.IsSpacer)
                        {
                            sb.AppendLine("<th style='border:none;background:transparent'></th>");
                            continue;
                        }
                        var hEl = slot.El;
                        string headerLabel;
                        if (!string.IsNullOrEmpty(hEl?.Text)) headerLabel = hEl.Text;
                        else if (hEl?.Binding != null) {
                            var resolved = ResolveFieldRaw(hEl, data);
                            headerLabel = !string.IsNullOrEmpty(resolved) ? resolved
                                : (hEl.Binding.Col ?? "");
                        }
                        else headerLabel = "";
                        var thStyle = BuildCellCssFromStyle(hEl?.Style);
                        sb.AppendLine($"<th style='{thStyle}'>{System.Net.WebUtility.HtmlEncode(headerLabel)}</th>");
                    }
                }
                else
                {
                    foreach (var el in elements)
                        sb.AppendLine($"<th>{System.Net.WebUtility.HtmlEncode(el.Text ?? el.Binding?.Col ?? "")}</th>");
                }
                sb.AppendLine("</tr></thead><tbody>");

                if (detailData != null)
                {
                    // Detail element'lerini header slot'larıyla eşle: spacer'lar atlanır,
                    // gerçek slot'lar sıradaki detail element'i alır.
                    // Zebra: bant zebraEnabled ise çift sıralara (rowIdx%2==1) bg uygulanır.
                    var colIndex = BuildColIndex(detailData);
                    var zebraOn  = band.ZebraEnabled;
                    var zebraHex = string.IsNullOrWhiteSpace(band.ZebraColor) ? "#F3F4F6" : band.ZebraColor!;

                    // ── Nesting hazırlığı: UI'da çoklu kolon eşleme destekli ──
                    var subDetailBand = doc.Bands.FirstOrDefault(b => b.Type == "SubDetail");
                    Dictionary<string, List<IReadOnlyList<object?>>>? subByKey = null;
                    List<LayoutElement>? subEls = null;
                    Dictionary<string, int>? subColIdx = null;
                    int[]? parentJoinIdxs = null;
                    if (band.Type == "Detail" && subDetailBand != null)
                    {
                        var subAlias = !string.IsNullOrWhiteSpace(subDetailBand.DataAlias)
                            ? subDetailBand.DataAlias!
                            : subDetailBand.Elements.Select(e => e.Binding?.Alias)
                                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "SubDetail";
                        var parentAliasResolved = !string.IsNullOrWhiteSpace(band.DataAlias)
                            ? band.DataAlias!
                            : band.Elements.Select(e => e.Binding?.Alias)
                                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "Detail";

                        if (joinMeta.TryGetValue(subAlias, out var jm) &&
                            string.Equals(jm.ParentAlias, parentAliasResolved, StringComparison.OrdinalIgnoreCase) &&
                            data.TryGetValue(subAlias, out var subData))
                        {
                            parentJoinIdxs = jm.Pairs
                                .Select(p => colIndex.TryGetValue(p.ParentCol, out var i) ? i : -1)
                                .ToArray();
                            subColIdx = BuildColIndex(subData);
                            var subKeyIdxs = jm.Pairs
                                .Select(p => subColIdx.TryGetValue(p.ChildCol, out var i) ? i : -1)
                                .ToArray();

                            if (parentJoinIdxs.All(i => i >= 0) && subKeyIdxs.All(i => i >= 0))
                            {
                                subByKey = new Dictionary<string, List<IReadOnlyList<object?>>>();
                                foreach (var sr in subData.Rows)
                                {
                                    var k = BuildCompositeKey(sr, subKeyIdxs);
                                    if (!subByKey.TryGetValue(k, out var list))
                                    {
                                        list = new List<IReadOnlyList<object?>>();
                                        subByKey[k] = list;
                                    }
                                    list.Add(sr);
                                }
                                subEls = subDetailBand.Elements.OrderBy(e => e.X).ToList();
                            }
                            else
                            {
                                parentJoinIdxs = null;
                            }
                        }
                    }

                    var totalSlotCount = slots.Count;
                    int rowIdx = 0;
                    foreach (var row in detailData.Rows)
                    {
                        var isZebraRow = zebraOn && (rowIdx % 2 == 1);
                        var rowStyle = isZebraRow ? $" style='background:{System.Net.WebUtility.HtmlEncode(zebraHex)}'" : "";
                        sb.AppendLine($"<tr{rowStyle}>");
                        int detailIdx = 0;
                        foreach (var slot in slots)
                        {
                            if (slot.IsSpacer)
                            {
                                // Spacer'a zebra arka planı row'dan miras alınır (transparent kalsın yeter)
                                sb.AppendLine("<td style='border:none;background:transparent'></td>");
                                continue;
                            }
                            var el = detailIdx < elements.Count ? elements[detailIdx++] : null;
                            var val = "";
                            if (el != null && el.Binding?.Col != null && colIndex.TryGetValue(el.Binding.Col, out var ci))
                                val = FormatValue(row.Count > ci ? row[ci] : null, el.Format);
                            var tdStyle = BuildCellCssFromStyle(el?.Style);
                            sb.AppendLine($"<td style='{tdStyle}'>{System.Net.WebUtility.HtmlEncode(val)}</td>");
                        }
                        sb.AppendLine("</tr>");

                        // ── Nested SubDetail (çoklu kolon composite key match'i) ──
                        if (subByKey != null && subEls != null && subColIdx != null && parentJoinIdxs != null)
                        {
                            var nestedKey = BuildCompositeKey(row, parentJoinIdxs);
                            if (subByKey.TryGetValue(nestedKey, out var matched))
                            {
                                var nestedBg = isZebraRow ? zebraHex : "#FAFAFA";
                                foreach (var subRow in matched)
                                {
                                    var pairs = new List<string>();
                                    foreach (var subEl in subEls)
                                    {
                                        var col = subEl.Binding?.Col;
                                        if (string.IsNullOrEmpty(col)) continue;
                                        if (!subColIdx.TryGetValue(col, out var sci)) continue;
                                        var v = subRow.Count > sci ? subRow[sci] : null;
                                        if (v == null || v is DBNull) continue;
                                        var fv = FormatValue(v, subEl.Format);
                                        if (string.IsNullOrWhiteSpace(fv)) continue;
                                        var label = !string.IsNullOrWhiteSpace(subEl.Text) ? subEl.Text! : col;
                                        pairs.Add($"<span style='color:#6b7280'>{System.Net.WebUtility.HtmlEncode(label)}:</span> <strong>{System.Net.WebUtility.HtmlEncode(fv)}</strong>");
                                    }
                                    if (pairs.Count == 0) continue;
                                    sb.AppendLine($"<tr><td colspan='{totalSlotCount}' style='background:{System.Net.WebUtility.HtmlEncode(nestedBg)};padding:2px 6px 2px 24px;font-size:8.5pt;border-top:none'>{string.Join("  •  ", pairs)}</td></tr>");
                                }
                            }
                        }

                        rowIdx++;
                    }
                }

                sb.AppendLine("</tbody></table>");
            }
            else
            {
                // Elements sıralanır: JSON'daki dizi sırası = z-index (son = üstte)
                foreach (var el in band.Elements)
                {
                    // Koşul değerlendirme — false dönerse element çıktıya girmez
                    if (el.Condition != null && EvaluateConditionSkip(el.Condition, data))
                        continue;

                    var xPx = el.X * 3.78;
                    var yPx = el.Y * 3.78;
                    var wPx = el.W * 3.78;
                    var hPx = el.H * 3.78;

                    var style = new StringBuilder($"left:{xPx:F0}px;top:{yPx:F0}px;width:{wPx:F0}px;height:{hPx:F0}px;");

                    var s = el.Style;
                    if (s != null)
                    {
                        if (s.FontSize > 0) style.Append($"font-size:{s.FontSize}pt;");
                        if (s.Bold) style.Append("font-weight:bold;");
                        if (s.Italic) style.Append("font-style:italic;");
                        if (!string.IsNullOrEmpty(s.Color)) style.Append($"color:{s.Color};");
                        if (!string.IsNullOrEmpty(s.BgColor) && s.BgColor != "transparent")
                            style.Append($"background:{s.BgColor};");
                        if (s.Border) style.Append("border:1px solid #999;");
                        var align = s.Align switch { "center" => "center", "right" => "right", "justify" => "justify", _ => "left" };
                        style.Append($"text-align:{align};");
                    }

                    string content;
                    if (el.Kind == "Image")
                    {
                        var src = el.ImageSrc ?? el.ImageSource;
                        if (!string.IsNullOrWhiteSpace(src))
                        {
                            // object-fit CSS ile sığdırma/sıkıştırma davranışı
                            var fit = (el.ImageFit ?? "contain").ToLowerInvariant();
                            var objectFit = fit switch
                            {
                                "stretch"  => "fill",
                                "original" => "none",
                                _          => "contain",
                            };
                            // src zaten data: ile başlıyorsa veya http ile başlıyorsa olduğu gibi koy
                            content = $"<img src=\"{System.Net.WebUtility.HtmlEncode(src)}\" style=\"object-fit:{objectFit};\" />";
                        }
                        else
                        {
                            content = "<span style='color:#888;font-size:8pt'>[Resim — kaynak yok]</span>";
                        }
                    }
                    else
                    {
                        content = el.Kind switch
                        {
                            "Label"        => System.Net.WebUtility.HtmlEncode(el.Text ?? ""),
                            "BoundField"   => ResolveField(el, data),
                            // HTML önizleme tek sürekli sayfa render eder; PDF gibi gerçek
                            // pagination yok. PDF çıktısında "Sayfa N / M" görünür, burada
                            // tek sayfa kabul edip "Sayfa 1 / 1" basıyoruz — kullanıcıya
                            // konum kavramı verir, placeholder gibi yabancı durmaz.
                            "PageNumber"   => "Sayfa 1 / 1",
                            "DateTimeNow"  => DateTime.Now.ToString("dd.MM.yyyy"),
                            "AmountInWords"=> ResolveAmountInWords(el, data),
                            "Aggregate"    => ResolveAggregate(el, data),
                            "Table"        => RenderTableHtml(el, data),
                            _              => System.Net.WebUtility.HtmlEncode(el.Text ?? "")
                        };
                    }

                    sb.AppendLine($"<div class='el' style='{style}'>{content}</div>");
                }
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RenderBandContent(IContainer container,
        LayoutBand band, IReadOnlyDictionary<string, ReportRawResult> data, decimal contentWidthMm)
    {
        // Bantlar designer'da serbestçe yerleştirilen absolute-pozisyonlu elementler içerir.
        // Row + ConstantItem yan-yana yerleşim yapıyordu — bu yüzden ÜST-ÜSTE BİNEN elementler
        // (örn. arka plan rect + üstündeki text) yan yana çiziliyor, toplam genişlik içerik
        // alanını aşıp "conflicting size constraints" fırlatıyordu. Doğru çözüm Layers():
        // PrimaryLayer bandın canvas'ını belirler, her element TranslateX/Y ile absolute olarak
        // konumlanır, JSON sırası z-index olarak korunur (sonradan eklenen üstte).
        // JSON dizisindeki sıra zaten z-index (önce eklenen alt katman, sonra eklenen üst).
        // Layers() çağrı sırası da bunu doğrudan koruyor — ekstra sıralama gerekmiyor.
        // Koşullu elementleri filtrele
        var sorted = band.Elements
            .Where(el => el.Condition == null || !EvaluateConditionSkip(el.Condition, data))
            .ToList();
        if (sorted.Count == 0) return;

        var maxMm = (double)contentWidthMm;

        // KURAL: Bant element'lerden daha kısa olmamalı. Bant tasarımdaki Height'tan kısa
        // olabilir ama element'lerin Y+H'sinden asla daha kısa olamaz. Element yüksekliğini
        // de KLİPLEME — text gerektiği kadar yer alsın, bant ona göre büyüsün.
        // Bu yüzden tüm boyutlar MinHeight ile veriliyor (max sabit Height kullanılmıyor).
        var groups = GroupByYOverlap(sorted);

        container.Column(col =>
        {
            col.Spacing(0);
            double cursorY = 0;

            foreach (var group in groups)
            {
                var groupTop    = group.Min(e => e.Y);
                var groupBottom = group.Max(e => e.Y + e.H);
                var groupHeight = groupBottom - groupTop;

                // Önceki gruptan boşluk (exact — spacer içerik yok, sadece boşluk)
                var yGap = groupTop - cursorY;
                if (yGap > 0.01)
                    col.Item().Height((float)yGap, Unit.Millimetre);

                // Grup yüksekliği EXACT (Height) — tasarımdan taşma yok.
                // Text fit etmezse QuestPDF clip eder, designer'ın dışına çıkmaz.
                col.Item().Height((float)groupHeight, Unit.Millimetre)
                    .Element(c => RenderYGroup(c, group, groupTop, maxMm, data));

                cursorY = groupBottom;
            }
        });
    }

    /// <summary>
    /// Elementleri Y-aralığına göre overlap-gruplara böler. Aynı dikey aralıkta olan elementler
    /// (Y aralıkları kesişen) aynı grupta toplanır — bunlar Row ile yan yana render edilecek.
    /// </summary>
    private static List<List<LayoutElement>> GroupByYOverlap(IReadOnlyList<LayoutElement> elements)
    {
        var byY = elements.OrderBy(e => e.Y).ToList();
        var groups = new List<List<LayoutElement>>();
        foreach (var el in byY)
        {
            // Mevcut son gruptaki herhangi bir elementle Y aralığı kesişiyorsa o gruba ekle
            if (groups.Count > 0)
            {
                var current = groups[^1];
                var currentBottom = current.Max(c => c.Y + c.H);
                if (el.Y < currentBottom - 0.01)
                {
                    current.Add(el);
                    continue;
                }
            }
            groups.Add(new List<LayoutElement> { el });
        }
        return groups;
    }

    /// <summary>
    /// Y-grubunu (aynı dikey aralıktaki elementler) render eder. X-aralıkları çakışmıyorsa
    /// Row+ConstantItem ile yan yana; çakışıyorsa Layers ile üst üste basar.
    /// </summary>
    private static void RenderYGroup(IContainer container, List<LayoutElement> group, double groupTopMm,
        double maxMm, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        // X-overlap kontrolü
        var sortedX = group.OrderBy(e => e.X).ToList();
        bool hasXOverlap = false;
        for (int i = 1; i < sortedX.Count; i++)
        {
            if (sortedX[i].X < sortedX[i - 1].X + sortedX[i - 1].W - 0.01)
            {
                hasXOverlap = true;
                break;
            }
        }

        if (!hasXOverlap)
        {
            // Yan yana — Row ile horizontal layout, Y içi konum için PaddingTop kullan
            container.Row(row =>
            {
                double cursorX = 0;
                foreach (var el in sortedX)
                {
                    var gap = el.X - cursorX;
                    if (gap > 0.01)
                        row.ConstantItem((float)gap, Unit.Millimetre);
                    // Minimum 0.1mm — ince çizgi (0.3mm) gibi tasarımlara saygı duy.
                    // Daha önce 0.5mm idi, ama o zaman 0.3mm tasarım ile parent grup
                    // arasında size constraint çakışması yaşanıyordu.
                    var width = Math.Max(el.W, 0.1);
                    var maxAllowed = maxMm - el.X;
                    if (width > maxAllowed) width = maxAllowed;
                    if (width < 0.1) continue;

                    // Element Y konumu grubun üstünden ofset → PaddingTop.
                    // Height EXACT (tasarımdan taşma yok); fazla içerik clip.
                    var topPad = el.Y - groupTopMm;
                    var height = Math.Max(el.H, 0.1);
                    var cell = row.ConstantItem((float)width, Unit.Millimetre);
                    if (topPad > 0.01)
                        cell.PaddingTop((float)topPad, Unit.Millimetre)
                            .Height((float)height, Unit.Millimetre)
                            .Element(c => RenderSingleElement(c, el, data));
                    else
                        cell.Height((float)height, Unit.Millimetre)
                            .Element(c => RenderSingleElement(c, el, data));
                    cursorX = el.X + width;
                }
            });
            return;
        }

        // X-çakışma var → Layers ile üst üste bas. JSON sırası z-order.
        // EXACT Height — tasarımdan taşma yok.
        var groupH = group.Max(e => e.Y + e.H) - groupTopMm;
        container.Width((float)maxMm, Unit.Millimetre)
                 .Height((float)groupH, Unit.Millimetre)
                 .Layers(layers =>
        {
            // PrimaryLayer'ı dış container boyutuna eşitlemek için 1/255 alpha background.
            // Canvas API 2024.3.0'da deprecated; neredeyse şeffaf renk QuestPDF'in tam
            // boyut almasını zorlar.
            layers.PrimaryLayer().Background("#01000000");

            foreach (var el in group)
            {
                var width = Math.Max(el.W, 0.1);
                var maxAllowed = maxMm - el.X;
                if (width > maxAllowed) width = maxAllowed;
                if (width < 0.1) continue;
                var topInGroup = el.Y - groupTopMm;
                var height = Math.Max(el.H, 0.1);

                layers.Layer()
                    .PaddingLeft((float)el.X, Unit.Millimetre)
                    .PaddingTop((float)topInGroup, Unit.Millimetre)
                    .AlignLeft().AlignTop()
                    .Width((float)width, Unit.Millimetre)
                    .Height((float)height, Unit.Millimetre)
                    .Element(c => RenderSingleElement(c, el, data));
            }
        });
    }

    /// <summary>
    /// Tek bir band element'ini (Label/BoundField/PageNumber/Image/etc.) verilen container'a basar.
    /// container zaten doğru boyut+pozisyonda — burada sadece içerik ve stil uygulanır.
    ///
    /// OVERFLOW POLITIKASI:
    /// QuestPDF default davranisi: kutuya sigmayan icerik (uzun text/numara) yeni sayfaya
    /// "tasinir" — band elementleri icin yanlis sonuc verir (tasarimda kutu sabit konumda;
    /// kullanici kutuyu o boyutta cizdi). Cozum: `.ShowOnce()` chaining + text icin
    /// `ClampLines(N)`. Sigmayan kisim KIRPILIR, yeni sayfaya tasinmaz.
    /// </summary>
    private static void RenderSingleElement(IContainer container, LayoutElement el,
        IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var s = el.Style;

        // Tum tek-element render'ları icin overflow guard:
        // - ShowOnce → kutu disina tasmayi engeller, yeni sayfa acmaz
        // - Image branchi de dahil tum yollar bu container'i kullanacak
        container = container.ShowOnce();

        if (el.Kind == "Image")
        {
            var imgBytes = TryDecodeImage(el.ImageSrc ?? el.ImageSource);
            if (imgBytes != null)
            {
                var fit = el.ImageFit ?? "contain";
                var img = container.Image(imgBytes);
                if (fit.Equals("stretch", StringComparison.OrdinalIgnoreCase))
                    img.FitArea();
                else if (fit.Equals("original", StringComparison.OrdinalIgnoreCase))
                    img.FitWidth();
                else
                    img.FitArea();
            }
            return;
        }

        if (el.Kind == "Table")
        {
            RenderTablePdf(container, el, data);
            return;
        }

        // Arka plan rengi varsa container'ı boyamadan önce uygula
        var cell = container;
        if (!string.IsNullOrEmpty(s?.BgColor) && !s.BgColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            cell = cell.Background(SafeColor(s.BgColor, Colors.Transparent));

        // Kenarlık (per-side, fallback eski global Border)
        if (s != null)
        {
            var legacy = s.Border;
            if (s.BorderTop    ?? legacy) cell = cell.BorderTop(0.5f).BorderColor(Colors.Grey.Darken1);
            if (s.BorderRight  ?? legacy) cell = cell.BorderRight(0.5f).BorderColor(Colors.Grey.Darken1);
            if (s.BorderBottom ?? legacy) cell = cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Darken1);
            if (s.BorderLeft   ?? legacy) cell = cell.BorderLeft(0.5f).BorderColor(Colors.Grey.Darken1);
        }

        // Şekil tipi elementler: TEXT ÇAĞIRMA.
        // Shape ve bos Label dekoratif (cizgi/kutu) — bg+border zaten yukarida
        // uygulandi; Text bypass edilir, aksi halde QuestPDF font line-height
        // tahsis edip 0.5mm cizgide "available space" hatasi firlatir.
        if (el.Kind == "Shape")
            return;
        if (el.Kind == "Label" && string.IsNullOrWhiteSpace(el.Text))
            return;

        // Barkod / QR: gercek PNG uretip Image olarak bas.
        // QR ayri bir Kind degil; el.BarcodeType == "QR" ile ayirt edilir (legacy
        // "QrCode" kind'i icin de QR uretilir — geriye uyumluluk).
        if (el.Kind is "Barcode" or "QrCode")
        {
            var rawValue = (el.Binding != null ? ResolveFieldRaw(el, data) : null)
                           ?? el.Text
                           ?? "";
            if (string.IsNullOrWhiteSpace(rawValue))
                return; // veri yoksa kutu bos kalir (tasarimcida placeholder vardi, render'da yok)

            var isQrType = el.Kind == "QrCode"
                || string.Equals(el.BarcodeType, "QR", StringComparison.OrdinalIgnoreCase);

            byte[]? png;
            try
            {
                png = isQrType
                    ? GenerateQrPng(rawValue, el.QrErrorCorrection)
                    // PureBarcode=true: ZXing kendi label'ini cizmesin — bos birak,
                    // alttaki text'i QuestPDF native olarak basacagiz (kullanici style'iyla).
                    : GenerateBarcodePng(rawValue, el.BarcodeType ?? "Code128", showLabel: false);
            }
            catch
            {
                // Gecersiz icerik (orn. EAN13'e 10 haneli sayi) — kutuyu bos birak,
                // tum sayfayi iptal ettirme.
                return;
            }

            if (png == null || png.Length == 0) return;

            // QR icin: sadece resim (yazi yok).
            // 1D barkod + ShowBarcodeText=true: Column → ust resim (FIXED yukseklik),
            //   alt text (FIXED yukseklik). Column item'lara sabit boyut vermek,
            //   QuestPDF'in text'i otomatik sonraki sayfaya tasimasini onler.
            // 1D barkod + ShowBarcodeText=false: sadece resim.
            var showLabelBelow = !isQrType && (el.ShowBarcodeText ?? true);
            if (!showLabelBelow)
            {
                cell.Image(png).FitArea();
            }
            else
            {
                // Text icin sabit alan: fontSize'a gore line-height (~0.423mm/pt) + minik
                // padding. Resim icin kalan: el.H - textH (en az 1mm). Kutu zaten
                // dis container .Height ile sabit oldugu icin toplam asilmaz.
                var fontSizePt = (s?.FontSize ?? 9f);
                if (fontSizePt < 4) fontSizePt = 4;
                var textHmm  = Math.Min(el.H - 1, Math.Max(2.5, fontSizePt * 0.45));
                var imageHmm = Math.Max(1.0, el.H - textHmm);
                cell.Column(col =>
                {
                    col.Item().Height((float)imageHmm, Unit.Millimetre)
                        .AlignCenter().Image(png).FitArea();
                    col.Item().Height((float)textHmm, Unit.Millimetre).Text(t =>
                    {
                        ApplyTextAlignment(t, s);
                        BuildTextStyle(t.Span(rawValue), s);
                    });
                });
            }
            return;
        }

        // ÖNEMLİ: Burada Padding KULLANMA.
        // Designer'da element kutusu kullanıcı tarafından X/Y/W/H ile zaten tanımlanmış;
        // ek 1mm padding küçük (5-6mm) elementlerde text yüksekliğini aşırı kısıyor
        // ve QuestPDF "available space not sufficient to render even a single line of text"
        // fırlatıp tüm sayfayı iptal ediyor. Padding gerekirse kullanıcı elementi büyütür.

        // Font otomatik shrink: el.H'ye sığmıyorsa orantılı küçült.
        // Kullanıcı element'i font line-height'ından daha kısa tasarladıysa QuestPDF
        // crash etmesin (designer'da fareyle istediğiniz boyutu serbestçe verebilir).
        var fittedFont = FitFontToHeight(s?.FontSize ?? 9f, el.H);
        // Orijinal s'i mutate etmeyip yeni effective style oluştur
        var sEffective = s is null
            ? null
            : s with { FontSize = fittedFont };

        // Maks satir sayisi: element yuksekligine / yaklasik satir yuksekligine bol.
        // QuestPDF text bunu astiginda son satira "…" ellipsis koyar (ClampLines kontrati).
        // Line-height yaklasik: fontSize * 1.2 punto = (fontSize * 1.2 / 72) inch
        //                     = (fontSize * 1.2 / 72) * 25.4 mm ≈ fontSize * 0.423 mm
        // Tek satir tasarlanan kutularda 1; daha yuksek kutularda hesapla. Min 1.
        var lineHeightMm = Math.Max(0.5, fittedFont * 0.423);
        var maxLines = Math.Max(1, (int)Math.Floor(el.H / lineHeightMm));

        // PageNumber: özel — t.CurrentPageNumber() ile dinamik
        if (el.Kind == "PageNumber")
        {
            cell.Text(t =>
            {
                ApplyTextAlignment(t, sEffective);
                t.ClampLines(maxLines);
                t.Span("Sayfa "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                t.DefaultTextStyle(x => x.FontSize(fittedFont));
            });
            return;
        }

        // Diğer tüm kindler text-based
        var text = el.Kind switch
        {
            "Label"         => el.Text ?? "",
            "BoundField"    => ResolveFieldRaw(el, data),
            "DateTimeNow"   => DateTime.Now.ToString("dd.MM.yyyy"),
            "AmountInWords" => ResolveAmountInWordsRaw(el, data),
            "Aggregate"     => ResolveAggregateRaw(el, data),
            _               => el.Text ?? ""
        };

        cell.Text(t =>
        {
            ApplyTextAlignment(t, sEffective);
            t.ClampLines(maxLines);
            BuildTextStyle(t.Span(text ?? ""), sEffective);
        });
    }

    private static void BuildTextStyle(TextSpanDescriptor span, ElementStyle? s)
    {
        if (s == null) return;
        if (s.FontSize > 0) span.FontSize(s.FontSize);
        if (s.Bold) span.Bold();
        if (s.Italic) span.Italic();
        if (!string.IsNullOrEmpty(s.Color)) span.FontColor(SafeColor(s.Color, Colors.Black));
    }

    /// <summary>
    /// Master-detail nesting için lookup: child alias → (parentAlias, joinPairs).
    /// joinPairs: birden çok kolon çifti olabilir (multi-column join). Tek kolonsa
    /// tek elemanlı liste döner.
    /// </summary>
    private static IReadOnlyDictionary<string, (string ParentAlias, (string ParentCol, string ChildCol)[] Pairs)>
        BuildJoinMeta(IReadOnlyList<DataSourceMeta>? sources)
    {
        var dict = new Dictionary<string, (string, (string, string)[])>(StringComparer.OrdinalIgnoreCase);
        if (sources == null) return dict;
        foreach (var s in sources)
        {
            if (string.IsNullOrWhiteSpace(s.ParentAlias) || string.IsNullOrWhiteSpace(s.JoinOn))
                continue;
            var pairs = ParseJoinPairs(s.JoinOn!);
            if (pairs.Length == 0) continue;
            dict[s.Alias] = (s.ParentAlias!, pairs);
        }
        return dict;
    }

    /// <summary>
    /// JoinOn string'ini kolon çiftlerine parse eder. İki format desteklenir:
    /// 1) Düz string: "KalemId" → tek kolon, parent ve child aynı ad
    /// 2) JSON array: [{"p":"id","c":"parent_id"}, ...] → çoklu kolon, ayrı adlar
    /// </summary>
    private static (string ParentCol, string ChildCol)[] ParseJoinPairs(string joinOn)
    {
        var trimmed = joinOn.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                var list = new List<(string, string)>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var p = el.TryGetProperty("p", out var pe) ? pe.GetString() : null;
                    var c = el.TryGetProperty("c", out var ce) ? ce.GetString() : null;
                    if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(c))
                        list.Add((p!, c!));
                }
                return list.ToArray();
            }
            catch { return Array.Empty<(string, string)>(); }
        }
        return new[] { (trimmed, trimmed) };
    }

    /// <summary>
    /// Çoklu kolon değerlerinden composite key üretir (lookup dictionary key'i).
    /// Boş değerler de korunur ki kısmi NULL match'ler doğru ayrışsın.
    /// </summary>
    private static string BuildCompositeKey(IReadOnlyList<object?> row, int[] colIndices)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < colIndices.Length; i++)
        {
            if (i > 0) sb.Append("||");
            var idx = colIndices[i];
            sb.Append(idx >= 0 && row.Count > idx ? (row[idx]?.ToString() ?? "") : "");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Element yüksekliğine sığacak şekilde efektif font boyutu hesaplar.
    /// Tasarlanan font yüksekliğe sığmıyorsa otomatik küçültür (min 4pt) — aksi halde
    /// QuestPDF "available space not sufficient to render even a single line of text"
    /// fırlatıp tüm sayfayı iptal eder. 1mm ≈ 2.835pt; line-height factor ≈ 1.2.
    /// </summary>
    private static float FitFontToHeight(float originalFontSize, double elementHeightMm)
    {
        if (originalFontSize <= 0) return 9f;
        if (elementHeightMm <= 0) return originalFontSize;
        var availablePt = elementHeightMm * 2.835 - 0.5;   // küçük güvenlik payı
        var requiredPt  = originalFontSize * 1.2;
        if (requiredPt <= availablePt) return originalFontSize;
        return (float)Math.Max(4.0, availablePt / 1.2);
    }

    private static void ApplyTextAlignment(TextDescriptor t, ElementStyle? s)
    {
        var align = s?.Align?.ToLowerInvariant();
        switch (align)
        {
            case "right":   t.AlignRight();   break;
            case "center":  t.AlignCenter();  break;
            case "justify": t.Justify();      break;
            default:        t.AlignLeft();    break;
        }
    }

    /// <summary>
    /// QuestPDF için color string'ini her zaman 8-karakter <c>#AARRGGBB</c> formuna
    /// normalize eder. Bu format QuestPDF 2025+ validasyonunun en güvenli giriş tipidir.
    /// "transparent", null, boş veya geçersiz hex → fallback değeri döner.
    /// </summary>
    private static string SafeColor(string? color, string fallback)
    {
        var normalized = TryNormalize(color);
        if (normalized != null) return normalized;
        // Fallback'i de normalize et (Colors.Black '#000000' olabilir; #FF000000'a çeviririz)
        return TryNormalize(fallback) ?? "#FF000000";
    }

    private static string? TryNormalize(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var c = color.Trim();
        if (c.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return null;
        if (!c.StartsWith('#')) c = "#" + c;

        var hex = c[1..].ToUpperInvariant();
        if (!hex.All(IsHexChar)) return null;

        // RGB / ARGB kısa formları → R'R'G'G'B'B' veya A'A'R'R'G'G'B'B' tam forma genişlet
        if (hex.Length == 3) hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
        else if (hex.Length == 4) hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];

        // 6-char RRGGBB → FF alpha eklenerek 8-char AARRGGBB'ye dönüştür
        if (hex.Length == 6) hex = "FF" + hex;

        if (hex.Length != 8) return null;
        return "#" + hex;
    }

    private static bool IsHexChar(char ch) =>
        (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');

    /// <summary>
    /// Resim kaynağını byte dizisine çevirir. Şu an sadece <c>data:image/...;base64,XXX</c>
    /// formundaki gömülü base64 resimleri destekler (DocDesigner uploader'ı bu formatta üretiyor).
    /// http(s) URL'leri ileride genişletilebilir (HttpClient gerekir).
    /// </summary>
    /// <summary>
    /// HTML detay tablosu hücresi için border + bgColor + align + overflow CSS'i üretir.
    /// </summary>
    private static string BuildCellCssFromStyle(ElementStyle? s)
    {
        if (s == null) return "border:none;";
        var legacyAll = s.Border == true;
        var bT = s.BorderTop    ?? legacyAll;
        var bR = s.BorderRight  ?? legacyAll;
        var bB = s.BorderBottom ?? legacyAll;
        var bL = s.BorderLeft   ?? legacyAll;

        var css = new StringBuilder();
        if (bT || bR || bB || bL)
        {
            css.Append($"border-top:{(bT ? "1px solid #999" : "none")};");
            css.Append($"border-right:{(bR ? "1px solid #999" : "none")};");
            css.Append($"border-bottom:{(bB ? "1px solid #999" : "none")};");
            css.Append($"border-left:{(bL ? "1px solid #999" : "none")};");
        }
        else
        {
            css.Append("border:none;");
        }

        if (!string.IsNullOrWhiteSpace(s.BgColor) && !s.BgColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            css.Append($"background:{s.BgColor};");

        if (!string.IsNullOrEmpty(s.Color)) css.Append($"color:{s.Color};");
        if (s.FontSize > 0) css.Append($"font-size:{s.FontSize}pt;");
        if (s.Bold)   css.Append("font-weight:bold;");
        if (s.Italic) css.Append("font-style:italic;");

        var align = s.Align switch { "center" => "center", "right" => "right", "justify" => "justify", _ => "left" };
        css.Append($"text-align:{align};");

        var overflow = (s.Overflow ?? "ellipsis").ToLowerInvariant();
        if (overflow == "wrap")
            css.Append("white-space:normal;");
        else if (overflow == "shrink")
            css.Append("white-space:nowrap;overflow:hidden;"); // gerçek shrink server-side font hesabıyla
        else
            css.Append("white-space:nowrap;overflow:hidden;text-overflow:ellipsis;");

        return css.ToString();
    }

    /// <summary>
    /// "Sığdır" (shrink-to-fit) modu için font boyutunu metin uzunluğu ve hücre genişliğine
    /// göre küçültür. Heuristic: ortalama karakter genişliği ≈ fontSize × 0.55 pt.
    /// Minimum 5pt — bu altına düşmez (okunabilirlik için).
    /// </summary>
    private static float ShrinkFontSize(string? text, double cellWidthMm, float baseFontSize)
    {
        if (string.IsNullOrEmpty(text)) return baseFontSize;
        // 1 mm ≈ 2.835 pt
        var cellWidthPt = cellWidthMm * 2.835 - 4;   // 2mm iç padding payı
        var avgCharPt = baseFontSize * 0.55;
        var requiredPt = text.Length * avgCharPt;
        if (requiredPt <= cellWidthPt) return baseFontSize;
        var scale = cellWidthPt / requiredPt;
        return Math.Max(5f, (float)(baseFontSize * scale));
    }

    private static byte[]? TryDecodeImage(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        try
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var commaIdx = src.IndexOf(',');
                if (commaIdx <= 0) return null;
                var prefix = src[..commaIdx];
                if (!prefix.Contains("base64", StringComparison.OrdinalIgnoreCase)) return null;
                var b64 = src[(commaIdx + 1)..].Trim();
                return Convert.FromBase64String(b64);
            }
            return null;   // http(s) URL desteği şimdilik yok
        }
        catch
        {
            return null;
        }
    }

    private sealed record ColDef(
        string? ColName, string? Format, double WidthMm, float FontSize, string? Color,
        // Per-side border (eski global Border bool ile birleşik resolved — render sırasında doğrudan kullanılır)
        bool BorderTop, bool BorderRight, bool BorderBottom, bool BorderLeft,
        // Hücre arkaplanı (detay elementinin bgColor'ı; null/"transparent" → arkaplan yok)
        string? BgColor,
        // Metin yaslamaları
        string? Align, string? VerticalAlign,
        // Taşma davranışı: "wrap" → alt satıra geç, "clip"/"ellipsis"/null → tek satır
        string? Overflow,
        // Spacer kolon — kullanıcının tasarımdaki kolonlar arası boşluğu temsil eder.
        // Header/body hücrelerinde içerik render edilmez (sadece tablo grid'i için yer tutar).
        bool IsSpacer = false)
    {
        public int Index { get; set; }
    }

    private static List<ColDef> BuildColumnDefs(LayoutBand? tableHeader, LayoutBand detailBand, decimal contentWidthMm)
    {
        // Kolon sayı + genişlikleri TableHeader'dan alınır (kullanıcının görsel tasarımı budur);
        // yoksa Detail'den fall back. WidthMm = element'in tasarımdaki gerçek genişliği (mm).
        // Tablo bu mm değerlerine göre absolute (ConstantColumn) çizilir — RelativeColumn
        // kullanılırsa kolonlar sayfa genişliğine orantılı dağılır ve tasarım bozulur.
        //
        // KOLONLAR ARASI BOŞLUK: header element'lerinin X+W değerleri ile bir sonraki
        // element'in X'i arasındaki farkı SPACER kolon olarak ekliyoruz. Aksi halde
        // tasarımdaki boşluk render'da kapanır ve "Tutar" gibi sağa yaslı kolonlar
        // sola kayar (kullanıcı raporu).
        var headerElems = tableHeader?.Elements.OrderBy(e => e.X).ToList();
        var detailElems = detailBand.Elements.OrderBy(e => e.X).ToList();

        var sourceElems = (headerElems != null && headerElems.Count > 0) ? headerElems : detailElems;
        if (sourceElems.Count == 0) return [];

        var defs = new List<ColDef>();
        double cursor = 0;
        for (int i = 0; i < sourceElems.Count; i++)
        {
            var srcEl = sourceElems[i];
            // 1) Bu element'ten önceki boşluğu spacer olarak ekle
            var gap = srcEl.X - cursor;
            if (gap > 0.1)
            {
                defs.Add(new ColDef(
                    ColName: null, Format: null, WidthMm: gap, FontSize: 9f, Color: null,
                    BorderTop: false, BorderRight: false, BorderBottom: false, BorderLeft: false,
                    BgColor: null, Align: null, VerticalAlign: null, Overflow: null,
                    IsSpacer: true
                ) { Index = defs.Count });
            }

            // 2) Asıl kolon
            var detail = i < detailElems.Count ? detailElems[i] : null;
            var ds = detail?.Style;
            var legacyAll = ds?.Border == true;
            defs.Add(new ColDef(
                ColName:      detail?.Binding?.Col,
                Format:       detail?.Format,
                WidthMm:      srcEl.W,
                FontSize:     ds?.FontSize ?? srcEl.Style?.FontSize ?? 9f,
                Color:        ds?.Color ?? srcEl.Style?.Color,
                BorderTop:    ds?.BorderTop    ?? legacyAll,
                BorderRight:  ds?.BorderRight  ?? legacyAll,
                BorderBottom: ds?.BorderBottom ?? legacyAll,
                BorderLeft:   ds?.BorderLeft   ?? legacyAll,
                BgColor:      ds?.BgColor,
                Align:        ds?.Align ?? srcEl.Style?.Align,
                VerticalAlign: ds?.VerticalAlign ?? srcEl.Style?.VerticalAlign,
                Overflow:     ds?.Overflow ?? "ellipsis"
            ) { Index = defs.Count });

            cursor = srcEl.X + srcEl.W;
        }
        return defs;
    }

    private static Dictionary<string, int> BuildColIndex(ReportRawResult result)
    {
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < result.ColumnNames.Count; i++)
            idx[result.ColumnNames[i]] = i;
        return idx;
    }

    private static string ResolveField(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var raw = ResolveFieldRaw(el, data);
        return System.Net.WebUtility.HtmlEncode(raw);
    }

    /// <summary>
    /// Binding'i çözer. Alias bulunamazsa FALLBACK: aynı kolon adına sahip ilk data source'u
    /// kullanır. Bu, kullanıcı veri kaynağı adlarını değiştirdiğinde / yeniden bağladığında
    /// element bindings eski adı tutarken de değer dönmesini sağlar. Tek alias varsa ona bağlanır;
    /// birden fazla source aynı kolona sahipse master role'lü olan tercih edilir (heuristic).
    /// </summary>
    private static string ResolveFieldRaw(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        if (el.Binding == null) return "";

        // 1) Tam eşleşme — alias data dict'te bulunuyorsa direkt al
        if (data.TryGetValue(el.Binding.Alias, out var result) && result.Rows.Count > 0)
        {
            var idx = BuildColIndex(result);
            if (idx.TryGetValue(el.Binding.Col, out var ci))
                return FormatValue(result.Rows[0].Count > ci ? result.Rows[0][ci] : null, el.Format);
        }

        // 2) Fallback — alias yok ya da kolon yok. Aynı kolon adına sahip data source'lardan
        // ilkini seç (kullanıcı alias rename yaptıysa eski binding'ler kırılmasın).
        foreach (var (_, alt) in data)
        {
            if (alt.Rows.Count == 0) continue;
            var altIdx = BuildColIndex(alt);
            if (altIdx.TryGetValue(el.Binding.Col, out var aci))
                return FormatValue(alt.Rows[0].Count > aci ? alt.Rows[0][aci] : null, el.Format);
        }
        return "";
    }

    private static string ResolveAmountInWords(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        // ÖNEMLİ: AmountInWords ham decimal değeri okur, ResolveField'den geçirmez.
        // Aksi halde değer önce TR culture ile "35.400,00" gibi formatlanıyor,
        // sonra InvariantCulture ile parse edilmeye çalışılıyor — `.` decimal,
        // `,` group olduğu için "35.400,00" yanlış parse oluyor ve 35400 için
        // "Üçyüz Elli Dört Milyon" gibi tamamen yanlış sonuç çıkıyor.
        var amt = ResolveDecimal(el, data);
        return amt.HasValue ? NumberToWordsTr(amt.Value) : "";
    }

    private static string ResolveAmountInWordsRaw(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var amt = ResolveDecimal(el, data);
        return amt.HasValue ? NumberToWordsTr(amt.Value) : "";
    }

    // ── Aggregate (Alt Toplam) ────────────────────────────────────────────────

    private static string ResolveAggregate(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var alias = el.AggSource ?? "master";
        if (!data.TryGetValue(alias, out var result) || result.Rows.Count == 0)
            return "";
        var colIndex = BuildColIndex(result);
        var fieldName = el.AggField ?? "";
        if (!colIndex.TryGetValue(fieldName, out var ci))
            return "";

        var values = result.Rows
            .Select(row => row.Count > ci ? row[ci] : null)
            .Select(v => v == null || v is DBNull ? (decimal?)null
                : v is decimal d ? d
                : v is double dbl ? (decimal)dbl
                : v is int i ? i
                : decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : (decimal?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        decimal result2 = (el.AggFunc ?? "SUM") switch
        {
            "COUNT" => values.Count,
            "AVG"   => values.Count > 0 ? values.Average() : 0m,
            "MIN"   => values.Count > 0 ? values.Min() : 0m,
            "MAX"   => values.Count > 0 ? values.Max() : 0m,
            _       => values.Sum(),
        };

        var formatted = string.IsNullOrEmpty(el.AggFormat)
            ? result2.ToString(new CultureInfo("tr-TR"))
            : result2.ToString(el.AggFormat, new CultureInfo("tr-TR"));

        return System.Net.WebUtility.HtmlEncode((el.AggPrefix ?? "") + formatted);
    }

    private static string ResolveAggregateRaw(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var alias = el.AggSource ?? "master";
        if (!data.TryGetValue(alias, out var result) || result.Rows.Count == 0)
            return "";
        var colIndex = BuildColIndex(result);
        var fieldName = el.AggField ?? "";
        if (!colIndex.TryGetValue(fieldName, out var ci))
            return "";

        var values = result.Rows
            .Select(row => row.Count > ci ? row[ci] : null)
            .Select(v => v == null || v is DBNull ? (decimal?)null
                : v is decimal d ? d
                : v is double dbl ? (decimal)dbl
                : v is int i ? i
                : decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : (decimal?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        decimal result2 = (el.AggFunc ?? "SUM") switch
        {
            "COUNT" => values.Count,
            "AVG"   => values.Count > 0 ? values.Average() : 0m,
            "MIN"   => values.Count > 0 ? values.Min() : 0m,
            "MAX"   => values.Count > 0 ? values.Max() : 0m,
            _       => values.Sum(),
        };

        var formatted = string.IsNullOrEmpty(el.AggFormat)
            ? result2.ToString(new CultureInfo("tr-TR"))
            : result2.ToString(el.AggFormat, new CultureInfo("tr-TR"));

        return (el.AggPrefix ?? "") + formatted;
    }

    // ── Table (Tablo) HTML render ─────────────────────────────────────────────

    private static string RenderTableHtml(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var cols = el.TableCols ?? [];
        if (cols.Count == 0) return "<span style='color:#888;font-size:8pt'>[Tablo — kolon tanımı yok]</span>";

        var alias = el.TableDataSource ?? "master";
        var border = el.TableBorderColor ?? "#e2e8f0";
        var headerBg = el.TableHeaderBgColor ?? "#f1f5f9";
        var fontSize = (el.Style?.FontSize ?? 9f).ToString(CultureInfo.InvariantCulture);
        var cellBorder = $"border:1px solid {border};padding:2px 4px;";

        var sb2 = new StringBuilder();
        sb2.Append($"<table style='width:100%;border-collapse:collapse;font-size:{fontSize}pt;'>");

        if (el.ShowHeader != false)
        {
            sb2.Append($"<tr style='background:{headerBg};font-weight:bold;'>");
            foreach (var col in cols)
                sb2.Append($"<th style='{cellBorder}text-align:{col.Align ?? "left"};'>{System.Net.WebUtility.HtmlEncode(col.Header ?? "")}</th>");
            sb2.Append("</tr>");
        }

        if (data.TryGetValue(alias, out var tableDs) && tableDs.Rows.Count > 0)
        {
            var colIndex = BuildColIndex(tableDs);
            foreach (var row in tableDs.Rows)
            {
                sb2.Append("<tr>");
                foreach (var col in cols)
                {
                    var fieldKey = col.Field ?? "";
                    // Alias mismatch: if col.Alias is set, look up that data source
                    IReadOnlyList<object?> dataRow = row;
                    IReadOnlyDictionary<string, int> idx = colIndex;
                    if (!string.IsNullOrEmpty(col.Alias) && col.Alias != alias
                        && data.TryGetValue(col.Alias, out var altDs) && altDs.Rows.Count > 0)
                    {
                        dataRow = altDs.Rows[0];
                        idx = BuildColIndex(altDs);
                    }
                    var val = idx.TryGetValue(fieldKey, out var fi) && dataRow.Count > fi
                        ? dataRow[fi]?.ToString() ?? ""
                        : "";
                    sb2.Append($"<td style='{cellBorder}text-align:{col.Align ?? "left"};'>{System.Net.WebUtility.HtmlEncode(val)}</td>");
                }
                sb2.Append("</tr>");
            }
        }

        sb2.Append("</table>");
        return sb2.ToString();
    }

    private static void RenderTablePdf(IContainer container, LayoutElement el,
        IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var cols = el.TableCols ?? [];
        if (cols.Count == 0) return;

        var alias = el.TableDataSource ?? "master";
        var border = SafeColor(el.TableBorderColor ?? "#e2e8f0", Colors.Grey.Lighten2);
        var headerBg = SafeColor(el.TableHeaderBgColor ?? "#f1f5f9", Colors.Grey.Lighten3);
        var fontSize = el.Style?.FontSize ?? 9f;

        IReadOnlyList<IReadOnlyList<object?>> rows = [];
        IReadOnlyDictionary<string, int> colIndex = new Dictionary<string, int>();
        if (data.TryGetValue(alias, out var tableDs) && tableDs.Rows.Count > 0)
        {
            rows = tableDs.Rows;
            colIndex = BuildColIndex(tableDs);
        }

        // Toplam genişliğe göre kolon oran payları hesapla
        var totalW = cols.Sum(c => c.Width > 0 ? c.Width : 30);

        container.ShowOnce().Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                foreach (var col in cols)
                    cd.RelativeColumn((float)(col.Width > 0 ? col.Width : 30) / (float)totalW);
            });

            if (el.ShowHeader != false)
            {
                foreach (var col in cols)
                {
                    t.Header(h =>
                    {
                        var cell = h.Cell().Background(headerBg)
                            .Border(0.3f).BorderColor(border)
                            .PaddingVertical(1).PaddingHorizontal(2);
                        cell.Text(col.Header ?? "").FontSize(fontSize).Bold();
                    });
                }
            }

            foreach (var row in rows)
            {
                foreach (var col in cols)
                {
                    var fieldKey = col.Field ?? "";
                    var val = colIndex.TryGetValue(fieldKey, out var fi) && row.Count > fi
                        ? row[fi]?.ToString() ?? ""
                        : "";
                    var cellContainer = t.Cell().Border(0.3f).BorderColor(border)
                        .PaddingVertical(1).PaddingHorizontal(2);
                    var aligned = (col.Align ?? "left") switch
                    {
                        "right"  => cellContainer.AlignRight(),
                        "center" => cellContainer.AlignCenter(),
                        _        => cellContainer.AlignLeft(),
                    };
                    aligned.Text(val).FontSize(fontSize);
                }
            }
        });
    }

    // ── Koşul değerlendirme ───────────────────────────────────────────────────

    /// <summary>
    /// Koşul değerlendirilmesi sonucu elementi atla (skip) mı?
    /// action="hide" → koşul doğruysa atla; action="show" → koşul yanlışsa atla.
    /// </summary>
    private static bool EvaluateConditionSkip(ElementCondition cond, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        var alias = cond.Source ?? "master";
        if (!data.TryGetValue(alias, out var result) || result.Rows.Count == 0)
            return false;
        var colIndex = BuildColIndex(result);
        var fieldName = cond.Field ?? "";
        var rawVal = colIndex.TryGetValue(fieldName, out var ci) && result.Rows[0].Count > ci
            ? result.Rows[0][ci]?.ToString() ?? ""
            : "";
        var condValue = cond.Value ?? "";

        bool conditionMet = (cond.Op ?? "eq") switch
        {
            "eq"       => string.Equals(rawVal, condValue, StringComparison.OrdinalIgnoreCase),
            "neq"      => !string.Equals(rawVal, condValue, StringComparison.OrdinalIgnoreCase),
            "gt"       => decimal.TryParse(rawVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var gd)
                       && decimal.TryParse(condValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var gc) && gd > gc,
            "lt"       => decimal.TryParse(rawVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var ld)
                       && decimal.TryParse(condValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var lc) && ld < lc,
            "contains" => rawVal.Contains(condValue, StringComparison.OrdinalIgnoreCase),
            "empty"    => string.IsNullOrWhiteSpace(rawVal),
            "notempty" => !string.IsNullOrWhiteSpace(rawVal),
            _          => false,
        };

        return (cond.Action ?? "hide") == "hide" ? conditionMet : !conditionMet;
    }

    /// <summary>
    /// Bağlanmış kolondan ham <see cref="decimal"/> değeri okur. FormatValue'dan
    /// geçmediği için culture-dependent string→decimal parse problemleri yaşanmaz.
    /// </summary>
    private static decimal? ResolveDecimal(LayoutElement el, IReadOnlyDictionary<string, ReportRawResult> data)
    {
        if (el.Binding == null) return null;
        if (!data.TryGetValue(el.Binding.Alias, out var result)) return null;
        if (result.Rows.Count == 0) return null;
        var colIndex = BuildColIndex(result);
        if (!colIndex.TryGetValue(el.Binding.Col, out var ci)) return null;
        var val = result.Rows[0].Count > ci ? result.Rows[0][ci] : null;
        if (val == null || val is DBNull) return null;
        return val switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            _ => decimal.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p1) ? p1
               : decimal.TryParse(val.ToString(), NumberStyles.Any, new CultureInfo("tr-TR"), out var p2) ? p2
               : (decimal?)null
        };
    }

    private static string FormatValue(object? val, string? format)
    {
        if (val == null || val is DBNull) return "";
        if (!string.IsNullOrEmpty(format))
        {
            if (val is decimal d) return d.ToString(format, new CultureInfo("tr-TR"));
            if (val is double dbl) return dbl.ToString(format, new CultureInfo("tr-TR"));
            if (val is DateTime dt && format.Contains('d', StringComparison.OrdinalIgnoreCase))
                return dt.ToString(format, new CultureInfo("tr-TR"));
        }
        return val.ToString() ?? "";
    }

    // ── Turkish amount-in-words ───────────────────────────────────────────────

    private static string NumberToWordsTr(decimal amount)
    {
        var whole = (long)Math.Floor(amount);
        var cents = (int)((amount - whole) * 100);
        var result = WholeToWordsTr(whole) + " Türk Lirası";
        if (cents > 0)
            result += " " + WholeToWordsTr(cents) + " Kuruş";
        return result;
    }

    private static readonly string[] Units = ["", "Bir", "İki", "Üç", "Dört", "Beş", "Altı", "Yedi", "Sekiz", "Dokuz"];
    private static readonly string[] Tens = ["", "On", "Yirmi", "Otuz", "Kırk", "Elli", "Altmış", "Yetmiş", "Seksen", "Doksan"];

    private static string WholeToWordsTr(long n)
    {
        if (n == 0) return "Sıfır";
        if (n < 0) return "Eksi " + WholeToWordsTr(-n);
        var sb = new StringBuilder();
        if (n >= 1_000_000_000) { sb.Append(WholeToWordsTr(n / 1_000_000_000) + " Milyar "); n %= 1_000_000_000; }
        if (n >= 1_000_000)     { sb.Append(WholeToWordsTr(n / 1_000_000) + " Milyon "); n %= 1_000_000; }
        if (n >= 1_000)
        {
            var thousands = n / 1_000;
            sb.Append(thousands == 1 ? "Bin " : WholeToWordsTr(thousands) + " Bin ");
            n %= 1_000;
        }
        if (n >= 100) { sb.Append(n / 100 == 1 ? "Yüz " : Units[n / 100] + "yüz "); n %= 100; }
        if (n >= 10)  { sb.Append(Tens[n / 10] + " "); n %= 10; }
        if (n > 0)    sb.Append(Units[n] + " ");
        return sb.ToString().Trim();
    }

    // ── Barkod / QR PNG uretici ──────────────────────────────────────────────
    // QRCoder + BarcodeStandard. Dpi yuksek (200) — render sirasinda FitArea
    // PNG'yi kutu boyutuna scale eder; baski net kalir. Hata durumunda caller
    // try/catch ile yakalar ve elementi atlar.

    private static byte[] GenerateQrPng(string value, string? eccLevel)
    {
        var ecc = (eccLevel ?? "M").ToUpperInvariant() switch
        {
            "L" => QRCodeGenerator.ECCLevel.L,
            "Q" => QRCodeGenerator.ECCLevel.Q,
            "H" => QRCodeGenerator.ECCLevel.H,
            _   => QRCodeGenerator.ECCLevel.M,
        };
        using var gen  = new QRCodeGenerator();
        using var data = gen.CreateQrCode(value, ecc);
        var png = new PngByteQRCode(data);
        // pixelsPerModule = 12 → QR'in net buyuklugu; FitArea scale eder.
        return png.GetGraphic(12);
    }

    private static byte[] GenerateBarcodePng(string value, string barcodeType, bool showLabel)
    {
        var format = MapBarcodeFormat(barcodeType);
        var writer = new BarcodeWriter
        {
            Format   = format,
            Renderer = new SKBitmapRenderer(),
            Options  = new EncodingOptions
            {
                Width            = 600,
                Height           = 200,
                Margin           = 2,
                PureBarcode      = !showLabel,
            },
        };
        using var bmp = writer.Write(value);
        if (bmp == null) return Array.Empty<byte>();
        using var img = SKImage.FromBitmap(bmp);
        using var skd = img.Encode(SKEncodedImageFormat.Png, 100);
        return skd?.ToArray() ?? Array.Empty<byte>();
    }

    private static BarcodeFormat MapBarcodeFormat(string? code) =>
        (code ?? "Code128").ToUpperInvariant() switch
        {
            "CODE128" => BarcodeFormat.CODE_128,
            "CODE39"  => BarcodeFormat.CODE_39,
            "EAN13"   => BarcodeFormat.EAN_13,
            "EAN8"    => BarcodeFormat.EAN_8,
            "UPCA"    => BarcodeFormat.UPC_A,
            "ITF"     => BarcodeFormat.ITF,
            "CODABAR" => BarcodeFormat.CODABAR,
            _         => BarcodeFormat.CODE_128,
        };

    // ── JSON deserialization ──────────────────────────────────────────────────

    private static LayoutDoc ParseLayout(string json) =>
        JsonSerializer.Deserialize<LayoutDoc>(json, JsonOpts) ?? throw new InvalidOperationException("Geçersiz LayoutJson.");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record LayoutDoc(
        [property: System.Text.Json.Serialization.JsonPropertyName("pageWidth")]  decimal PageWidth,
        [property: System.Text.Json.Serialization.JsonPropertyName("pageHeight")] decimal PageHeight,
        [property: System.Text.Json.Serialization.JsonPropertyName("margins")]    LayoutMargins Margins,
        [property: System.Text.Json.Serialization.JsonPropertyName("bands")]      IReadOnlyList<LayoutBand> Bands);

    private sealed record LayoutMargins(decimal Top, decimal Bottom, decimal Left, decimal Right);

    private sealed record LayoutBand(
        string Id, string Type, decimal Height,
        bool RepeatOnEveryPage,
        string? DataAlias,
        bool CanGrow,
        IReadOnlyList<LayoutElement> Elements,
        // Zebra modu — Detail/SubDetail bantlarında çift sıra satırlara
        // alternatif arka plan rengi uygular. Designer'ın "Zebra Modu" + "Zebra Rengi"
        // ayarlarından beslenir.
        [property: System.Text.Json.Serialization.JsonPropertyName("zebraEnabled")] bool ZebraEnabled = false,
        [property: System.Text.Json.Serialization.JsonPropertyName("zebraColor")]   string? ZebraColor = null);

    private sealed record LayoutElement(
        string Id, string Kind,
        double X, double Y, double W, double H,
        string? Text,
        ElementStyle? Style,
        BindingDef? Binding,
        string? Format,
        string? Expression,
        string? ShapeKind,
        [property: System.Text.Json.Serialization.JsonPropertyName("imageSrc")]  string? ImageSrc,
        [property: System.Text.Json.Serialization.JsonPropertyName("imageFit")]  string? ImageFit,
        // Geriye uyumluluk: eski LayoutJson kayıtlarında "imageSource" varsa onu da yakalayalım
        [property: System.Text.Json.Serialization.JsonPropertyName("imageSource")] string? ImageSource = null,
        // Barkod elementi metadata'si (Kind == "Barcode"). QR ayri bir kind degil —
        // barcodeType == "QR" ile ayirt edilir (frontend'le tutarli).
        [property: System.Text.Json.Serialization.JsonPropertyName("barcodeType")]       string? BarcodeType = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("showBarcodeText")]   bool?   ShowBarcodeText = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("qrErrorCorrection")] string? QrErrorCorrection = null,
        // Aggregate (Alt Toplam)
        [property: System.Text.Json.Serialization.JsonPropertyName("aggSource")] string? AggSource = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("aggField")]  string? AggField  = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("aggFunc")]   string? AggFunc   = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("aggFormat")] string? AggFormat = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("aggPrefix")] string? AggPrefix = null,
        // Table (Tablo)
        [property: System.Text.Json.Serialization.JsonPropertyName("tableCols")]          List<TableColDef>? TableCols         = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("tableDataSource")]    string?            TableDataSource   = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("showHeader")]         bool?              ShowHeader        = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("tableBorderColor")]   string?            TableBorderColor  = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("tableHeaderBgColor")] string?            TableHeaderBgColor = null,
        // Koşullu görünürlük
        [property: System.Text.Json.Serialization.JsonPropertyName("condition")] ElementCondition? Condition = null);

    private sealed record TableColDef(
        [property: System.Text.Json.Serialization.JsonPropertyName("key")]    string? Key,
        [property: System.Text.Json.Serialization.JsonPropertyName("header")] string? Header,
        [property: System.Text.Json.Serialization.JsonPropertyName("width")]  double  Width,
        [property: System.Text.Json.Serialization.JsonPropertyName("alias")]  string? Alias,
        [property: System.Text.Json.Serialization.JsonPropertyName("field")]  string? Field,
        [property: System.Text.Json.Serialization.JsonPropertyName("align")]  string? Align = "left");

    private sealed record ElementCondition(
        [property: System.Text.Json.Serialization.JsonPropertyName("source")] string? Source,
        [property: System.Text.Json.Serialization.JsonPropertyName("field")]  string? Field,
        [property: System.Text.Json.Serialization.JsonPropertyName("op")]     string? Op,
        [property: System.Text.Json.Serialization.JsonPropertyName("value")]  string? Value,
        [property: System.Text.Json.Serialization.JsonPropertyName("action")] string? Action = "hide");

    private sealed record ElementStyle(
        float FontSize, bool Bold, bool Italic, bool Underline,
        string Align, string? Color, string? BgColor, bool Border,
        [property: System.Text.Json.Serialization.JsonPropertyName("borderTop")]    bool? BorderTop    = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("borderRight")]  bool? BorderRight  = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("borderBottom")] bool? BorderBottom = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("borderLeft")]   bool? BorderLeft   = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("overflow")]      string? Overflow      = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("verticalAlign")] string? VerticalAlign = null);

    private sealed record BindingDef(string Alias, string Col);
}
