using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval.EntityTypes;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _repo;
    private readonly IFinanceService _financeService;
    private readonly IDocumentTypeRepository _documentTypeRepo;
    private readonly IDocumentSourceRepository _docSourceRepo;
    private readonly IDocumentNumberService? _docNumberService;
    private readonly IApprovalFlowService? _approvalFlowService;
    private readonly ICompanyParameterService? _companyParameters;
    private readonly IDecimalSettingService? _decimalSettings;
    private readonly IAuditTrailService? _audit;
    private const string DefaultSalesQuoteTypeCode = "satis_teklifi";
    private const string DefaultSalesOrderTypeCode = "satis_siparisi";

    public DocumentService(
        IDocumentRepository repo,
        IFinanceService financeService,
        IDocumentTypeRepository documentTypeRepo,
        IDocumentSourceRepository docSourceRepo,
        IDocumentNumberService? docNumberService = null,
        IApprovalFlowService? approvalFlowService = null,
        ICompanyParameterService? companyParameters = null,
        IDecimalSettingService? decimalSettings = null,
        IAuditTrailService? audit = null)
    {
        _repo = repo;
        _financeService = financeService;
        _documentTypeRepo = documentTypeRepo;
        _docSourceRepo = docSourceRepo;
        _docNumberService = docNumberService;
        _approvalFlowService = approvalFlowService;
        _companyParameters = companyParameters;
        _decimalSettings = decimalSettings;
        _audit = audit;
    }

    // ── İşlem logu (audit trail) yardımcıları ──────────────────────────────
    // Belgenin audit entity kodu = DocumentType.Code ("satis_siparisi", "alis_talebi"...).
    // Edit ekranındaki "Değişiklik Geçmişi" sekmesi aynı kodla sorgular (_AuditTrailHost).

    private async Task<string> ResolveAuditEntityAsync(int? documentTypeId, CancellationToken ct)
    {
        if (documentTypeId is > 0)
        {
            try
            {
                var dt = await _documentTypeRepo.GetByIdAsync(documentTypeId.Value, ct);
                if (!string.IsNullOrWhiteSpace(dt?.Code)) return dt!.Code;
            }
            catch { /* audit için tip çözümlenemedi — genel koda düş */ }
        }
        return "belge";
    }

    /// <summary>Header diff snapshot'ı — yalnızca kullanıcı tarafından düzenlenen alanlar
    /// (türetilmiş SubTotal/TaxAmount/DiscountAmount gürültü olmasın diye dışarıda).</summary>
    private static object SnapHeader(Document d) => new
    {
        d.DocumentDate,
        d.ValidUntil,
        d.DeliveryDate,
        d.DeliveryDays,
        d.ContactName,
        d.ContactAddress,
        d.SalesRepId,
        d.RequesterPersonnelId,
        d.LocationId,
        d.CurrencyId,
        d.DiscountRate,
        d.TaxRate,
        d.GrandTotal,
        d.PaymentTerms,
        d.DeliveryTerms,
        d.DeliveryAddress,
        d.Notes,
    };

    /// <summary>
    /// Kalem satırları diff'i — eklenen/silinen/değişen satırları alan değişikliği
    /// listesine çevirir (yalnızca değişenler). Eşleştirme iki geçişlidir:
    ///   1) Satır Id eşleşmesi (normal upsert — Id korunur).
    ///   2) Id ile eşleşmeyenler içerik anahtarıyla (ItemId + CombinationId, LineNo sıralı)
    ///      eşleştirilir — bazı ekranlar satır Id'sini göndermediği için SaveLinesAsync
    ///      DELETE+INSERT çalışır ve Id değişir; bu geçiş olmadan basit bir miktar
    ///      düzeltmesi "silindi + eklendi" olarak raporlanırdı.
    /// </summary>
    private static List<AuditFieldChange> BuildLineChanges(
        IReadOnlyCollection<DocumentLine> oldLines, IReadOnlyCollection<DocumentLine> newLines)
    {
        var changes = new List<AuditFieldChange>();
        var oldById = oldLines.ToDictionary(l => l.Id);
        var newById = newLines.ToDictionary(l => l.Id);

        static string LineName(DocumentLine l) =>
            l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId);
        static string LineSummary(DocumentLine l) =>
            $"{AuditDiff.Normalize(l.Quantity)} {l.UnitCode ?? "birim"} × {AuditDiff.Normalize(l.UnitPrice)}";
        static string ContentKey(DocumentLine l) =>
            l.ItemId + "|" + (l.CombinationId?.ToString() ?? "");

        // 1. geçiş — Id eşleşmesi
        var pairs = new List<(DocumentLine Old, DocumentLine New)>();
        var unmatchedOld = new List<DocumentLine>();
        foreach (var o in oldLines)
        {
            if (newById.TryGetValue(o.Id, out var n)) pairs.Add((o, n));
            else unmatchedOld.Add(o);
        }
        var unmatchedNew = newLines.Where(n => !oldById.ContainsKey(n.Id)).ToList();

        // 2. geçiş — içerik anahtarı eşleşmesi (aynı malzeme+kombinasyon, LineNo sırasıyla bire bir)
        var newByKey = unmatchedNew
            .GroupBy(ContentKey)
            .ToDictionary(g => g.Key, g => new Queue<DocumentLine>(g.OrderBy(l => l.LineNo)));
        var removedLines = new List<DocumentLine>();
        foreach (var o in unmatchedOld.OrderBy(l => l.LineNo))
        {
            if (newByKey.TryGetValue(ContentKey(o), out var q) && q.Count > 0)
                pairs.Add((o, q.Dequeue()));
            else
                removedLines.Add(o);
        }
        var addedLines = newByKey.Values.SelectMany(q => q).OrderBy(l => l.LineNo);

        foreach (var o in removedLines)
            changes.Add(new AuditFieldChange($"Line[{o.Id}]", $"Kalem Silindi — {LineName(o)}", LineSummary(o), null));

        foreach (var n in addedLines)
            changes.Add(new AuditFieldChange($"Line[{n.Id}]", $"Kalem Eklendi — {LineName(n)}", null, LineSummary(n)));

        foreach (var (o, n) in pairs)
        {
            var name = LineName(n);
            void AddIfChanged(string field, string label, object? oldVal, object? newVal)
            {
                var os = AuditDiff.Normalize(oldVal);
                var ns = AuditDiff.Normalize(newVal);
                if (!string.Equals(os ?? "", ns ?? "", StringComparison.Ordinal))
                    changes.Add(new AuditFieldChange($"Line[{n.Id}].{field}", $"{name} · {label}", os, ns));
            }
            AddIfChanged("Quantity", "Miktar", o.Quantity, n.Quantity);
            AddIfChanged("UnitPrice", "Birim Fiyat", o.UnitPrice, n.UnitPrice);
            AddIfChanged("DiscountRate", "İskonto %", o.DiscountRate, n.DiscountRate);
            AddIfChanged("Unit", "Birim", o.UnitCode, n.UnitCode);
            AddIfChanged("Location", "Lokasyon", o.LocationName ?? o.LocationCode, n.LocationName ?? n.LocationCode);
            AddIfChanged("Combination", "Kombinasyon", o.CombinationCode, n.CombinationCode);
            AddIfChanged("Notes", "Not", o.Notes, n.Notes);
        }
        return changes;
    }

    /// <summary>
    /// Belge hesaplarında kullanılacak etkili ondalık ayarı. Servis kayıtlı
    /// değilse (Worker vb.) fallback (2,2,2,2,4) — davranış asla bloklanmaz.
    /// </summary>
    private async Task<EffectiveDecimalsDto> ResolveDecimalsAsync(string formCode, CancellationToken ct)
    {
        if (_decimalSettings is null) return EffectiveDecimalsDto.Fallback(formCode);
        try { return await _decimalSettings.GetEffectiveAsync(formCode, ct); }
        catch { return EffectiveDecimalsDto.Fallback(formCode); }
    }

    /// <summary>
    /// Faz P — Yeni belge numarası türetme: önce DocumentNumberRule kontrol et,
    /// kural yoksa eski default davranışa fallback (repo.GetNextDocumentNumberAsync).
    /// Yeni belge tipleri için admin DocumentNumberRule tanımlayınca otomatik etkin olur.
    /// </summary>
    private async Task<string> ResolveNextDocumentNumberAsync(
        int documentTypeId, int? contactId, int? userId, DateTime issueDate, CancellationToken ct)
    {
        if (_docNumberService is not null)
        {
            // ContactGroupId resolve — Contact var ise grubunu çek
            int? contactGroupId = null;
            if (contactId is > 0)
            {
                var contact = await _financeService.GetContactByIdAsync(contactId.Value, ct);
                contactGroupId = contact?.ContactGroupId;
            }
            var ctx = new DocumentNumberContext(
                DocumentTypeId: documentTypeId,
                ContactId:      contactId,
                ContactGroupId: contactGroupId,
                UserId:         userId,
                BranchId:       null,             // ileride
                IssueDate:      issueDate);
            var generated = await _docNumberService.GenerateNextAsync(ctx, ct);
            if (!string.IsNullOrWhiteSpace(generated)) return generated;
        }

        // Fallback: kural yok → eski sayaç (TKL{yyMM}{seq}) davranışı korunur (geriye uyum)
        return await _repo.GetNextDocumentNumberAsync(ct);
    }

    private async Task<int?> ResolveDefaultQuoteTypeIdAsync(CancellationToken ct)
    {
        var type = await _documentTypeRepo.GetByCodeAsync(DefaultSalesQuoteTypeCode, ct);
        return type?.Id;
    }

    private async Task<int?> ResolveDefaultOrderTypeIdAsync(CancellationToken ct)
    {
        var type = await _documentTypeRepo.GetByCodeAsync(DefaultSalesOrderTypeCode, ct);
        return type?.Id;
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct)
    {
        var quotes = await _repo.GetAllAsync(search, status, ct);
        return quotes.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.CurrencyId, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId,
            null,
            q.CurrencyCode, q.CurrencySymbol
        )).ToArray();
    }

    /// <summary>Bir cariye ait tum tekliflerin ozet listesi (cari karti "Verilen Teklifler" sekmesi icin).</summary>
    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesByContactAsync(int contactId, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(null, null, ct);
        return all.Where(q => q.ContactId == contactId)
            .Select(q => new DocumentListItemDto(
                q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
                q.ContactName, q.CurrencyId, q.GrandTotal,
                q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
                q.ContactId,
                q.DocumentTypeId,
                q.CurrencyCode, q.CurrencySymbol
            )).ToArray();
    }

    /// <summary>Bir cariye ait tum hareketler (belge tipi + tarih aralgi filtresi).</summary>
    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetMovementsByContactAsync(
        int contactId, int? documentTypeId, DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(null, null, ct);
        var q = all.Where(d => d.ContactId == contactId);
        if (documentTypeId is > 0) q = q.Where(d => d.DocumentTypeId == documentTypeId);
        if (fromDate.HasValue)     q = q.Where(d => d.DocumentDate >= fromDate.Value.Date);
        if (toDate.HasValue)       q = q.Where(d => d.DocumentDate <= toDate.Value.Date);
        return q.OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.Id)
            .Select(d => new DocumentListItemDto(
                d.Id, d.DocumentNumber, d.DocumentDate, d.ValidUntil,
                d.ContactName, d.CurrencyId, d.GrandTotal,
                d.Status.ToString(), d.RevisionNo, d.IsActive, d.LineCount,
                d.ContactId,
                d.DocumentTypeId,
                d.CurrencyCode, d.CurrencySymbol
            )).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct)
    {
        var docs = await _repo.GetByTypeAsync(typeCode, search, status, ct);
        return docs.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.CurrencyId, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId,
            q.DocumentTypeId,
            q.CurrencyCode, q.CurrencySymbol,
            q.RequesterPersonnelId, q.RequesterPersonnelName,
            FulfillPending: q.FulfillPending,
            FulfillPartial: q.FulfillPartial,
            FulfillFull:    q.FulfillFull
        )).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct)
    {
        // document_source tablosu yoksa olustur — modal/repo NOT EXISTS subquery'sinin
        // calismasi icin garanti gerekir. Idempotent.
        await _docSourceRepo.EnsureSchemaAsync(ct);
        var docs = await _repo.GetConvertibleQuotesAsync(fromDate, toDate, contactId, search, ct);
        return docs.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.CurrencyId, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId,
            q.DocumentTypeId,
            q.CurrencyCode, q.CurrencySymbol
        )).ToArray();
    }

    public async Task<DocumentDto?> GetQuoteByIdAsync(int id, CancellationToken ct)
    {
        var q = await _repo.GetByIdAsync(id, ct);
        return q == null ? null : MapDto(q);
    }

    public async Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(int documentId, CancellationToken ct)
    {
        var lines = await _repo.GetLinesAsync(documentId, ct);
        var result = new List<DocumentLineDto>(lines.Count);
        foreach (var ln in lines)
        {
            var details = await _repo.GetLineDetailsAsync(ln.Id, ct);
            var detailDtos = details
                .Select(d => new DocumentLineDetailDto(
                    d.Id, d.QuoteLineId, d.FeatureName, d.ValueCode, d.ValueName, d.Description, d.LineOrder))
                .ToList();
            result.Add(MapLineDto(ln, detailDtos));
        }
        return result;
    }

    public async Task<(bool Success, string? Error, DocumentDto? Quote, bool ApprovalStarted)> SaveQuoteAsync(
        SaveDocumentRequest request, int? createdById, string? startedByUser, CancellationToken ct)
    {
        // ── Cari cozumleme ─────────────────────────────────────
        // contact_id otorite kaynaktir; client ContactName gondermiyor (label),
        // ID cozumlendikten sonra Contact'tan live AccountTitle alinir (display/rapor icin).
        int? resolvedContactId = request.ContactId;
        string? resolvedContactName = request.ContactName;

        if (!resolvedContactId.HasValue && !string.IsNullOrWhiteSpace(request.ContactCode))
        {
            // Koda gore tek satir cek (search param'i ile filtreli — tum cari listesini yuklemez)
            var code = request.ContactCode.Trim();
            var matches = await _financeService.GetContactsAsync(null, code, ct);
            var exact = matches.FirstOrDefault(a =>
                a.IsActive && string.Equals(a.AccountCode, code, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                resolvedContactId = exact.Id;
                resolvedContactName = exact.AccountTitle;
            }
        }
        else if (resolvedContactId.HasValue)
        {
            // ID set edilmisse tek satirlik PK sorgusu — full list yuklemekten cok daha hizli.
            var live = await _financeService.GetContactByIdAsync(resolvedContactId.Value, ct);
            if (live != null && live.IsActive)
                resolvedContactName = live.AccountTitle;
        }

        // 2026-05-23: İhtiyaç Kaydı (alis_talebi) bir IC belge — tedarikci/musteri
        // bu asamada belli degil. Cari Kod zorunlulugu sadece diger belge tiplerinde geçerli.
        var isPurchaseRequest = false;
        if (request.DocumentTypeId.HasValue)
        {
            var dt = await _documentTypeRepo.GetByIdAsync(request.DocumentTypeId.Value, ct);
            isPurchaseRequest = string.Equals(dt?.Code, "alis_talebi", StringComparison.OrdinalIgnoreCase);
        }
        if (!isPurchaseRequest && !resolvedContactId.HasValue && string.IsNullOrWhiteSpace(resolvedContactName))
            return (false, "Cari (musteri) zorunludur. Kalem eklemeden once cari seciniz.", null, false);
        // 2026-06-01: İhtiyaç Kaydı (alis_talebi) için Talep Eden personel zorunlu —
        // onay akışı + raporlama bu personel üzerinden ilerler. Frontend de aynı
        // kontrolü uyguluyor ama API'ye direkt POST atılırsa burada yakalanır.
        if (isPurchaseRequest && (!request.RequesterPersonnelId.HasValue || request.RequesterPersonnelId.Value <= 0))
            return (false, "İhtiyaç Kaydı için 'Talep Eden' personel seçilmelidir.", null, false);
        if (request.Lines.Count == 0)
            return (false, "En az bir satir eklenmeli.", null, false);

        // Kombinasyon takibi acik olan stok icin kombinasyon ID zorunlu
        // Kombinasyon zorunlu kontrolü — mesajda satır numarası (opak ItemId değil).
        // TrackCombinations bayrağı payload'dan gelir; frontend bunu stok kartı (master)
        // değerinden doldurur (bkz. DocumentEdit save map, 2026-07-08 hizalama).
        var lineList = request.Lines.ToList();
        for (var i = 0; i < lineList.Count; i++)
        {
            var ln = lineList[i];
            if (ln.TrackCombinations && (!ln.CombinationId.HasValue || ln.CombinationId.Value <= 0))
            {
                return (false, $"{i + 1}. kalemde kombinasyon takibi açık; kombinasyon seçilmelidir.", null, false);
            }
        }

        request = request with { ContactId = resolvedContactId, ContactName = resolvedContactName };

        var isNew = !request.Id.HasValue || request.Id.Value == 0;

        // ── Bağlantı bütünlüğü koruması (güncelleme) ───────────────────────────
        // Kaynak kalem, bağlantıyı bozacak şekilde düzenlenemez/silinemez:
        //  1) İhtiyaç zinciri: karşılanan (FulfilledFromStock+ByPurchase) miktar tabandır.
        //  2) Dönüşüm zinciri (teklif→sipariş vb.): başka AKTİF belgede SourceLineId ile
        //     referans alınan kalem silinemez; miktarı türetilmiş toplamın altına inemez.
        // Bağlantıyı etkilemeyen düzenlemeler (not, fiyat, artırma...) serbesttir.
        if (!isNew && request.Id.HasValue)
        {
            var derivedAgg = await _repo.GetDerivedLineAggregatesAsync(request.Id.Value, ct);
            if (isPurchaseRequest || derivedAgg.Count > 0)
            {
                var existingLines = await GetQuoteLinesAsync(request.Id.Value, ct);
                var incomingById = request.Lines
                    .Where(l => l.Id.HasValue && l.Id.Value > 0)
                    .GroupBy(l => l.Id!.Value)
                    .ToDictionary(g => g.Key, g => g.First());
                foreach (var ex in existingLines)
                {
                    var consumed = isPurchaseRequest ? ex.FulfilledFromStock + ex.FulfilledByPurchase : 0m;
                    var hasDerived = derivedAgg.TryGetValue(ex.Id, out var da);
                    var floor = Math.Max(consumed, hasDerived ? da.QtySum : 0m);
                    if (consumed <= 0 && !hasDerived) continue;

                    var name = ex.MaterialName ?? ex.MaterialCode ?? $"#{ex.Id}";
                    var reason = hasDerived
                        ? $"bu kalemden türetilmiş {da.Count} belge satırı olduğu"
                        : $"{consumed:0.##} birim karşılandığı";
                    if (!incomingById.TryGetValue(ex.Id, out var inc))
                        return (false, $"'{name}' kalemi {reason} için silinemez. Önce bağlantılı belgeleri geri alın.", null, false);
                    if (inc.Quantity < floor)
                        return (false, $"'{name}' kalemi {reason} için miktarı {floor:0.##} altına düşürülemez (girilen: {inc.Quantity:0.##}).", null, false);
                }
            }
        }
        var effectiveTypeIdForNumber = request.DocumentTypeId ?? await ResolveDefaultQuoteTypeIdAsync(ct) ?? 0;
        var quoteNumber = isNew
            ? await ResolveNextDocumentNumberAsync(
                documentTypeId: effectiveTypeIdForNumber,
                contactId:      request.ContactId,
                userId:         null,    // current user — controller catch eder, ileride pass
                issueDate:      request.DocumentDate,
                ct)
            : "";

        // Satirlari hesapla — ondalik ayari form bazinda cozumlenir (Ayarlar → Ondalik Ayarlari)
        var decimals = await ResolveDecimalsAsync(
            isPurchaseRequest ? FormCodes.PurchaseRequest : FormCodes.SalesQuote, ct);
        var lineRequests = request.Lines.ToArray();
        decimal subTotal = 0;
        var lineEntities = new List<DocumentLine>(lineRequests.Length);
        int lineNo = 1;
        foreach (var ln in lineRequests)
        {
            var lineDiscountMultiplier = 1m - (ln.DiscountRate / 100m);
            var lineTotal = decimals.RoundAmount(ln.Quantity * ln.UnitPrice * lineDiscountMultiplier);
            subTotal += lineTotal;
            lineEntities.Add(new DocumentLine
            {
                // DocumentId 0 — UpsertAsync sonrasi quote.Id ile set edecegiz.
                // Id dolu gelirse repository UPDATE uygular; yoksa INSERT ederek yeni IDENTITY alir.
                Id = ln.Id ?? 0,
                LineNo = lineNo++,
                ItemId = ln.ItemId,
                UnitId = ln.UnitId,
                Quantity = decimals.RoundQuantity(ln.Quantity),
                UnitPrice = decimals.RoundUnitPrice(ln.UnitPrice),
                DiscountRate = decimals.RoundRate(ln.DiscountRate),
                LineTotal = lineTotal,
                CombinationId = ln.CombinationId,
                LocationId = ln.LocationId,
                Notes = ln.Notes,
                NotesPinned = ln.NotesPinned,
                RevisedFromId = ln.RevisedFromId,
            });
        }

        var discountAmount = decimals.RoundAmount(subTotal * (request.DiscountRate / 100m));
        var afterDiscount = subTotal - discountAmount;
        var taxAmount = decimals.RoundAmount(afterDiscount * (request.TaxRate / 100m));
        var grandTotal = decimals.RoundAmount(afterDiscount + taxAmount);

        // DocumentTypeId null ise varsayilan 'satis_teklifi' turune bagla
        var effectiveDocumentTypeId = request.DocumentTypeId ?? await ResolveDefaultQuoteTypeIdAsync(ct);

        // İşlem logu için eski durum snapshot'ları (yalnızca güncellemede ve audit aktifken)
        object? auditOldHeader = null;
        IReadOnlyCollection<DocumentLine>? auditOldLines = null;

        Document quote;
        if (isNew)
        {
            quote = new Document
            {
                DocumentNumber = quoteNumber,
                DocumentTypeId = effectiveDocumentTypeId,
                DocumentDate = request.DocumentDate,
                ValidUntil = request.ValidUntil,
                DeliveryDate = request.DeliveryDate,    // Faz M
                DeliveryDays = request.DeliveryDays,    // Faz M
                ContactId = request.ContactId,
                ContactName = request.ContactName,
                ContactAddress = request.ContactAddress,
                SalesRepId = request.SalesRepId,
                RequesterPersonnelId = request.RequesterPersonnelId,
                LocationId = request.LocationId,
                CurrencyId = request.CurrencyId > 0 ? request.CurrencyId : 1,
                SubTotal = Math.Round(subTotal, 4),
                DiscountRate = request.DiscountRate,
                DiscountAmount = discountAmount,
                TaxRate = request.TaxRate,
                TaxAmount = taxAmount,
                GrandTotal = Math.Round(grandTotal, 4),
                PaymentTerms = request.PaymentTerms,
                DeliveryTerms = request.DeliveryTerms,
                DeliveryAddress = request.DeliveryAddress,
                Notes = request.Notes,
                CreatedById = createdById,
                Status = DocumentStatus.Draft
            };
        }
        else
        {
            var existing = await _repo.GetByIdAsync(request.Id!.Value, ct)
                    ?? throw new InvalidOperationException("Teklif bulunamadi.");

            // GetByIdAsync zaten line_count'u tek sorguda getiriyor — tekrar lines cekmeye gerek yok.
            if (existing.LineCount > 0 && existing.ContactId != request.ContactId)
                return (false, "Kalem girilmis belgenin cari kodu degistirilemez.", null, false);

            // İşlem logu: mutasyondan ÖNCE eski header + kalem snapshot'ı al
            if (_audit is not null)
            {
                auditOldHeader = SnapHeader(existing);
                try { auditOldLines = await _repo.GetLinesAsync(request.Id!.Value, ct); }
                catch { auditOldLines = null; }
            }

            existing.DocumentTypeId = request.DocumentTypeId ?? existing.DocumentTypeId ?? effectiveDocumentTypeId;
            existing.DocumentDate = request.DocumentDate;
            existing.ValidUntil = request.ValidUntil;
            existing.DeliveryDate = request.DeliveryDate;    // Faz M
            existing.DeliveryDays = request.DeliveryDays;    // Faz M
            existing.ContactId = request.ContactId;
            existing.ContactName = request.ContactName;
            existing.ContactAddress = request.ContactAddress;
            existing.SalesRepId = request.SalesRepId;
            existing.RequesterPersonnelId = request.RequesterPersonnelId;
            existing.LocationId = request.LocationId;
            existing.CurrencyId = request.CurrencyId > 0 ? request.CurrencyId : 1;
            existing.SubTotal = Math.Round(subTotal, 4);
            existing.DiscountRate = request.DiscountRate;
            existing.DiscountAmount = discountAmount;
            existing.TaxRate = request.TaxRate;
            existing.TaxAmount = taxAmount;
            existing.GrandTotal = Math.Round(grandTotal, 4);
            existing.PaymentTerms = request.PaymentTerms;
            existing.DeliveryTerms = request.DeliveryTerms;
            existing.DeliveryAddress = request.DeliveryAddress;
            existing.Notes = request.Notes;
            existing.UpdatedAt = DateTime.Now;
            quote = existing;
        }

        var savedId = await _repo.UpsertAsync(quote, ct);

        // Upsert sonrasi yeni Id'yi entity'ye ata (init-only oldugu icin yeni instance)
        if (isNew)
        {
            quote = new Document
            {
                Id = savedId,
                DocumentNumber = quote.DocumentNumber,
                DocumentTypeId = quote.DocumentTypeId,
                DocumentDate = quote.DocumentDate,
                ValidUntil = quote.ValidUntil,
                DeliveryDate = quote.DeliveryDate,    // Faz M
                DeliveryDays = quote.DeliveryDays,    // Faz M
                ContactId = quote.ContactId,
                ContactName = quote.ContactName,
                ContactAddress = quote.ContactAddress,
                ContactCode = quote.ContactCode,
                SalesRepId = quote.SalesRepId,
                RequesterPersonnelId = quote.RequesterPersonnelId,
                CurrencyId = quote.CurrencyId,
                SubTotal = quote.SubTotal,
                DiscountRate = quote.DiscountRate,
                DiscountAmount = quote.DiscountAmount,
                TaxRate = quote.TaxRate,
                TaxAmount = quote.TaxAmount,
                GrandTotal = quote.GrandTotal,
                PaymentTerms = quote.PaymentTerms,
                DeliveryTerms = quote.DeliveryTerms,
                DeliveryAddress = quote.DeliveryAddress,
                Status = quote.Status,
                RevisionNo = quote.RevisionNo,
                ParentDocumentId = quote.ParentDocumentId,
                Notes = quote.Notes,
                CreatedById = quote.CreatedById,
                CreatedAt = quote.CreatedAt,
                UpdatedAt = quote.UpdatedAt,
                IsActive = quote.IsActive,
                LineCount = quote.LineCount,
            };
        }

        // Satirlari kaydet — DocumentId'yi yeni Id ile set et.
        // ÖNEMLI: Id'yi de kopyala — aksi halde UPSERT'te tum satirlar INSERT olarak
        // algilanir, eski Id'ler silinir ve WidgetTra kayitlari orphan kalir.
        var finalLines = lineEntities.Select(ln => new DocumentLine
        {
            Id = ln.Id,
            DocumentId = quote.Id,
            LineNo = ln.LineNo,
            ItemId = ln.ItemId,
            UnitId = ln.UnitId,
            Quantity = ln.Quantity,
            UnitPrice = ln.UnitPrice,
            DiscountRate = ln.DiscountRate,
            LineTotal = ln.LineTotal,
            CombinationId = ln.CombinationId,
            LocationId = ln.LocationId,
            Notes = ln.Notes,
            NotesPinned = ln.NotesPinned,
            RevisedFromId = ln.RevisedFromId,
        }).ToArray();

        await _repo.SaveLinesAsync(quote.Id, finalLines, ct);

        // İhtiyaç Kaydı → türetilen belge köprüsü. Sadece yeni belgede (isNew) ve
        // FromRequestId verilmişse çalışır; mevcut belge güncellemelerinde atlenir.
        if (isNew && request.FromRequestId.HasValue && request.FromRequestId.Value > 0)
        {
            await _docSourceRepo.EnsureSchemaAsync(ct);
            await _docSourceRepo.AddAsync(quote.Id, request.FromRequestId.Value, ct);
        }

        // Satir detaylarini (ozellik-deger-aciklama) kaydet — herhangi bir satirda
        // detay varsa line ID'leri icin ek sorgu at. Cogu teklif detaysiz (no-op save)
        // bu yol hic calismaz ve 1 RT + N RT kazanilir.
        var hasAnyDetails = lineRequests.Any(r => r.CombinationDetails != null && r.CombinationDetails.Count > 0);
        if (hasAnyDetails)
        {
            var savedLines = await _repo.GetLinesAsync(quote.Id, ct);
            var byLineNo = savedLines.ToDictionary(l => l.LineNo);
            for (int i = 0; i < lineRequests.Length; i++)
            {
                var reqLine = lineRequests[i];
                var lineNoForThis = i + 1;
                if (!byLineNo.TryGetValue(lineNoForThis, out var savedLine)) continue;

                var details = (reqLine.CombinationDetails ?? new List<SaveQuoteLineDetailItem>())
                    .Select((d, idx) => new DocumentLineDetail
                    {
                        QuoteLineId = savedLine.Id,
                        FeatureName = d.FeatureName,
                        ValueCode = d.ValueCode,
                        ValueName = d.ValueName,
                        Description = d.Description,
                        LineOrder = d.LineOrder > 0 ? d.LineOrder : (idx + 1),
                    })
                    .ToList();

                await _repo.SaveLineDetailsAsync(savedLine.Id, details, ct);
            }
        }

        // Yeni belgede aktif bir onay akışı varsa otomatik başlat.
        // Hata akışı durdurmaz — belge zaten kaydedildi, akış başlatılamazsa sessizce geçer.
        var approvalStarted = false;
        if (isNew && _approvalFlowService is not null)
        {
            try
            {
                // Belge tipinden spesifik kind çözümle (SalesQuote/PurchaseOrder/...).
                // Spesifik akışlar da otomatik tetiklenir; tip çözümlenemezse wildcard'a düşer.
                var kind = DocumentEntityTypes.WildcardKind;
                if (quote.DocumentTypeId.HasValue)
                {
                    var docType = await _documentTypeRepo.GetByIdAsync(quote.DocumentTypeId.Value, ct);
                    kind = DocumentEntityTypes.ResolveKind(docType?.Code);
                }

                // Şirket parametresi: belge türü bazında onay kapalıysa otomatik başlatma.
                // Parametre tanımsızsa açık kabul edilir (geriye uyum). Manuel "Onaya Gönder"
                // yolu bu parametreden etkilenmez — açık kullanıcı niyeti her zaman geçerli.
                var approvalEnabled = true;
                if (_companyParameters is not null && kind != DocumentEntityTypes.WildcardKind)
                {
                    approvalEnabled = await _companyParameters.GetBoolAsync(
                        ApprovalParameters.FormCode, ApprovalParameters.EnabledKey(kind), ct) ?? true;
                }

                if (approvalEnabled)
                {
                    var flow = await _approvalFlowService.MatchFlowAsync(
                        kind, quote.GrandTotal, null, null, ct);
                    if (flow is not null)
                    {
                        await _approvalFlowService.StartAsync(
                            new StartApprovalRequest(
                                DocumentId:      quote.Id,
                                FlowId:          flow.Id,
                                StartedBy:       startedByUser ?? "system",
                                StartedByUserId: createdById),
                            ct);
                        approvalStarted = true;
                    }
                }
            }
            catch
            {
                // Akış başlatma hatası belge kaydını iptal etmez
            }
        }

        // ── İşlem logu — yeni kayıt: Insert; güncelleme: yalnızca değişen alanlar ──
        if (_audit is not null)
        {
            try
            {
                var auditEntity = await ResolveAuditEntityAsync(quote.DocumentTypeId, ct);
                if (isNew)
                {
                    _audit.LogInsert(auditEntity, quote.Id, quote.DocumentNumber,
                        detail: $"{finalLines.Length} kalem · Genel Toplam {AuditDiff.Normalize(quote.GrandTotal)}");
                }
                else
                {
                    var changes = new List<AuditFieldChange>();
                    if (auditOldHeader is not null)
                        changes.AddRange(AuditDiff.Compute(auditOldHeader, SnapHeader(quote), auditEntity));
                    if (auditOldLines is not null)
                    {
                        var newLinesForAudit = await _repo.GetLinesAsync(quote.Id, ct);
                        changes.AddRange(BuildLineChanges(auditOldLines, newLinesForAudit));
                    }
                    _audit.LogChanges(auditEntity, quote.Id, quote.DocumentNumber, changes);
                }
            }
            catch { /* audit yazımı belge kaydını asla bozmaz */ }
        }

        return (true, null, MapDto(quote), approvalStarted);
    }

    public async Task<(bool Ok, string? Error)> DeleteQuoteAsync(int id, CancellationToken ct)
    {
        // 1) Karşılanmış kalem içeren belge silinemez (İhtiyaç Kaydı zinciri koruması).
        //    Diğer belge türlerinde Fulfilled* = 0 olduğundan bu kontrol no-op'tur.
        var lines = await GetQuoteLinesAsync(id, ct);
        if (lines.Any(l => l.FulfilledFromStock + l.FulfilledByPurchase > 0))
            return (false, "Bu belge karşılanmış kalem(ler) içerdiği için silinemez. Önce karşılama belgelerini geri alın.");

        // 2) Bu belgeden türetilmiş AKTİF belge varsa (DocumentSource: teklif→sipariş,
        //    İhtiyaç→talep/fiş...) kaynak silinemez — bağlantı bozulur. Türetilenler
        //    silinirse (soft-delete) kaynak yeniden silinebilir hale gelir.
        var derivedIds = await _docSourceRepo.GetDerivedDocumentIdsAsync(id, ct);
        if (derivedIds.Count > 0)
        {
            var activeNos = new List<string>();
            foreach (var did in derivedIds)
            {
                var d = await GetQuoteByIdAsync(did, ct);
                if (d is { IsActive: true }) activeNos.Add(d.DocumentNumber);
            }
            if (activeNos.Count > 0)
                return (false, $"Bu belgeden türetilmiş belge(ler) var: {string.Join(", ", activeNos)}. " +
                               "Kaynak belge silinemez; önce türetilmiş belgeleri silin/iptal edin.");
        }

        // İşlem logu için silinen belgenin kimliğini silmeden ÖNCE al
        Document? docForAudit = null;
        if (_audit is not null)
        {
            try { docForAudit = await _repo.GetByIdAsync(id, ct); } catch { }
        }

        await _repo.DeleteAsync(id, ct);

        if (_audit is not null && docForAudit is not null)
        {
            var auditEntity = await ResolveAuditEntityAsync(docForAudit.DocumentTypeId, ct);
            _audit.LogDelete(auditEntity, id, docForAudit.DocumentNumber);
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ChangeStatusAsync(int id, string newStatus, CancellationToken ct)
    {
        if (!Enum.TryParse<DocumentStatus>(newStatus, out _))
            return (false, "Gecersiz durum.");

        // İşlem logu için eski durumu değişiklikten ÖNCE oku
        Document? docForAudit = null;
        if (_audit is not null)
        {
            try { docForAudit = await _repo.GetByIdAsync(id, ct); } catch { }
        }

        await _repo.UpdateStatusAsync(id, newStatus, ct);

        if (_audit is not null && docForAudit is not null &&
            !string.Equals(docForAudit.Status.ToString(), newStatus, StringComparison.OrdinalIgnoreCase))
        {
            var auditEntity = await ResolveAuditEntityAsync(docForAudit.DocumentTypeId, ct);
            _audit.LogChanges(auditEntity, id, docForAudit.DocumentNumber,
                [new AuditFieldChange("Status", "Durum", docForAudit.Status.ToString(), newStatus)]);
        }
        return (true, null);
    }

    public Task<string> GetNextDocumentNumberAsync(CancellationToken ct) => _repo.GetNextDocumentNumberAsync(ct);

    private static DocumentDto MapDto(Document q) => new(
        q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
        q.ContactId, q.ContactName, q.ContactAddress,
        q.SalesRepId,
        q.CurrencyId, q.SubTotal, q.DiscountRate, q.DiscountAmount,
        q.TaxRate, q.TaxAmount, q.GrandTotal,
        q.PaymentTerms, q.DeliveryTerms, q.DeliveryAddress,
        q.Status.ToString(), q.RevisionNo, q.ParentDocumentId, q.Notes,
        q.CreatedById, q.CreatedAt, q.UpdatedAt, q.IsActive,
        q.ContactCode,
        q.DocumentTypeId,
        q.DeliveryDate,        // Faz M
        q.DeliveryDays,        // Faz M
        q.CurrencyCode, q.CurrencySymbol,
        q.RequesterPersonnelId, q.RequesterPersonnelName,
        q.LocationId, q.LocationName);

    /// <summary>
    /// Satir revizyonu — repository katmanina delege eder. Widget degerlerinin
    /// kopyalanmasi bu service sinifinda DEGIL (WidgetService farkli), controller
    /// akisinda yapilir.
    /// </summary>
    public Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct)
        => _repo.ReviseLineAsync(parentLineId, description, ct);

    /// <summary>
    /// Tekliflerden cari bazli siparis(ler) uretir.
    ///   1) Tum kaynak teklifleri yukle + dogrula (Approved + henuz consume edilmemis)
    ///   2) ContactId bazinda grupla — her grup icin tek bir Document (type=satis_siparisi) olustur
    ///   3) Tum gruptaki teklifin satirlarini siparise clone et (LineNo resequence + Notes'a kaynak ekle)
    ///   4) Kombinasyon detaylarini ayrica kopyala
    ///   5) document_source koprusu kayit ekle, kaynak teklif statusunu Converted yap
    /// Service-level transaction yok; document_source UNIQUE INDEX cift insert riskini engeller.
    /// </summary>
    public async Task<CreateOrdersFromQuotesResult> CreateOrdersFromQuotesAsync(
        CreateOrdersFromQuotesRequest req, int? createdById, CancellationToken ct)
    {
        if (req.QuoteIds == null || req.QuoteIds.Count == 0)
            return new CreateOrdersFromQuotesResult(false, "En az bir teklif secilmelidir.", 0, Array.Empty<int>());

        await _docSourceRepo.EnsureSchemaAsync(ct);

        var orderTypeId = await ResolveDefaultOrderTypeIdAsync(ct);
        if (!orderTypeId.HasValue)
            return new CreateOrdersFromQuotesResult(false, "'satis_siparisi' belge tipi bulunamadi.", 0, Array.Empty<int>());

        var quoteTypeId = await ResolveDefaultQuoteTypeIdAsync(ct);

        // ── 1) Yukle + dogrula ──
        var quotes = new List<(Document Quote, IReadOnlyCollection<DocumentLine> Lines)>(req.QuoteIds.Count);
        foreach (var qid in req.QuoteIds.Distinct())
        {
            var doc = await _repo.GetByIdAsync(qid, ct);
            if (doc == null)
                return new CreateOrdersFromQuotesResult(false, $"Teklif #{qid} bulunamadi.", 0, Array.Empty<int>());
            if (!doc.IsActive)
                return new CreateOrdersFromQuotesResult(false, $"Teklif {doc.DocumentNumber} pasif.", 0, Array.Empty<int>());
            if (doc.Status != DocumentStatus.Approved)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} onaylanmamis (durum: {doc.Status}). Sadece Approved teklifler siparise donusturulebilir.", 0, Array.Empty<int>());
            if (quoteTypeId.HasValue && doc.DocumentTypeId.HasValue && doc.DocumentTypeId != quoteTypeId)
                return new CreateOrdersFromQuotesResult(false,
                    $"Belge {doc.DocumentNumber} satis teklifi degil.", 0, Array.Empty<int>());
            if (!doc.ContactId.HasValue)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} icin cari bos. Cari secilmeden siparise donusturulemez.", 0, Array.Empty<int>());
            if (await _docSourceRepo.IsSourceConsumedAsync(qid, ct))
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} zaten siparise donusturulmus.", 0, Array.Empty<int>());

            var lines = await _repo.GetLinesAsync(qid, ct);
            if (lines.Count == 0)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} icin satir yok — bos teklif siparise donusturulemez.", 0, Array.Empty<int>());
            quotes.Add((doc, lines));
        }

        // ── 2) Cari bazli grupla ──
        var groups = quotes.GroupBy(t => t.Quote.ContactId!.Value).ToArray();
        var orderIds = new List<int>(groups.Length);

        foreach (var grp in groups)
        {
            var first = grp.First().Quote;

            // Yeni siparis numarasi — Faz P: kurala göre türet, yoksa eski fallback
            var orderTypeIdForNumber = await ResolveDefaultOrderTypeIdAsync(ct) ?? 0;
            var orderNumber = await ResolveNextDocumentNumberAsync(
                documentTypeId: orderTypeIdForNumber,
                contactId:      first.ContactId,
                userId:         null,
                issueDate:      DateTime.Now,
                ct);

            // Tutar hesabi: subtotal = tum gruptaki line'larin line_total toplami
            var allLines = grp.SelectMany(t => t.Lines).ToArray();
            var subTotal = allLines.Sum(l => l.LineTotal);
            var discountRate = first.DiscountRate;
            var taxRate = first.TaxRate;
            var discountAmount = Math.Round(subTotal * (discountRate / 100m), 4);
            var afterDiscount = subTotal - discountAmount;
            var taxAmount = Math.Round(afterDiscount * (taxRate / 100m), 4);
            var grandTotal = afterDiscount + taxAmount;

            var order = new Document
            {
                DocumentNumber = orderNumber,
                DocumentTypeId = orderTypeId,
                DocumentDate = req.OrderDate,
                ValidUntil = null,
                ContactId = first.ContactId,
                ContactName = first.ContactName,
                ContactAddress = first.ContactAddress,
                SalesRepId = first.SalesRepId,
                CurrencyId = first.CurrencyId,
                SubTotal = Math.Round(subTotal, 4),
                DiscountRate = discountRate,
                DiscountAmount = discountAmount,
                TaxRate = taxRate,
                TaxAmount = taxAmount,
                GrandTotal = Math.Round(grandTotal, 4),
                PaymentTerms = first.PaymentTerms,
                DeliveryTerms = first.DeliveryTerms,
                DeliveryAddress = first.DeliveryAddress,
                Notes = grp.Count() > 1
                    ? $"Kaynak teklifler: {string.Join(", ", grp.Select(t => t.Quote.DocumentNumber))}"
                    : $"Kaynak teklif: {first.DocumentNumber}",
                CreatedById = createdById,
                Status = DocumentStatus.Draft,
            };

            var newOrderId = await _repo.UpsertAsync(order, ct);

            // ── 3) Satirlari clone et (LineNo resequence + Notes'a kaynak ekle) ──
            var clonedLines = new List<DocumentLine>();
            // Her clone'un kaynak QuoteLine.Id'sini hatirla — detail kopyasinda kullanilacak
            var lineSourceMap = new List<int>();
            int lineNo = 1;
            foreach (var (quote, lines) in grp)
            {
                foreach (var src in lines)
                {
                    var sourceTag = $"[Kaynak: {quote.DocumentNumber}]";
                    var combinedNotes = string.IsNullOrWhiteSpace(src.Notes)
                        ? sourceTag
                        : $"{src.Notes}\n{sourceTag}";

                    clonedLines.Add(new DocumentLine
                    {
                        Id = 0, // INSERT
                        DocumentId = newOrderId,
                        LineNo = lineNo++,
                        ItemId = src.ItemId,
                        UnitId = src.UnitId,
                        Quantity = src.Quantity,
                        UnitPrice = src.UnitPrice,
                        DiscountRate = src.DiscountRate,
                        LineTotal = src.LineTotal,
                        CombinationId = src.CombinationId,
                        LocationId = src.LocationId,
                        Notes = combinedNotes,
                        NotesPinned = src.NotesPinned,
                        // RevisedFromId YOK — siparis revizyonu farkli bir senaryo
                        // SourceLineId — kalem bazli kaynak iz (sipariş satiri ↔ teklif satiri)
                        SourceLineId = src.Id,
                    });
                    lineSourceMap.Add(src.Id);
                }
            }

            await _repo.SaveLinesAsync(newOrderId, clonedLines, ct);

            // ── 4) Kombinasyon detaylarini kopyala ──
            // SaveLinesAsync sonrasi yeni Id'leri yukleyip line_no -> new id eslestirmesi yap
            var savedLines = await _repo.GetLinesAsync(newOrderId, ct);
            // savedLines line_no'ya gore sirali; lineSourceMap da line_no sirasiyla insert edilmisti.
            var savedByLineNo = savedLines.OrderBy(l => l.LineNo).ToArray();
            for (int i = 0; i < savedByLineNo.Length && i < lineSourceMap.Count; i++)
            {
                var sourceLineId = lineSourceMap[i];
                var destLineId = savedByLineNo[i].Id;
                var details = await _repo.GetLineDetailsAsync(sourceLineId, ct);
                if (details.Count == 0) continue;
                var clonedDetails = details.Select((d, idx) => new DocumentLineDetail
                {
                    QuoteLineId = destLineId,
                    FeatureName = d.FeatureName,
                    ValueCode = d.ValueCode,
                    ValueName = d.ValueName,
                    Description = d.Description,
                    LineOrder = d.LineOrder > 0 ? d.LineOrder : (idx + 1),
                }).ToList();
                await _repo.SaveLineDetailsAsync(destLineId, clonedDetails, ct);
            }

            // ── 5) document_source koprusu + status update ──
            foreach (var (quote, _) in grp)
            {
                await _docSourceRepo.AddAsync(newOrderId, quote.Id, ct);
                var prevStatus = quote.Status;
                quote.Status = DocumentStatus.Converted;
                quote.UpdatedAt = DateTime.Now;
                await _repo.UpsertAsync(quote, ct);
                _audit?.LogChanges(
                    await ResolveAuditEntityAsync(quote.DocumentTypeId, ct), quote.Id, quote.DocumentNumber,
                    [new AuditFieldChange("Status", "Durum", prevStatus.ToString(), DocumentStatus.Converted.ToString())],
                    detail: $"Siparişe dönüştürüldü: {orderNumber}");
            }

            _audit?.LogInsert(
                await ResolveAuditEntityAsync(orderTypeId, ct), newOrderId, orderNumber,
                detail: $"Tekliften dönüştürüldü: {string.Join(", ", grp.Select(t => t.Quote.DocumentNumber))}");

            orderIds.Add(newOrderId);
        }

        return new CreateOrdersFromQuotesResult(true, null, orderIds.Count, orderIds);
    }

    private static DocumentLineDto MapLineDto(DocumentLine ln, IReadOnlyList<DocumentLineDetailDto>? details = null) => new(
        ln.Id, ln.DocumentId, ln.LineNo,
        ln.ItemId, ln.MaterialCode, ln.MaterialName,
        ln.UnitId, ln.UnitCode, ln.UnitName,
        ln.Quantity, ln.UnitPrice, ln.DiscountRate, ln.LineTotal,
        ln.CombinationId, ln.CombinationCode,
        ln.LocationId, ln.LocationCode, ln.LocationName,
        ln.Notes, details,
        NotesPinned:         ln.NotesPinned,
        RevisedFromId:       ln.RevisedFromId,
        SourceLineId:        ln.SourceLineId,
        FulfilledFromStock:  ln.FulfilledFromStock,
        FulfilledByPurchase: ln.FulfilledByPurchase,
        FulfillmentStatus:   ln.FulfillmentStatus);
}
