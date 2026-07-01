using System.Linq;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

/// <summary>
/// AR-GE proje servisi. Document motorunu (numara uretimi + Document shell) yeniden kullanir,
/// uzerine AR-GE invariant'larini koyar. DocumentService.SaveQuoteAsync KULLANILMAZ — onun
/// cari/satir zorunlulugu + ikinci dogrulama katmani (SaveDocumentRequestValidator) AR-GE'ye uymaz.
/// Statu tek otorite companion ArgeProject.Status'tedir; Document.Status hep Draft kalir (dokunulmaz).
/// </summary>
public sealed class ArgeProjectService : IArgeProjectService
{
    private const string TypeCode = "arge_proje";

    private readonly IDocumentRepository _documents;
    private readonly IDocumentTypeRepository _documentTypes;
    private readonly IDocumentNumberService _numberService;
    private readonly IArgeProjectRepository _arge;
    private readonly ILogisticsConfigurationRepository _logistics;
    private readonly IRoutingService _routings;

    public ArgeProjectService(
        IDocumentRepository documents,
        IDocumentTypeRepository documentTypes,
        IDocumentNumberService numberService,
        IArgeProjectRepository arge,
        ILogisticsConfigurationRepository logistics,
        IRoutingService routings)
    {
        _documents = documents;
        _documentTypes = documentTypes;
        _numberService = numberService;
        _arge = arge;
        _logistics = logistics;
        _routings = routings;
    }

    public Task<IReadOnlyCollection<ArgeProjectListItem>> ListAsync(string? search, byte? status, CancellationToken ct)
        => _arge.ListAsync(search, status, ct);

    public Task<IReadOnlyCollection<ArgePersonnelOption>> GetPersonnelAsync(CancellationToken ct)
        => _arge.GetPersonnelAsync(ct);

    public async Task<ArgeProjectDetail?> GetAsync(int documentId, CancellationToken ct)
    {
        var companion = await _arge.GetByDocumentIdAsync(documentId, ct);
        if (companion is null) return null;
        var doc = await _documents.GetByIdAsync(documentId, ct);
        return new ArgeProjectDetail(
            DocumentId: documentId,
            DocumentNumber: doc?.DocumentNumber ?? string.Empty,
            Name: companion.Name,
            ProjectType: (byte)companion.ProjectType,
            Status: (byte)companion.Status,
            OwnerPersonnelId: companion.OwnerPersonnelId,
            TargetDate: companion.TargetDate,
            ProgressPercent: companion.ProgressPercent,
            Description: companion.Description);
    }

    public async Task<(bool Ok, string? Error, int DocumentId)> SaveAsync(SaveArgeProjectRequest request, int? userId, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Proje adı zorunludur.", 0);

        // ── Mevcut proje: yalnizca companion guncelle (statu degismez) ──
        if (request.Id > 0)
        {
            var existing = await _arge.GetByDocumentIdAsync(request.Id, ct);
            if (existing is null) return (false, "Proje bulunamadı.", 0);
            existing.Name = name;
            existing.ProjectType = (ArgeProjectType)request.ProjectType;
            existing.OwnerPersonnelId = request.OwnerPersonnelId;
            existing.TargetDate = request.TargetDate;
            existing.ProgressPercent = request.ProgressPercent;
            existing.Description = request.Description;
            existing.UpdatedById = userId;
            await _arge.UpsertCompanionAsync(existing, ct);
            return (true, null, request.Id);
        }

        // ── Yeni proje: Document shell + companion ──
        var type = await _documentTypes.GetByCodeAsync(TypeCode, ct);
        if (type is null)
            return (false, "'arge_proje' belge tipi tanımlı değil (DB init çalışmadı mı?).", 0);

        var number = await _numberService.GenerateNextAsync(
                         new DocumentNumberContext(type.Id, null, null, userId, null, DateTime.Now), ct)
                     ?? await _documents.GetNextDocumentNumberAsync(ct);

        var doc = new Document
        {
            DocumentNumber = number,
            DocumentTypeId = type.Id,
            DocumentDate = DateTime.Now,
            Status = DocumentStatus.Draft, // AR-GE akisinda Document.Status sabit; otorite companion'da
            CreatedById = userId,
        };
        var documentId = await _documents.UpsertAsync(doc, ct);

        await _arge.UpsertCompanionAsync(new ArgeProject
        {
            DocumentId = documentId,
            Name = name,
            Status = ArgeProjectStatus.Planning,
            ProjectType = (ArgeProjectType)request.ProjectType,
            OwnerPersonnelId = request.OwnerPersonnelId,
            TargetDate = request.TargetDate,
            ProgressPercent = request.ProgressPercent,
            Description = request.Description,
            CreatedById = userId,
        }, ct);

        return (true, null, documentId);
    }

    public async Task<(bool Ok, string? Error)> ChangeStatusAsync(int documentId, byte newStatus, int? userId, CancellationToken ct)
    {
        var companion = await _arge.GetByDocumentIdAsync(documentId, ct);
        if (companion is null) return (false, "Proje bulunamadı.");

        if (!Enum.IsDefined(typeof(ArgeProjectStatus), newStatus))
            return (false, "Geçersiz durum değeri.");
        var next = (ArgeProjectStatus)newStatus;

        if (!ValidateTransition(companion.Status, next))
            return (false, $"Geçersiz durum geçişi: {companion.Status} → {next}.");

        await _arge.UpdateStatusAsync(documentId, newStatus, userId, ct);
        return (true, null);
    }

    public Task DeleteAsync(int documentId, CancellationToken ct) => _documents.DeleteAsync(documentId, ct);

    public async Task<(bool Ok, string? Error, int ItemId, string? ItemCode, string? Note)> ConvertToProductionAsync(int documentId, int? userId, CancellationToken ct)
    {
        var companion = await _arge.GetByDocumentIdAsync(documentId, ct);
        if (companion is null) return (false, "Proje bulunamadı.", 0, null, null);
        if (companion.Status != ArgeProjectStatus.Approved)
            return (false, "Yalnızca onaylı (Onaylandı) projeler üretime aktarılabilir.", 0, null, null);
        if (await _arge.IsTransferredAsync(documentId, ct))
            return (false, "Bu proje zaten üretime aktarılmış.", 0, null, null);

        var prefix = companion.ProjectType == ArgeProjectType.UrGe ? "URGE" : "ARGE";
        var code = $"{prefix}-{documentId}-v1";
        int itemId;
        try
        {
            itemId = await _logistics.AddItemAsync(new Item
            {
                Code = code,
                Name = companion.Name,
                Created = DateTime.UtcNow,
                CreatedById = userId,
            }, ct);
        }
        catch (Exception ex)
        {
            return (false, $"Seri ürün kartı oluşturulamadı: {ex.Message}", 0, null, null);
        }

        await _arge.AddProductionLinkAsync(documentId, itemId, 1, userId, ct);
        await _arge.UpdateStatusAsync(documentId, (byte)ArgeProjectStatus.TransferredToProduction, userId, ct);

        // Köprü klonu: onayli (yoksa en güncel) prototip Item'inin BOM+Rota'sini seri Item'a kopyala (best-effort).
        var note = await CloneFromPrototypeAsync(documentId, itemId, code, companion.Name, ct);
        return (true, null, itemId, code, note);
    }

    /// <summary>
    /// Üretime aktarim köprüsü: klon kaynagi prototipi (onayli, yoksa Item'i olan en güncel) bulur,
    /// onun BOM + Rota'sini seri Item'a kopyalar. Best-effort — klon hatasi aktarimi bozmaz.
    /// Döndürdugu not kullaniciya gösterilir.
    /// </summary>
    private async Task<string?> CloneFromPrototypeAsync(int projectId, int seriesItemId, string seriesCode, string projectName, CancellationToken ct)
    {
        int? sourceItemId;
        try
        {
            var protos = await _arge.ListPrototypesAsync(projectId, ct);
            var source = protos.FirstOrDefault(p => p.IsApproved && p.ItemId is > 0)
                         ?? protos.FirstOrDefault(p => p.ItemId is > 0);
            sourceItemId = source?.ItemId;
        }
        catch { return null; }
        if (sourceItemId is not > 0)
            return "İlişkili prototip stok kartı yok — boş ürün kartı oluşturuldu.";

        var cloned = new List<string>();

        // ── BOM (reçete) klonu ──
        try
        {
            var src = await _logistics.GetBOMByItemAsync(sourceItemId.Value, null, ct);
            if (src is not null && src.Lines.Count > 0)
            {
                await _logistics.AddBOMAsync(new BOM
                {
                    ItemId = seriesItemId,
                    ConfigId = null,
                    Description = src.Description,
                    Lines = src.Lines.Select(l => new BOMLine
                    {
                        ItemId = l.ItemId,
                        ConfigId = l.ConfigId,
                        Quantity = l.Quantity,
                        ScrapRatio = l.ScrapRatio,
                        LineGuid = Guid.NewGuid(),
                    }).ToList(),
                }, ct);
                cloned.Add($"reçete ({src.Lines.Count} bileşen)");
            }
        }
        catch { /* best-effort: reçete klonu başarısız olsa da aktarım sürer */ }

        // ── Rota klonu ──
        try
        {
            var routings = await _routings.ListAsync(sourceItemId.Value, ct);
            var srcR = routings.FirstOrDefault(x => x.IsActive) ?? routings.FirstOrDefault();
            if (srcR is not null)
            {
                var ops = await _routings.GetOperationsAsync(srcR.Id, ct);
                var newRoutingId = await _routings.SaveAsync(new SaveRoutingRequest(
                    Id: 0,
                    Code: $"{seriesCode}-RT",
                    Name: $"{projectName} Rota",
                    ItemId: seriesItemId,
                    ConfigId: null,
                    Description: srcR.Description,
                    IsActive: true,
                    Operations: ops.Select(o => new SaveRoutingOperationLine(
                        o.Sequence, o.OperationId, o.MachineId, o.OverrideDuration, o.DurationUnit, o.Notes)).ToList()), ct);
                try { await _routings.AddItemMapAsync(newRoutingId, seriesItemId, null, ct); } catch { /* map opsiyonel */ }
                cloned.Add($"rota ({ops.Count} operasyon)");
            }
        }
        catch { /* best-effort */ }

        if (cloned.Count == 0)
            return "Prototipte reçete/rota tanımlı değil — boş ürün kartı oluşturuldu.";
        return "Prototipten klonlandı: " + string.Join(" + ", cloned) + ".";
    }

    // ── Prototip yönetimi ─────────────────────────────────────────────────────

    public Task<IReadOnlyCollection<ArgePrototypeDto>> ListPrototypesAsync(int projectId, CancellationToken ct)
        => _arge.ListPrototypesAsync(projectId, ct);

    public async Task<(bool Ok, string? Error, int Id)> SavePrototypeAsync(SavePrototypeRequest request, int? userId, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Prototip adı zorunludur.", 0);
        if (request.Id <= 0 && request.ProjectId <= 0) return (false, "Geçersiz proje.", 0);

        var id = await _arge.UpsertPrototypeAsync(new ArgePrototype
        {
            Id = request.Id,
            ProjectId = request.ProjectId,
            Name = name,
            VersionLabel = string.IsNullOrWhiteSpace(request.VersionLabel) ? null : request.VersionLabel.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedById = request.Id <= 0 ? userId : null,
            UpdatedById = request.Id > 0 ? userId : null,
        }, ct);
        return (true, null, id);
    }

    public Task DeletePrototypeAsync(int prototypeId, int? userId, CancellationToken ct)
        => _arge.SoftDeletePrototypeAsync(prototypeId, userId, ct);

    public async Task<(bool Ok, string? Error, int ItemId, string? ItemCode)> EnsurePrototypeItemAsync(int prototypeId, int? userId, CancellationToken ct)
    {
        var proto = await _arge.GetPrototypeAsync(prototypeId, ct);
        if (proto is null) return (false, "Prototip bulunamadı.", 0, null);

        if (proto.ItemId is > 0)
        {
            var existingCode = (await _arge.ListPrototypesAsync(proto.ProjectId, ct))
                .FirstOrDefault(p => p.Id == prototypeId)?.ItemCode;
            return (true, null, proto.ItemId.Value, existingCode);
        }

        var companion = await _arge.GetByDocumentIdAsync(proto.ProjectId, ct);
        var prefix = companion?.ProjectType == ArgeProjectType.UrGe ? "URGE" : "ARGE";
        var code = $"{prefix}-{proto.ProjectId}-P{prototypeId}";
        int itemId;
        try
        {
            itemId = await _logistics.AddItemAsync(new Item
            {
                Code = code,
                Name = proto.Name,
                Created = DateTime.UtcNow,
                CreatedById = userId,
            }, ct);
        }
        catch (Exception ex)
        {
            return (false, $"Prototip stok kartı oluşturulamadı: {ex.Message}", 0, null);
        }
        await _arge.LinkPrototypeItemAsync(prototypeId, itemId, userId, ct);
        return (true, null, itemId, code);
    }

    public async Task<(bool Ok, string? Error)> SetPrototypeApprovedAsync(int prototypeId, bool approved, int? userId, CancellationToken ct)
    {
        var proto = await _arge.GetPrototypeAsync(prototypeId, ct);
        if (proto is null) return (false, "Prototip bulunamadı.");
        await _arge.SetPrototypeApprovedAsync(prototypeId, proto.ProjectId, approved, userId, ct);
        return (true, null);
    }

    public Task<ArgeProjectLaborDto> GetProjectLaborAsync(int projectId, CancellationToken ct)
        => _arge.GetProjectLaborAsync(projectId, ct);

    public Task<ArgeProjectMaterialDto> GetProjectMaterialAsync(int projectId, CancellationToken ct)
        => _arge.GetProjectMaterialAsync(projectId, ct);

    /// <summary>
    /// AR-GE yasam dongusu gecis whitelist'i (WorkOrderService.ValidateTransition deseni).
    /// Iptal yalnizca onaylanmamis/aktarilmamis/reddedilmemis durumlardan alinir
    /// (Approved / TransferredToProduction / Rejected → iptal YOK). Reddedildi gelistirmeye geri doner.
    /// Ayni→ayni no-op kabul.
    /// </summary>
    private static bool ValidateTransition(ArgeProjectStatus current, ArgeProjectStatus next)
    {
        if (current == next) return true;
        return (current, next) switch
        {
            (ArgeProjectStatus.Planning,     ArgeProjectStatus.Development)              => true,
            (ArgeProjectStatus.Development,   ArgeProjectStatus.Prototyping)             => true,
            (ArgeProjectStatus.Prototyping,   ArgeProjectStatus.Testing)                 => true,
            (ArgeProjectStatus.Prototyping,   ArgeProjectStatus.Development)             => true,
            (ArgeProjectStatus.Testing,       ArgeProjectStatus.DesignReview)            => true,
            (ArgeProjectStatus.Testing,       ArgeProjectStatus.Prototyping)             => true,
            (ArgeProjectStatus.DesignReview,  ArgeProjectStatus.Approved)                => true,
            (ArgeProjectStatus.DesignReview,  ArgeProjectStatus.Rejected)                => true,
            (ArgeProjectStatus.Approved,      ArgeProjectStatus.TransferredToProduction) => true,
            (ArgeProjectStatus.Rejected,      ArgeProjectStatus.Development)             => true,

            (ArgeProjectStatus.Planning,     ArgeProjectStatus.Cancelled) => true,
            (ArgeProjectStatus.Development,   ArgeProjectStatus.Cancelled) => true,
            (ArgeProjectStatus.Prototyping,   ArgeProjectStatus.Cancelled) => true,
            (ArgeProjectStatus.Testing,       ArgeProjectStatus.Cancelled) => true,
            (ArgeProjectStatus.DesignReview,  ArgeProjectStatus.Cancelled) => true,

            _ => false,
        };
    }
}
