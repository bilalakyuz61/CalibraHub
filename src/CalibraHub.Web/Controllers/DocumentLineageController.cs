using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Belge soyağacı (İlişkili Belgeler / Akış). DocumentSource kenarlarını iki yönde
/// rekürsif yürüyerek seçili belgenin önceki (kaynak) ve sonraki (türeyen) belgelerini
/// yön + derinlik + yetki bilgisiyle döner. UI belge formunda akış paneli olarak gösterir.
///
/// Kenarların yazıldığı yerler: teklif→sipariş (DocumentService), İhtiyaç→talep/sipariş +
/// İhtiyaç→transfer/çıkış/FIFO (PurchaseController.LinkFulfillmentSourcesAsync).
/// </summary>
[Authorize]
public sealed class DocumentLineageController : Controller
{
    private readonly IDocumentSourceRepository _docSource;
    private readonly IDocumentService _docService;
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly IPermissionService _permService;

    // Döngü/patlama koruması — soyağacı zinciri normalde birkaç seviyedir.
    private const int MaxDepth = 25;

    public DocumentLineageController(
        IDocumentSourceRepository docSource,
        IDocumentService docService,
        IDocumentTypeRepository docTypeRepo,
        IPermissionService permService)
    {
        _docSource = docSource;
        _docService = docService;
        _docTypeRepo = docTypeRepo;
        _permService = permService;
    }

    [HttpGet("/Document/Lineage")]
    public async Task<IActionResult> Lineage(int id, CancellationToken ct)
    {
        if (id <= 0)
            return Json(new { rootId = id, nodes = Array.Empty<object>(), edges = Array.Empty<object>() });

        // depth: seçili belgeye göre bağıl derinlik (negatif=önceki/kaynak, 0=self, pozitif=sonraki)
        var depth = new Dictionary<int, int> { [id] = 0 };
        var edges = new HashSet<(int From, int To)>();

        // Yukarı yürüyüş — kaynak (önceki) belgeler
        var upQ = new Queue<int>();
        upQ.Enqueue(id);
        while (upQ.Count > 0)
        {
            var cur = upQ.Dequeue();
            if (-depth[cur] >= MaxDepth) continue;
            foreach (var p in await _docSource.GetSourceIdsAsync(cur, ct))
            {
                edges.Add((p, cur));
                if (!depth.ContainsKey(p)) { depth[p] = depth[cur] - 1; upQ.Enqueue(p); }
            }
        }

        // Aşağı yürüyüş — türeyen (sonraki) belgeler
        var downQ = new Queue<int>();
        downQ.Enqueue(id);
        var downSeen = new HashSet<int> { id };
        while (downQ.Count > 0)
        {
            var cur = downQ.Dequeue();
            var curDepth = depth.TryGetValue(cur, out var dd) ? dd : 0;
            if (curDepth >= MaxDepth) continue;
            foreach (var c in await _docSource.GetDerivedDocumentIdsAsync(cur, ct))
            {
                edges.Add((cur, c));
                if (downSeen.Add(c))
                {
                    if (!depth.ContainsKey(c)) depth[c] = curDepth + 1;
                    downQ.Enqueue(c);
                }
            }
        }

        // Yetki bağlamı (bir kez çözülür)
        UserAuthorizationCatalog.TryParseRole(User.FindFirstValue(ClaimTypes.Role) ?? "", out var role);
        int? dept = int.TryParse(User.FindFirstValue("department_id"), out var dv) && dv > 0 ? dv : null;
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;
        var viewActions = new[] { "VIEW", "VIEW_OWN", "EDIT_OWN", "EDIT_ALL" };

        var nodes = new List<object>();
        foreach (var (nodeId, d) in depth.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key))
        {
            var doc = await _docService.GetQuoteByIdAsync(nodeId, ct);
            if (doc == null) continue;

            string? typeCode = null, typeName = null;
            if (doc.DocumentTypeId.HasValue)
            {
                var dt = await _docTypeRepo.GetByIdAsync(doc.DocumentTypeId.Value, ct);
                typeCode = dt?.Code;
                typeName = dt?.Name;
            }

            var (fallbackName, permForm, editUrl) = ResolveNav(typeCode, nodeId);

            // Yetki: form kodu çözülebiliyorsa VIEW/EDIT yetkisi kontrol edilir; çözülemezse
            // link açık bırakılır (hedef ekran kendi PermissionScope'u ile zaten korur).
            var canView = true;
            if (!string.IsNullOrEmpty(permForm))
                canView = await _permService.CheckAnyAsync(userId, role, dept, permForm, viewActions, ct);

            // Soft-delete edilmiş belge zincirde görünür kalır (akış kopmaz) ama
            // "Silinmiş" işaretlenir ve link verilmez.
            var isDeleted = !doc.IsActive;

            nodes.Add(new
            {
                id             = nodeId,
                documentNumber = doc.DocumentNumber,
                typeName       = string.IsNullOrWhiteSpace(typeName) ? fallbackName : typeName,
                typeCode,
                date           = doc.DocumentDate,
                status         = isDeleted ? "Deleted" : doc.Status,
                depth          = d,
                isRoot         = nodeId == id,
                canView        = canView && !isDeleted,
                url            = canView && !isDeleted && editUrl.Length > 0 ? editUrl : null,
            });
        }

        return Json(new
        {
            rootId = id,
            nodes,
            edges = edges.Select(e => new { from = e.From, to = e.To }).ToArray(),
        });
    }

    /// <summary>Belge türü kodundan (görünen ad, yetki form kodu, edit URL) çözer.</summary>
    private static (string TypeName, string PermForm, string EditUrl) ResolveNav(string? code, int id)
        => (code ?? "").Trim().ToLowerInvariant() switch
        {
            "satis_teklifi"     => ("Satış Teklifi",       "SALES_QUOTE",      $"/Sales/DocumentEdit?id={id}"),
            "satis_siparisi"    => ("Satış Siparişi",      "SALES_ORDER",      $"/Sales/DocumentEdit?id={id}"),
            "alis_talebi"       => ("İhtiyaç Kaydı",       "PURCHASE_REQUEST", $"/Purchase/Edit?type=purchase_request&id={id}"),
            "alis_teklifi"      => ("Satın Alma Teklifi",  "PURCHASE_QUOTE",   $"/Purchase/Edit?type=purchase_quote&id={id}"),
            "alis_siparisi"     => ("Satın Alma Siparişi", "PURCHASE_ORDER",   $"/Purchase/Edit?type=purchase_order&id={id}"),
            "satin_alma_talebi" => ("Satın Alma Talebi",   "PURCHASE_DEMAND",  $"/Purchase/Edit?type=purchase_demand&id={id}"),
            "depo_transfer"     => ("Depo Transferi",      "TRANSFER",         $"/Warehouse/TransferEdit?id={id}"),
            "depo_giris"        => ("Depo Girişi",         "STOCK_IN",         $"/Warehouse/StockEntryEdit?id={id}"),
            "depo_cikis"        => ("Ambar Çıkış",         "STOCK_OUT",        $"/Warehouse/StockEntryEdit?id={id}"),
            "sayim"             => ("Sayım Fişi",          "INVENTORY_COUNT",  $"/Warehouse/InventoryEdit?id={id}"),
            _                   => (string.IsNullOrWhiteSpace(code) ? "Belge" : code!, "", ""),
        };
}
