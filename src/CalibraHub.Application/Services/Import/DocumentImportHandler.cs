using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// PageComment Seq 1106, Faz 2 — Ticari belge (Satış Teklifi/Siparişi, Alış Teklifi/Siparişi,
/// İhtiyaç Kaydı) içe-aktarım handler'ı. Kaynak düz satır tablosu; her satır bir belge KALEMİdir
/// ve üzerinde belgenin başlık alanlarını da taşır. Satırlar <see cref="DocGroup.SourceDocumentNo"/>'ya
/// göre gruplanır — başlık alanları grubun İLK satırından, kalemler grubun TÜM satırlarından
/// üretilir (bkz. <see cref="RoutingImportHandler"/>, aynı desen).
///
/// Tek sınıf 5 farklı <see cref="DocumentImportProfile"/> ile Program.cs'te 5 kez DI'a
/// kaydedilir (satis_teklifi/satis_siparisi/alis_teklifi/alis_siparisi/alis_talebi) — kod
/// tekrarını önler (CLAUDE.md DRY kuralı).
///
/// MÜKERRER KORUMASI (kritik — iş cron ile tekrar tekrar çalışır): eşleştirme anahtarı
/// SourceDocumentNo + belge türü (<see cref="IDocumentRepository.FindIdBySourceDocumentNoAsync"/>).
/// Kullanıcı kararı: mevcut belgeler GÜNCELLENMEZ — eşleşen belge bulunursa grup "skip" olarak
/// raporlanır (Skipped sayacı), sessizce atlanmaz. SourceDocumentNo boş satır da tek başına
/// reddedilir (mükerrer önleme anahtarsız aktarım her turda mükerrer üretir).
///
/// Grup içi TEK kalem bile reddedilirse (malzeme/birim/kombinasyon/cari çözülemezse) grubun
/// TÜMÜ reddedilir — yarım belge yazılmaz (CLAUDE.md "sessiz kırık" kural #3 ruhu: bir kaydın
/// sessizce atlanması yerine tüm grup açıkça reddedilir).
///
/// Yazma <see cref="IDocumentService.SaveQuoteAsync"/> üzerinden yapılır — doğrudan repository
/// INSERT'i YOK; tutar hesabı/onay akışı/audit/baz-birim normalizasyonu o yolda çalışır. Belge
/// her zaman Taslak (Draft) açılır (SaveQuoteAsync varsayılanı).
///
/// KAPSAM DIŞI (bilinçli): satis_irsaliyesi / alis_irsaliyesi (stok etkili, ayrı karar bekliyor).
/// </summary>
public sealed class DocumentImportHandler : IImportTargetHandler
{
    private const int PreviewDetailLimit = 500;

    /// <summary>
    /// KDV oranı içe aktarım kataloğunda YOK (bkz. DocumentHeaderFieldCatalog.Common —
    /// DocumentEdit.cshtml ekranı da bunu ayrı bir input olarak sunmuyor; satır bazlı KDV
    /// hesaplanıyor, header TaxRate legacy alan). JS tarafı da aynı sabiti fallback olarak
    /// gönderiyor (bkz. DocumentEdit.cshtml sqSave payload yorumu "yoksa 20 gönder").
    /// </summary>
    private const decimal DefaultTaxRate = 20m;

    private readonly IDocumentService _documentService;
    private readonly IDocumentRepository _documentRepo;
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly IFinanceRepository _financeRepo;
    private readonly ILogisticsConfigurationRepository _logisticsRepo;
    private readonly ICurrencyRepository _currencyRepo;
    private readonly ISalesRepresentativeService _salesRepService;
    private readonly IPersonnelRepository _personnelRepo;
    private readonly DocumentImportProfile _profile;
    private readonly ILogger<DocumentImportHandler>? _logger;

    private bool _loaded;
    private int? _documentTypeId;
    private List<Item>? _items;
    private List<Unit>? _units;
    private List<Currency>? _currencies;
    private List<SalesRepresentativeDto>? _salesReps;
    private List<PersonnelDto>? _personnel;
    private List<Location>? _locations;
    private readonly Dictionary<string, IReadOnlyCollection<CombinationLookupRow>> _comboCache = new(StringComparer.OrdinalIgnoreCase);

    public DocumentImportHandler(
        IDocumentService documentService,
        IDocumentRepository documentRepo,
        IDocumentTypeRepository docTypeRepo,
        IFinanceRepository financeRepo,
        ILogisticsConfigurationRepository logisticsRepo,
        ICurrencyRepository currencyRepo,
        ISalesRepresentativeService salesRepService,
        IPersonnelRepository personnelRepo,
        DocumentImportProfile profile,
        ILogger<DocumentImportHandler>? logger = null)
    {
        _documentService = documentService;
        _documentRepo = documentRepo;
        _docTypeRepo = docTypeRepo;
        _financeRepo = financeRepo;
        _logisticsRepo = logisticsRepo;
        _currencyRepo = currencyRepo;
        _salesRepService = salesRepService;
        _personnelRepo = personnelRepo;
        _profile = profile;
        _logger = logger;
    }

    public string Entity => _profile.Entity;
    public string Label => _profile.Label;

    /// <summary>Bu handler her zaman insert-only'dir — eşleşen kaynak numara GÜNCELLEMEZ, "skip" raporlanır.</summary>
    public bool SupportsUpsert => false;

    /// <summary>Güncellemez ama mükerrer de üretmez: kaynak belge no + belge türü ile eşleşen belge atlanır.</summary>
    public bool PreventsDuplicateOnRerun => true;

    public Task PreloadAsync(CancellationToken ct) => EnsureLoadedAsync(ct);

    public IReadOnlyList<ImportTargetFieldDto> GetFields()
    {
        var dateFieldKey = _profile.DateFieldIsDelivery ? "DeliveryDate" : "ValidUntil";
        var dateFieldLabel = _profile.DateFieldIsDelivery ? "Teslim Tarihi" : "Geçerlilik Tarihi";

        var header = new List<ImportTargetFieldDto>
        {
            new("SourceDocumentNo", "Kaynak Belge No", "string", true, true,
                "ERP/kaynak sistemdeki belge numarası — aynı numara ikinci kez aktarılmaz (zorunlu)", MaxLength: 100),
            new("DocumentDate", "Belge Tarihi", "date", true, false, "gg.aa.yyyy (zorunlu)"),
            new(dateFieldKey, dateFieldLabel, "date", false, false, "gg.aa.yyyy (opsiyonel)"),
        };

        if (_profile.IsPurchaseRequest)
        {
            header.Add(new ImportTargetFieldDto("RequesterCode", "Talep Eden", "string", true, false,
                "Mevcut personel kodu veya adı (zorunlu)"));
            header.Add(new ImportTargetFieldDto("LocationCode", "Lokasyon", "string", false, false,
                "Mevcut lokasyon kodu veya adı (opsiyonel; verilirse eşleşmeli)"));
        }
        else
        {
            header.Add(new ImportTargetFieldDto("ContactCode", "Cari Kodu", "string", true, false,
                "Mevcut cari kartının kodu (zorunlu)"));
            header.Add(new ImportTargetFieldDto("ContactName", "Cari Adı", "string", false, false,
                "Yalnız bilgi amaçlı — eşleştirme her zaman Cari Kodu ile yapılır"));
            header.Add(new ImportTargetFieldDto("CurrencyCode", "Para Birimi", "string", false, false,
                "TRY / USD / EUR (boşsa TRY)"));
            header.Add(new ImportTargetFieldDto("ExchangeRate", "Kur", "decimal", false, false,
                "1 birim belge dövizi = X TL (TRY belgede yok sayılır; boşsa 1)"));
            header.Add(new ImportTargetFieldDto("IsVatIncluded", "KDV Dahil mi", "bool", false, false,
                "Evet / Hayır (boşsa Hayır)", new[] { "Evet", "Hayır" }));
            header.Add(new ImportTargetFieldDto("DiscountRate", "Genel İskonto %", "decimal", false, false, "Boşsa 0"));
            // KDV oranı belge BAŞLIĞINDA tutulur (DocumentLine'da KDV kolonu yok) — vergi tutarı ve
            // "KDV dahil" net geri hesabı bu orandan gelir. Eşlenmezse varsayılan %20 kullanılır;
            // kaynakta %1/%10 gibi farklı oranlı belgeler varsa bu alan MUTLAKA eşlenmelidir,
            // aksi halde vergi ve genel toplam sessizce yanlış hesaplanır.
            header.Add(new ImportTargetFieldDto("TaxRate", "KDV Oranı %", "decimal", false, false,
                $"Boşsa %{DefaultTaxRate:0.##}. Kaynakta farklı oranlar varsa eşleyin."));
            header.Add(new ImportTargetFieldDto("SalesRepName", "Temsilci", "string", false, false,
                "Mevcut satış temsilcisi adı (opsiyonel; verilirse eşleşmeli)"));
        }

        header.Add(new ImportTargetFieldDto("PaymentTerms", "Ödeme Koşulu", "string", false, false));
        header.Add(new ImportTargetFieldDto("DeliveryTerms", "Teslimat Koşulu", "string", false, false));
        header.Add(new ImportTargetFieldDto("DeliveryAddress", "Teslimat Adresi", "string", false, false));
        header.Add(new ImportTargetFieldDto("Notes", "Notlar", "string", false, false));

        // Kalem alanları — grubun TÜM satırlarından üretilir.
        header.Add(new ImportTargetFieldDto("ItemCode", "Malzeme Kodu", "string", true, false,
            "Mevcut stok kartının kodu (zorunlu)"));
        header.Add(new ImportTargetFieldDto("Quantity", "Miktar", "decimal", true, false, "Zorunlu, sıfırdan büyük"));
        header.Add(new ImportTargetFieldDto("UnitCode", "Birim", "string", false, false,
            "Mevcut birim kodu/adı (opsiyonel; boşsa malzemenin ana birimi kullanılır)"));
        header.Add(new ImportTargetFieldDto("Combination", "Kombinasyon", "string", false, false,
            "Stok kombinasyon kodu — biliniyorsa en kesin yol"));
        // Kombinasyon KODU sunucuda otomatik üretilir; dış sistemden Excel hazırlayan kullanıcı
        // onu bilemez. Bu kolon aynı kombinasyonu ÖZELLİK=DEĞER çiftleriyle tarif etmeyi sağlar.
        header.Add(new ImportTargetFieldDto("Configuration", "Konfigürasyon", "string", false, false,
            "Kombinasyon kodu yerine özellik tarifi: \"Renk=Kırmızı; Uzunluk=120\". " +
            "Kombinasyon takipli stokta Kombinasyon veya Konfigürasyon'dan biri ZORUNLU."));
        if (!_profile.IsPurchaseRequest)
        {
            header.Add(new ImportTargetFieldDto("UnitPrice", "Birim Fiyat", "decimal", false, false, "Boşsa 0"));
            header.Add(new ImportTargetFieldDto("LineDiscountRate", "Satır İskontosu %", "decimal", false, false, "Boşsa 0"));
        }
        header.Add(new ImportTargetFieldDto("LineNotes", "Satır Notu", "string", false, false));

        return header;
    }

    // ── Önizleme ─────────────────────────────────────────────────────────

    public async Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        if (_documentTypeId is null)
            return new ImportPreviewResultDto(false,
                $"Belge türü tanımlı değil: '{_profile.DocumentTypeCode}' — sistem yapılandırması eksik (dbo.DocumentType).",
                0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ImportPreviewRowDto>());

        var (keys, labels) = DisplayCols(set.MappedKeys);
        var (groups, orphanRows) = await BuildGroupsAsync(set, ct);
        var rowsByNo = IndexRows(set);

        int valid = 0, error = 0, docCount = 0;
        var detail = new List<ImportPreviewRowDto>();

        void AddDetail(int rowNo, string action, IReadOnlyList<string> errs)
        {
            if (detail.Count >= PreviewDetailLimit) return;
            var row = rowsByNo[rowNo];
            var cells = keys.Select(k => new ImportPreviewCellDto(k, row.TryGetValue(k, out var v) ? v : null)).ToList();
            detail.Add(new ImportPreviewRowDto(rowNo, action, cells, errs));
        }

        foreach (var (rowNo, err) in orphanRows)
        {
            error++;
            AddDetail(rowNo, "error", new[] { err });
        }

        foreach (var g in groups)
        {
            if (g.HeaderError is not null)
            {
                foreach (var r in g.RowNumbers) { error++; AddDetail(r, "error", new[] { $"Belge başlığı: {g.HeaderError}" }); }
                continue;
            }
            if (g.LineErrors.Count > 0)
            {
                var failingRows = g.LineErrors.Select(e => e.RowNo).ToList();
                foreach (var r in g.RowNumbers)
                {
                    error++;
                    var own = g.LineErrors.FirstOrDefault(e => e.RowNo == r).Error
                              ?? $"Bu belgedeki {string.Join(", ", failingRows)}. satır(lar)da hata olduğu için belge tümüyle reddedilecek.";
                    AddDetail(r, "error", new[] { own });
                }
                continue;
            }
            if (g.ExistingDocumentId is not null)
            {
                foreach (var r in g.RowNumbers) { valid++; AddDetail(r, "skip", Array.Empty<string>()); }
                continue;
            }
            docCount++;
            foreach (var r in g.RowNumbers) { valid++; AddDetail(r, "insert", Array.Empty<string>()); }
        }

        detail = detail.OrderBy(d => d.RowNumber).ToList();
        return new ImportPreviewResultDto(true, null, set.Rows.Count, valid, error, docCount, 0, keys, labels, detail);
    }

    // ── Aktarım ──────────────────────────────────────────────────────────

    public async Task<ImportCommitResultDto> CommitAsync(ImportRowSet set, int? userId, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        if (_documentTypeId is null)
            return new ImportCommitResultDto(false,
                $"Belge türü tanımlı değil: '{_profile.DocumentTypeCode}' — sistem yapılandırması eksik (dbo.DocumentType).",
                0, 0, 0, Array.Empty<ImportCommitRowDto>());

        var (groups, orphanRows) = await BuildGroupsAsync(set, ct);
        var results = new List<ImportCommitRowDto>();
        int inserted = 0, failed = 0, skipped = 0;

        foreach (var (rowNo, err) in orphanRows) { results.Add(Fail(rowNo, err)); failed++; }

        foreach (var g in groups)
        {
            if (g.HeaderError is not null)
            {
                foreach (var r in g.RowNumbers)
                    results.Add(Fail(r, $"Belge (kaynak no '{g.SourceDocumentNo}') açılmadı — başlık: {g.HeaderError}"));
                failed += g.RowNumbers.Count;
                continue;
            }
            if (g.LineErrors.Count > 0)
            {
                var failingRows = g.LineErrors.Select(e => e.RowNo).ToList();
                foreach (var r in g.RowNumbers)
                {
                    var own = g.LineErrors.FirstOrDefault(e => e.RowNo == r).Error;
                    var msg = own ?? $"Bu belgede (kaynak no '{g.SourceDocumentNo}') {string.Join(", ", failingRows)}. " +
                              "satır(lar)da hata olduğu için TÜM belge reddedildi — yarım belge açılmadı.";
                    results.Add(Fail(r, msg));
                }
                failed += g.RowNumbers.Count;
                continue;
            }
            if (g.ExistingDocumentId is not null)
            {
                var msg = $"Bu kaynak belge zaten aktarılmış (CalibraHub No: {g.ExistingDocumentNumber ?? $"#{g.ExistingDocumentId}"}) " +
                          "— belge güncelleme desteklenmiyor.";
                foreach (var r in g.RowNumbers)
                    results.Add(new ImportCommitRowDto(r, true, "skip", msg, g.ExistingDocumentId));
                skipped += g.RowNumbers.Count;
                continue;
            }

            try
            {
                var req = BuildSaveRequest(g);
                var (success, error, savedDoc, _) = await _documentService.SaveQuoteAsync(req, userId, "DB İçe Aktarım", ct);
                if (!success || savedDoc is null)
                {
                    _logger?.LogError(
                        "Belge içe aktarımı reddedildi (kaynak no {SourceNo}, tür {DocType}): {Error}",
                        g.SourceDocumentNo, _profile.DocumentTypeCode, error);
                    foreach (var r in g.RowNumbers)
                        results.Add(Fail(r, $"Belge kaydedilemedi (kaynak no '{g.SourceDocumentNo}'): {error}"));
                    failed += g.RowNumbers.Count;
                    continue;
                }
                inserted++;
                foreach (var r in g.RowNumbers)
                    results.Add(new ImportCommitRowDto(r, true, "insert", null, savedDoc.Id));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Belge içe aktarımı sırasında beklenmeyen hata (kaynak no {SourceNo}, tür {DocType})",
                    g.SourceDocumentNo, _profile.DocumentTypeCode);
                foreach (var r in g.RowNumbers)
                    results.Add(Fail(r, $"Belge kaydı sırasında beklenmeyen hata (kaynak no '{g.SourceDocumentNo}'): {ex.Message}"));
                failed += g.RowNumbers.Count;
            }
        }

        results = results.OrderBy(r => r.RowNumber).ToList();
        return new ImportCommitResultDto(true, null, inserted, 0, failed, results, skipped);
    }

    // ── Grup kurma (Preview + Commit ortak) ─────────────────────────────

    private async Task<(List<DocGroup> Groups, List<(int RowNo, string Error)> OrphanRows)> BuildGroupsAsync(
        ImportRowSet set, CancellationToken ct)
    {
        var groups = new Dictionary<string, DocGroup>(StringComparer.OrdinalIgnoreCase);
        var orphanRows = new List<(int RowNo, string Error)>();
        var rowNo = 0;

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNo++;

            var srcNo = ImportParse.Get(row, "SourceDocumentNo")?.Trim();
            if (string.IsNullOrWhiteSpace(srcNo))
            {
                orphanRows.Add((rowNo, "Kaynak belge no boş — mükerrer aktarım önlemesi için bu alan her satırda zorunlu; satır aktarılmadı."));
                continue;
            }

            if (!groups.TryGetValue(srcNo, out var g))
            {
                g = new DocGroup(rowNo, srcNo);
                groups[srcNo] = g;
                await ResolveHeaderAsync(row, g, ct);
            }
            g.RowNumbers.Add(rowNo);

            var (line, lineErr) = await ResolveLineAsync(row, ct);
            if (lineErr is not null) g.LineErrors.Add((rowNo, lineErr));
            else g.Lines.Add(line!);
        }

        return (groups.Values.ToList(), orphanRows);
    }

    /// <summary>Grubun başlık alanları — YALNIZCA grubun ilk satırından okunur ve çözülür.</summary>
    private async Task ResolveHeaderAsync(IReadOnlyDictionary<string, string?> row, DocGroup g, CancellationToken ct)
    {
        var docDate = ImportParse.ParseDate(ImportParse.Get(row, "DocumentDate"));
        if (docDate is null) { g.HeaderError = "Belge tarihi boş veya okunamadı."; return; }
        g.DocumentDate = docDate.Value;

        var dateFieldKey = _profile.DateFieldIsDelivery ? "DeliveryDate" : "ValidUntil";
        var dateFieldRaw = ImportParse.Get(row, dateFieldKey);
        if (!string.IsNullOrWhiteSpace(dateFieldRaw))
        {
            var parsed = ImportParse.ParseDate(dateFieldRaw);
            if (parsed is null)
            {
                g.HeaderError = $"{(_profile.DateFieldIsDelivery ? "Teslim" : "Geçerlilik")} tarihi okunamadı: '{dateFieldRaw}'.";
                return;
            }
            g.DateField = parsed;
        }

        if (_profile.IsPurchaseRequest)
        {
            var reqCode = ImportParse.Get(row, "RequesterCode")?.Trim();
            if (string.IsNullOrWhiteSpace(reqCode)) { g.HeaderError = "Talep Eden boş — İhtiyaç Kaydı için zorunlu."; return; }
            var person = _personnel!.FirstOrDefault(p =>
                string.Equals(p.Code?.Trim(), reqCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.FullName?.Trim(), reqCode, StringComparison.OrdinalIgnoreCase));
            if (person is null) { g.HeaderError = $"Talep Eden personel bulunamadı: '{reqCode}'"; return; }
            if (!person.IsActive) { g.HeaderError = $"Talep Eden personel pasif: '{reqCode}'"; return; }
            g.RequesterPersonnelId = person.Id;

            var locCode = ImportParse.Get(row, "LocationCode")?.Trim();
            if (!string.IsNullOrWhiteSpace(locCode))
            {
                var loc = _locations!.FirstOrDefault(l =>
                    string.Equals(l.LocationCode?.Trim(), locCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.LocationName?.Trim(), locCode, StringComparison.OrdinalIgnoreCase));
                if (loc is null) { g.HeaderError = $"Lokasyon bulunamadı: '{locCode}'"; return; }
                g.LocationId = loc.Id;
            }
        }
        else
        {
            var contactCode = ImportParse.Get(row, "ContactCode")?.Trim();
            if (string.IsNullOrWhiteSpace(contactCode)) { g.HeaderError = "Cari kodu boş — zorunlu."; return; }
            var contact = await _financeRepo.GetContactByCodeAsync(contactCode, ct);
            if (contact is null) { g.HeaderError = $"Cari bulunamadı: '{contactCode}'"; return; }
            if (!contact.IsActive) { g.HeaderError = $"Cari pasif: '{contactCode}'"; return; }
            g.ContactId = contact.Id;
            g.ContactName = contact.AccountTitle;

            var currencyCode = ImportParse.Get(row, "CurrencyCode")?.Trim();
            var isTry = true;
            if (!string.IsNullOrWhiteSpace(currencyCode))
            {
                var cur = _currencies!.FirstOrDefault(c => string.Equals(c.Code?.Trim(), currencyCode, StringComparison.OrdinalIgnoreCase));
                if (cur is null) { g.HeaderError = $"Döviz bulunamadı: '{currencyCode}'"; return; }
                g.CurrencyId = cur.Id;
                isTry = string.Equals(cur.Code?.Trim(), "TRY", StringComparison.OrdinalIgnoreCase);
            }

            var rate = ImportParse.ParseDecimal(ImportParse.Get(row, "ExchangeRate"));
            g.ExchangeRate = (isTry || rate is null || rate <= 0) ? 1m : rate.Value;

            g.IsVatIncluded = ImportParse.ParseBool(ImportParse.Get(row, "IsVatIncluded"));

            var taxRaw = ImportParse.ParseDecimal(ImportParse.Get(row, "TaxRate"));
            // Aralık dışı oran sessizce kırpılmaz/varsayılana düşürülmez — belge doğrulaması
            // 0-100 ister; kaynakta bozuk oran varsa belge yanlış vergiyle açılmasın diye reddedilir.
            if (taxRaw is < 0m or > 100m) { g.HeaderError = $"KDV oranı 0-100 aralığında olmalı: '{taxRaw}'"; return; }
            g.TaxRate = taxRaw ?? DefaultTaxRate;
            var discRaw = ImportParse.ParseDecimal(ImportParse.Get(row, "DiscountRate"));
            if (discRaw < 0) { g.HeaderError = "Genel iskonto negatif olamaz."; return; }
            g.DiscountRate = discRaw ?? 0m;

            var repName = ImportParse.Get(row, "SalesRepName")?.Trim();
            if (!string.IsNullOrWhiteSpace(repName))
            {
                var rep = _salesReps!.FirstOrDefault(r => string.Equals(r.RepName?.Trim(), repName, StringComparison.OrdinalIgnoreCase));
                if (rep is null) { g.HeaderError = $"Temsilci bulunamadı: '{repName}'"; return; }
                g.SalesRepId = rep.Id;
            }
        }

        g.PaymentTerms = NullIfBlank(ImportParse.Get(row, "PaymentTerms"));
        g.DeliveryTerms = NullIfBlank(ImportParse.Get(row, "DeliveryTerms"));
        g.DeliveryAddress = NullIfBlank(ImportParse.Get(row, "DeliveryAddress"));
        g.Notes = NullIfBlank(ImportParse.Get(row, "Notes"));

        // Mükerrer kontrolü — yalnız başlık geçerliyse (gereksiz sorgu yapmamak için).
        var existingId = await _documentRepo.FindIdBySourceDocumentNoAsync(g.SourceDocumentNo, _documentTypeId!.Value, ct);
        if (existingId is not null)
        {
            g.ExistingDocumentId = existingId;
            var existingDoc = await _documentRepo.GetByIdAsync(existingId.Value, ct);
            g.ExistingDocumentNumber = existingDoc?.DocumentNumber;
        }
    }

    /// <summary>Tek satırın kalem alanları. Malzeme/birim/kombinasyon çözülemezse (null, hata) döner.</summary>
    private async Task<(SaveDocumentLineRequest? Line, string? Error)> ResolveLineAsync(
        IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var itemCode = ImportParse.Get(row, "ItemCode")?.Trim();
        if (string.IsNullOrWhiteSpace(itemCode)) return (null, "Malzeme kodu boş.");
        var item = _items!.FirstOrDefault(i => string.Equals(i.Code?.Trim(), itemCode, StringComparison.OrdinalIgnoreCase));
        if (item is null) return (null, $"Malzeme bulunamadı: '{itemCode}'");

        var qty = ImportParse.ParseDecimal(ImportParse.Get(row, "Quantity"));
        if (qty is null || qty <= 0) return (null, "Miktar geçersiz (sayı ve sıfırdan büyük olmalı).");

        int? unitId = item.UnitId;
        var unitCode = ImportParse.Get(row, "UnitCode")?.Trim();
        if (!string.IsNullOrWhiteSpace(unitCode))
        {
            var unit = _units!.FirstOrDefault(u =>
                string.Equals(u.Code?.Trim(), unitCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Name?.Trim(), unitCode, StringComparison.OrdinalIgnoreCase));
            if (unit is null) return (null, $"Birim bulunamadı: '{unitCode}'");
            unitId = unit.Id;
        }

        int? configId = null;
        var combo = ImportParse.Get(row, "Combination")?.Trim();
        var configText = ImportParse.Get(row, "Configuration")?.Trim();

        // Konfigürasyon tarifi verilmiş ama stok kombinasyon takipli DEĞİLSE satır reddedilir.
        // Sessizce yok saymak, kullanıcının varyant sandığı satırı jenerik ürün olarak
        // kaydetmek demektir — siparişte yanlış ürün, fark edilmesi zor.
        if (!item.Combinations && !string.IsNullOrWhiteSpace(configText))
            return (null, $"'{itemCode}' kombinasyon takipli değil; Konfigürasyon alanı doldurulamaz.");

        if (item.Combinations)
        {
            if (string.IsNullOrWhiteSpace(combo) && string.IsNullOrWhiteSpace(configText))
                return (null, $"Kombinasyon zorunlu (stok kombinasyon takipli): '{itemCode}'. Kombinasyon kodu ya da Konfigürasyon tarifi verin.");

            var combos = await GetCombosAsync(itemCode, ct);

            if (!string.IsNullOrWhiteSpace(combo))
            {
                var hit = combos.FirstOrDefault(c => string.Equals(c.Code?.Trim(), combo, StringComparison.OrdinalIgnoreCase));
                if (hit is null) return (null, $"Kombinasyon bulunamadı: '{combo}' ({itemCode})");

                // İKİSİ DE doluysa çelişki aranır. Kod sessizce kazansaydı, tarifi yanlış
                // üretmiş bir kaynak sistem fark edilmeden yanlış varyantı sipariş ederdi:
                // belge "aktarıldı" görünür, satırda başka bir ürün durur.
                if (!string.IsNullOrWhiteSpace(configText))
                {
                    var pairs = ParseConfigurationPairs(configText!);
                    var conflict = pairs.FirstOrDefault(p => !CombinationHasPair(hit, p.Feature, p.Value));
                    if (conflict.Feature is not null)
                        return (null, $"Kombinasyon kodu '{combo}' ile Konfigürasyon tarifi çelişiyor ({itemCode}): " +
                                      $"bu kombinasyonda '{conflict.Feature}={conflict.Value}' yok.");
                }

                configId = hit.ConfigId;
            }
            else
            {
                var (resolved, error) = ResolveCombinationByConfiguration(itemCode, configText!, combos);
                if (error is not null) return (null, error);
                configId = resolved;
            }
        }

        decimal unitPrice = 0m, lineDiscount = 0m;
        if (!_profile.IsPurchaseRequest)
        {
            var priceRaw = ImportParse.ParseDecimal(ImportParse.Get(row, "UnitPrice"));
            if (priceRaw < 0) return (null, "Birim fiyat negatif olamaz.");
            unitPrice = priceRaw ?? 0m;

            var lineDiscRaw = ImportParse.ParseDecimal(ImportParse.Get(row, "LineDiscountRate"));
            if (lineDiscRaw < 0) return (null, "Satır iskontosu negatif olamaz.");
            lineDiscount = lineDiscRaw ?? 0m;
        }

        var notes = NullIfBlank(ImportParse.Get(row, "LineNotes"));

        var line = new SaveDocumentLineRequest(
            Id: null, ItemId: item.Id, UnitId: unitId,
            Quantity: qty.Value, UnitPrice: unitPrice, DiscountRate: lineDiscount,
            CombinationId: configId, LocationId: null, Notes: notes,
            CombinationDetails: null, TrackCombinations: item.Combinations, NotesPinned: false,
            RevisedFromId: null, Serials: null);

        return (line, null);
    }

    private SaveDocumentRequest BuildSaveRequest(DocGroup g) => new(
        Id: null,
        DocumentDate: g.DocumentDate,
        ValidUntil: _profile.DateFieldIsDelivery ? null : g.DateField,
        ContactId: g.ContactId,
        ContactName: g.ContactName,
        ContactAddress: null,
        SalesRepId: g.SalesRepId,
        CurrencyId: g.CurrencyId,
        DiscountRate: g.DiscountRate,
        TaxRate: g.TaxRate,
        PaymentTerms: g.PaymentTerms,
        DeliveryTerms: g.DeliveryTerms,
        DeliveryAddress: g.DeliveryAddress,
        Notes: g.Notes,
        Lines: g.Lines,
        ContactCode: null,
        DocumentTypeId: _documentTypeId,
        DeliveryDate: _profile.DateFieldIsDelivery ? g.DateField : null,
        DeliveryDays: null,
        RequesterPersonnelId: g.RequesterPersonnelId,
        FromRequestId: null,
        LocationId: g.LocationId,
        ExchangeRate: g.ExchangeRate,
        IsVatIncluded: g.IsVatIncluded,
        RateDate: null,
        SourceDocumentNo: g.SourceDocumentNo);

    // ── Yükleme / yardımcılar ────────────────────────────────────────────

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;

        var dt = await _docTypeRepo.GetByCodeAsync(_profile.DocumentTypeCode, ct);
        _documentTypeId = dt?.Id;

        _items = (await _logisticsRepo.GetItemsAsync(ct)).ToList();
        _units = (await _logisticsRepo.GetUnitsAsync(ct)).ToList();

        if (_profile.IsPurchaseRequest)
        {
            _personnel = (await _personnelRepo.ListAsync(false, false, ct)).ToList();
            _locations = (await _logisticsRepo.GetLocationsAsync(ct)).ToList();
        }
        else
        {
            _currencies = (await _currencyRepo.GetAllAsync(ct)).ToList();
            _salesReps = (await _salesRepService.GetAllAsync(ct)).ToList();
        }

        _loaded = true;
    }

    /// <summary>
    /// "Renk=Kırmızı; Uzunluk=120" tarifini kombinasyona çözer.
    ///
    /// Kural: verilen ÇİFTLERİN TAMAMINI karşılayan kombinasyonlar aranır.
    ///   · tam 1 aday  → o kullanılır
    ///   · 0 aday      → reddedilir (tarife uyan kombinasyon tanımlı değil)
    ///   · 1'den fazla → BELİRSİZ sayılır ve reddedilir
    ///
    /// Son madde kritik: 3 özellikli bir üründe yalnız 2 özellik yazılırsa birden çok
    /// kombinasyon uyar. İlkini seçmek, siparişe yanlış varyantı yazmak olurdu — üstelik
    /// belge "başarıyla aktarıldı" göründüğü için kimse fark etmezdi. Belirsizlik hata
    /// olarak bildirilir ve kullanıcıdan eksik özelliği yazması istenir.
    /// </summary>
    private static (int? ConfigId, string? Error) ResolveCombinationByConfiguration(
        string itemCode, string configText, IReadOnlyCollection<CombinationLookupRow> combos)
    {
        var pairs = ParseConfigurationPairs(configText);
        if (pairs.Count == 0)
            return (null, $"Konfigürasyon okunamadı: '{configText}'. Beklenen biçim: Özellik=Değer; Özellik=Değer");

        var matches = combos.Where(c => pairs.All(p => CombinationHasPair(c, p.Feature, p.Value))).ToList();

        if (matches.Count == 1) return (matches[0].ConfigId, null);

        if (matches.Count == 0)
        {
            // Hangi çiftin tutmadığını söyle — "bulunamadı" tek başına teşhis ettirmez.
            var culprit = pairs.FirstOrDefault(p => !combos.Any(c => CombinationHasPair(c, p.Feature, p.Value)));
            var detail = culprit.Feature is null
                ? "çiftlerin bu bileşimi tanımlı değil"
                : $"'{culprit.Feature}={culprit.Value}' hiçbir kombinasyonda yok";
            return (null, $"Konfigürasyona uyan kombinasyon yok ({itemCode}): {detail}.");
        }

        var eksik = matches
            .SelectMany(c => c.FeatureValues.Select(fv => fv.Feature))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !pairs.Any(p => string.Equals(p.Feature, f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var eksikText = eksik.Count > 0 ? " Eksik özellik(ler): " + string.Join(", ", eksik) + "." : "";
        return (null, $"Konfigürasyon {matches.Count} kombinasyona birden uyuyor ({itemCode}) — hangisi olduğu belirsiz.{eksikText}");
    }

    /// <summary>
    /// "Renk=Kırmızı; Uzunluk=120" → [(Renk, Kırmızı), (Uzunluk, 120)].
    /// Ayraçlar: çiftler arası ';' veya satır sonu, çift içinde '='.
    /// Değerin kendisinde '=' geçerse ilk '=' bölme noktasıdır (özellik adında '=' olmaz).
    /// </summary>
    private static List<(string Feature, string Value)> ParseConfigurationPairs(string text)
    {
        var result = new List<(string, string)>();
        foreach (var part in text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            if (i <= 0) continue;
            var feature = part[..i].Trim();
            var value = part[(i + 1)..].Trim();
            if (feature.Length == 0 || value.Length == 0) continue;
            result.Add((feature, value));
        }
        return result;
    }

    /// <summary>
    /// Kombinasyon bu özellik=değer çiftini taşıyor mu. Değer hem kendi metniyle hem
    /// kodu ile eşleşebilir — kullanıcı ikisinden birini yazmış olabilir.
    /// </summary>
    private static bool CombinationHasPair(CombinationLookupRow combo, string feature, string value)
        => combo.FeatureValues.Any(fv =>
            string.Equals(fv.Feature?.Trim(), feature, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(fv.Value?.Trim(), value, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(fv.ValueCode?.Trim(), value, StringComparison.OrdinalIgnoreCase)));

    private async Task<IReadOnlyCollection<CombinationLookupRow>> GetCombosAsync(string itemCode, CancellationToken ct)
    {
        if (_comboCache.TryGetValue(itemCode, out var cached)) return cached;
        var combos = await _logisticsRepo.GetCombinationsByMaterialCodeAsync(itemCode, ct);
        _comboCache[itemCode] = combos;
        return combos;
    }

    private static Dictionary<int, IReadOnlyDictionary<string, string?>> IndexRows(ImportRowSet set)
    {
        var map = new Dictionary<int, IReadOnlyDictionary<string, string?>>();
        var rowNo = 0;
        foreach (var row in set.Rows) map[++rowNo] = row;
        return map;
    }

    private (IReadOnlyList<string> Keys, IReadOnlyList<string> Labels) DisplayCols(IReadOnlyList<string> mapped)
    {
        var keys = new List<string>();
        var labels = new List<string>();
        foreach (var f in GetFields())
            if (mapped.Any(k => string.Equals(k, f.Key, StringComparison.OrdinalIgnoreCase)))
            { keys.Add(f.Key); labels.Add(f.Label); }
        return (keys, labels);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ImportCommitRowDto Fail(int rowNo, string err) => new(rowNo, false, "error", err, null);

    /// <summary>Kaynak belge numarasına göre gruplanmış satırların işlenme durumu.</summary>
    private sealed class DocGroup(int firstRow, string sourceDocumentNo)
    {
        public int FirstRow { get; } = firstRow;
        public string SourceDocumentNo { get; } = sourceDocumentNo;
        public List<int> RowNumbers { get; } = new();
        public List<SaveDocumentLineRequest> Lines { get; } = new();
        public List<(int RowNo, string Error)> LineErrors { get; } = new();

        /// <summary>Boş değilse grup TÜMÜYLE reddedilir (yarım belge yazılmaz).</summary>
        public string? HeaderError { get; set; }

        public int? ExistingDocumentId { get; set; }
        public string? ExistingDocumentNumber { get; set; }

        public DateTime DocumentDate { get; set; }
        public DateTime? DateField { get; set; }
        public int? ContactId { get; set; }
        public string? ContactName { get; set; }
        public int CurrencyId { get; set; }
        public decimal ExchangeRate { get; set; } = 1m;
        public bool IsVatIncluded { get; set; }
        public decimal DiscountRate { get; set; }
        public decimal TaxRate { get; set; } = DefaultTaxRate;
        public int? SalesRepId { get; set; }
        public int? RequesterPersonnelId { get; set; }
        public int? LocationId { get; set; }
        public string? PaymentTerms { get; set; }
        public string? DeliveryTerms { get; set; }
        public string? DeliveryAddress { get; set; }
        public string? Notes { get; set; }
    }
}

/// <summary>
/// <see cref="DocumentImportHandler"/>'ı belge türüne göre parametreleyen sabit profil.
/// Program.cs bu tipin 5 örneğini (satis_teklifi/satis_siparisi/alis_teklifi/alis_siparisi/
/// alis_talebi) ayrı <see cref="Abstractions.Services.IImportTargetHandler"/> kaydı olarak DI'a ekler.
/// </summary>
public sealed record DocumentImportProfile(
    string Entity,
    string Label,
    string DocumentTypeCode,
    /// <summary>true ise İhtiyaç Kaydı (alis_talebi) — cari/fiyat alanları yok, Talep Eden zorunlu.</summary>
    bool IsPurchaseRequest,
    /// <summary>true ise "geçerlilik/teslim tarihi" alanı DeliveryDate'e yazılır (sipariş türleri); false ise ValidUntil'e (teklif/talep).</summary>
    bool DateFieldIsDelivery)
{
    public static readonly DocumentImportProfile SalesQuote = new(
        "DOC_SALES_QUOTE", "Satış Teklifi", "satis_teklifi", false, false);

    public static readonly DocumentImportProfile SalesOrder = new(
        "DOC_SALES_ORDER", "Satış Siparişi", "satis_siparisi", false, true);

    public static readonly DocumentImportProfile PurchaseQuote = new(
        "DOC_PURCHASE_QUOTE", "Alış Teklifi", "alis_teklifi", false, false);

    public static readonly DocumentImportProfile PurchaseOrder = new(
        "DOC_PURCHASE_ORDER", "Alış Siparişi", "alis_siparisi", false, true);

    public static readonly DocumentImportProfile PurchaseRequest = new(
        "DOC_PURCHASE_REQUEST", "İhtiyaç Kaydı / Talep", "alis_talebi", true, false);
}
