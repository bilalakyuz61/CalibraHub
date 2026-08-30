using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services;

/// <summary>
/// Reçete Ağacı — çok seviyeli reçetenin tek ekranda okunması ve kaydedilmesi (2026-08-29).
///
/// <para><b>Neden ayrı dosya:</b> ana servis 4.000+ satır. Ağaç mantığı (post-order kaydetme,
/// değişiklik tespiti, otomatik versiyon türetme) kendi içinde bir bütün ve tek başına
/// okunabilir olmalı.</para>
///
/// <para><b>Yeni tablo YOK.</b> Ağaç, mevcut <c>BOM</c>/<c>BOMLine</c> kayıtlarının hiyerarşik
/// okunuş biçimidir. Bir düğümün "alt reçetesi", o düğümün malzemesine ait BOM kaydıdır —
/// ata satırında <c>ComponentBomId</c> ile sabitlenmiş versiyon ya da o malzemenin baz reçetesi.</para>
/// </summary>
public sealed partial class LogisticsConfigurationService
{
    /// <summary>Ağaç derinlik tavanı. Gerçek reçeteler 5-6 seviyeyi geçmez; bu sonsuz döngüye karşı.</summary>
    private const int BomTreeMaxDepth = 12;

    /// <summary>
    /// Düğüm tavanı. Derinlik tavanı tek başına yetmez: geniş (her seviyede 50 bileşen)
    /// bir ağaç düğüm başına bir sorgu ile veritabanını boğar. Tavana varılırsa dal
    /// kesilir ve <see cref="BomTreeDto.Truncated"/> ile kullanıcıya BİLDİRİLİR —
    /// sessizce eksik ağaç göstermek, olmayan bileşeni "yok" sanmaya yol açar.
    /// </summary>
    private const int BomTreeMaxNodes = 600;

    // ═══════════════════════════════════════════════════════════════════
    // OKUMA
    // ═══════════════════════════════════════════════════════════════════

    public async Task<BomTreeDto?> GetBomTreeAsync(
        int itemId, int? configId, int? bomId, CancellationToken cancellationToken)
    {
        if (itemId <= 0) throw new ArgumentException("Reçete ağacı için mamul seçilmelidir.");

        var items = await _repository.GetItemsByIdsAsync(new[] { itemId }, cancellationToken);
        var root = items.FirstOrDefault(x => x.IsActive && x.Id == itemId);
        if (root is null) return null;

        // Aynı (malzeme, kombinasyon, sabitlenmiş reçete) birden çok dalda geçebilir;
        // her geçişte tekrar sorgulamamak için önbellek.
        var bomCache = new Dictionary<(int ItemId, int ConfigKey, int PinnedBomId), BOMWithNames?>();

        async Task<BOMWithNames?> ResolveBomAsync(int nodeItemId, int? nodeConfigId, int? pinnedBomId)
        {
            var key = (nodeItemId, nodeConfigId ?? 0, pinnedBomId ?? 0);
            if (bomCache.TryGetValue(key, out var cached)) return cached;
            var loaded = pinnedBomId is > 0
                ? await _repository.GetBOMByIdWithNamesAsync(pinnedBomId.Value, cancellationToken)
                : await _repository.GetBOMByItemAsync(nodeItemId, nodeConfigId, cancellationToken);
            bomCache[key] = loaded;
            return loaded;
        }

        var nodeCount = 0;
        var truncated = false;
        var maxDepthSeen = 0;
        // Her düğümün reçetesi; referans sayıları TEK toplu sorguda alınacak (aşağıda).
        var bomIdsSeen = new HashSet<int>();
        // Ağaçta geçen tüm malzemeler — tip ikonu için tek toplu okuma yapılacak.
        // Düğüm başına ayrı sorgu, geniş bir ağaçta yüzlerce gidiş-geliş demekti.
        var itemIdsSeen = new HashSet<int> { itemId };

        // ancestors: kökten bu düğüme kadarki malzemeler — döngü (A→B→A) tespiti için.
        // Bir dalda kendini tekrar eden malzeme genişletilmez; aksi halde sonsuza gider.
        async Task<BomTreeNodeDto> BuildAsync(
            int nodeItemId, string code, string name, int? nodeConfigId, string? configCode,
            decimal quantity, decimal scrapRatio, string? note,
            int? pinnedBomId, int depth, HashSet<int> ancestors)
        {
            maxDepthSeen = Math.Max(maxDepthSeen, depth);

            itemIdsSeen.Add(nodeItemId);
            if (ancestors.Contains(nodeItemId))
            {
                // Döngüde kesilen dal da tip ikonu almalı (yukarıda eklendi) — aksi
                // halde o satır ikonsuz kalır ve hizası kayar.
                return new BomTreeNodeDto(
                    nodeItemId, code, name, nodeConfigId, configCode,
                    quantity, scrapRatio, note,
                    BomId: null, BomVersionCode: null, IsPinned: pinnedBomId is > 0,
                    ReferenceCount: 0, IsCycle: true, Children: Array.Empty<BomTreeNodeDto>());
            }

            var bom = await ResolveBomAsync(nodeItemId, nodeConfigId, pinnedBomId);
            if (bom is not null) bomIdsSeen.Add(bom.Id);

            var children = new List<BomTreeNodeDto>();
            if (bom is not null && depth < BomTreeMaxDepth)
            {
                var nextAncestors = new HashSet<int>(ancestors) { nodeItemId };
                foreach (var line in bom.Lines)
                {
                    if (nodeCount >= BomTreeMaxNodes) { truncated = true; break; }
                    nodeCount++;
                    children.Add(await BuildAsync(
                        line.ItemId, line.ComponentMaterialCode, line.ComponentMaterialName,
                        line.ConfigId, line.ComponentConfigCode,
                        line.Quantity, line.ScrapRatio, line.Note,
                        line.ComponentBomId, depth + 1, nextAncestors));
                }
            }
            else if (bom is not null && depth >= BomTreeMaxDepth && bom.Lines.Count > 0)
            {
                truncated = true;
            }

            return new BomTreeNodeDto(
                nodeItemId, code, name, nodeConfigId, configCode,
                quantity, scrapRatio, note,
                BomId: bom?.Id, BomVersionCode: bom?.VersionCode,
                IsPinned: pinnedBomId is > 0,
                ReferenceCount: 0,          // toplu sorgudan sonra doldurulur
                IsCycle: false, Children: children);
        }

        // Kombinasyon kodu (varsa) — kök için gösterim alanı.
        string? rootConfigCode = null;
        if (configId.HasValue)
        {
            var combos = await _repository.GetCombinationsByMaterialCodeAsync(root.Code, cancellationToken);
            rootConfigCode = combos.FirstOrDefault(c => c.ConfigId == configId.Value)?.Code;
        }

        var tree = await BuildAsync(
            itemId, root.Code, root.Name ?? root.Code, configId, rootConfigCode,
            quantity: 1m, scrapRatio: 0m, note: null,
            pinnedBomId: bomId, depth: 0, ancestors: new HashSet<int>());

        // Referans sayıları ve malzeme tipleri: düğüm başına ayrı sorgu yerine
        // iki toplu okuma, ardından TEK gezinme ile ağaca uygulanır.
        var refCounts = await _repository.GetBomReferenceCountsAsync(bomIdsSeen, cancellationToken);
        var typeById = (await _repository.GetItemsByIdsAsync(itemIdsSeen, cancellationToken))
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.First().TypeId);
        tree = ApplyNodeFacts(tree, refCounts, typeById);

        return new BomTreeDto(tree, maxDepthSeen, truncated);
    }

    /// <summary>
    /// Toplu okunan gerçekleri (referans sayısı, malzeme tipi) ağaca tek gezinmede işler.
    /// </summary>
    public Task<IReadOnlyList<BomReferenceDto>> GetBomReferencesAsync(
        int bomId, CancellationToken cancellationToken)
        => _repository.GetBomReferencesAsync(bomId, cancellationToken);

    private static BomTreeNodeDto ApplyNodeFacts(
        BomTreeNodeDto node,
        IReadOnlyDictionary<int, int> counts,
        IReadOnlyDictionary<int, int?> typeById)
    {
        var children = node.Children.Count == 0
            ? node.Children
            : node.Children.Select(c => ApplyNodeFacts(c, counts, typeById)).ToList();
        var refCount = node.BomId is int id && counts.TryGetValue(id, out var c) ? c : 0;
        var typeId = typeById.TryGetValue(node.ItemId, out var t) ? t : null;
        return node with { ReferenceCount = refCount, TypeId = typeId, Children = children };
    }

    // ═══════════════════════════════════════════════════════════════════
    // KAYDETME
    // ═══════════════════════════════════════════════════════════════════

    public async Task<SaveBomTreeResultDto> SaveBomTreeAsync(
        SaveBomTreeRequest request, int? userId, CancellationToken cancellationToken)
    {
        if (request?.Root is null) throw new ArgumentException("Kaydedilecek reçete ağacı boş.");
        var root = request.Root;
        if (root.ItemId <= 0) throw new ArgumentException("Mamul seçmek zorunludur.");
        if (root.Children is null || root.Children.Count == 0)
            throw new ArgumentException("Reçetede en az bir bileşen olmalıdır.");

        var notes = new List<BomTreeSaveNoteDto>();

        // Gösterim için kod sözlüğü — türetilen versiyon kodu kök mamul kodundan üretilir.
        var allItemIds = new List<int>();
        CollectItemIds(root, allItemIds);
        var itemSnapshot = await _repository.GetItemsByIdsAsync(allItemIds.Distinct().ToList(), cancellationToken);
        var codeById = itemSnapshot.ToDictionary(x => x.Id, x => x.Code);
        var rootCode = codeById.TryGetValue(root.ItemId, out var rc) ? rc : root.ItemId.ToString();

        // Döngü denetimi HER ŞEYDEN ÖNCE. Alan katmanı (BOM.EnsureNoCycle) zaten
        // engelliyor ama mesajı "bu bileşenlerden biri" diyor — hangisi olduğunu
        // söylemediği için kullanıcı düzeltemiyordu. Burada zincir çıkarılıp adıyla
        // bildiriliyor. Ayrıca ERKEN çalışır: yarısı yazılmış bir ağaç bırakmaz.
        await EnsureTreeHasNoCycleAsync(root, codeById, cancellationToken);

        // Referans sayıları — hangi alt reçetenin PAYLAŞIMLI olduğunu bilmeden
        // yerinde ezmek, başka mamulleri sessizce değiştirmek demektir.
        var existingBomIds = new List<int>();
        CollectBomIds(root, existingBomIds);
        var refCounts = await _repository.GetBomReferenceCountsAsync(existingBomIds, cancellationToken);

        // ── POST-ORDER: önce çocuklar, sonra ata ──
        // Sıra zorunlu. Bir alt reçete türetilirse ata satırının ona SABİTLENMESİ gerekir
        // (ComponentBomId), yani ata kaydedilirken çocuğun NİHAİ reçete kimliği bilinmeli.
        async Task<int?> SaveNodeAsync(SaveBomTreeNode node, bool isRoot)
        {
            var hasChildren = node.Children is { Count: > 0 };
            if (!hasChildren)
            {
                // Yaprak: kendi reçetesi yok. Mevcut bir reçetesi varsa DOKUNULMAZ —
                // ağaçta genişletilmemiş bir dalı "boşaltma" olarak yorumlamak,
                // kullanıcının hiç görmediği reçeteyi silmek olurdu.
                return node.BomId;
            }

            // Çocukları önce kaydet; her birinin nihai reçete kimliğini topla.
            var childFinalBomIds = new int?[node.Children.Count];
            for (var i = 0; i < node.Children.Count; i++)
                childFinalBomIds[i] = await SaveNodeAsync(node.Children[i], isRoot: false);

            var lines = new List<SaveBOMLineRequest>(node.Children.Count);
            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                lines.Add(new SaveBOMLineRequest(
                    ItemId: child.ItemId,
                    ConfigId: child.ConfigId,
                    ComponentMaterialCode: null,
                    ComponentConfigCode: null,
                    Quantity: child.Quantity,
                    ScrapRatio: child.ScrapRatio,
                    Note: child.Note,
                    // Çocuk kendi reçetesini SABİTLİYORSA bunu satıra yaz. Yalnızca
                    // türetilmiş/seçilmiş versiyonlar sabitlenir; baz reçete NULL kalır
                    // ki baz güncellenince ata da güncel bazı izlesin.
                    ComponentBomId: childFinalBomIds[i]));
            }

            // ── Değişti mi? ──
            // Bu kontrol performans için DEĞİL doğruluk için: paylaşımlı bir düğüm
            // değişmediği hâlde "kaydedildi" sayılırsa, ağacı açıp kaydetmek her
            // paylaşımlı düğüm için gereksiz bir versiyon türetirdi.
            BOMWithNames? existing = null;
            if (node.BomId is > 0)
                existing = await _repository.GetBOMByIdWithNamesAsync(node.BomId.Value, cancellationToken);
            else if (!isRoot)
                existing = await _repository.GetBOMByItemAsync(node.ItemId, node.ConfigId, cancellationToken);

            if (existing is not null && !LinesDiffer(existing.Lines, lines))
            {
                notes.Add(new BomTreeSaveNoteDto(
                    node.ItemId, codeById.GetValueOrDefault(node.ItemId, ""), existing.Id,
                    "unchanged", existing.VersionCode, refCounts.GetValueOrDefault(existing.Id, 0)));
                // Sabitleme kuralı: ata satırı yalnız versiyonlu reçeteye sabitlenir.
                return existing.VersionCode is null ? null : existing.Id;
            }

            // ── Paylaşımlı mı? Öyleyse yerinde EZME, versiyon türet ──
            var isShared = existing is not null && refCounts.GetValueOrDefault(existing.Id, 0) > 1;
            if (!isRoot && isShared)
            {
                var versionCode = await BuildDerivedVersionCodeAsync(
                    existing!.ItemId, existing.ConfigId, rootCode, cancellationToken);
                var derivedId = await DeriveBomVersionAsync(existing.Id, versionCode, userId, cancellationToken);
                await SaveBOMAsync(new SaveBOMRequest(
                    Id: derivedId, ItemId: node.ItemId, ConfigId: node.ConfigId,
                    ParentMaterialCode: null, ConfigurationCode: null,
                    Description: existing.Description,
                    ImageBase64: null, ImageMimeType: null, ImageFitMode: existing.ImageFitMode,
                    ImageRotation: existing.ImageRotation,
                    Lines: lines, RoutingId: existing.RoutingId, RoutingCode: null,
                    VersionCode: versionCode), userId, cancellationToken);

                notes.Add(new BomTreeSaveNoteDto(
                    node.ItemId, codeById.GetValueOrDefault(node.ItemId, ""), derivedId,
                    "derived", versionCode, refCounts.GetValueOrDefault(existing.Id, 0)));
                return derivedId;   // ata satırı bu versiyona sabitlenir
            }

            // ── Yerinde kaydet / yeni oluştur ──
            var savedId = await SaveBOMAsync(new SaveBOMRequest(
                Id: existing?.Id,
                ItemId: node.ItemId, ConfigId: node.ConfigId,
                ParentMaterialCode: null, ConfigurationCode: null,
                Description: existing?.Description,
                ImageBase64: null, ImageMimeType: null,
                ImageFitMode: existing?.ImageFitMode, ImageRotation: existing?.ImageRotation ?? 0,
                Lines: lines, RoutingId: existing?.RoutingId, RoutingCode: null,
                VersionCode: existing?.VersionCode), userId, cancellationToken);

            notes.Add(new BomTreeSaveNoteDto(
                node.ItemId, codeById.GetValueOrDefault(node.ItemId, ""), savedId,
                existing is null ? "created" : "updated",
                existing?.VersionCode, refCounts.GetValueOrDefault(existing?.Id ?? 0, 0)));

            return existing?.VersionCode is null ? null : savedId;
        }

        // Kök her zaman yerinde kaydedilir: kullanıcının açtığı reçete odur.
        await SaveNodeAsync(root, isRoot: true);

        var rootNote = notes.LastOrDefault(n => n.ItemId == root.ItemId);
        return new SaveBomTreeResultDto(rootNote?.BomId ?? root.BomId ?? 0, notes);
    }

    /// <summary>
    /// Gönderilen ağaçta döngü var mı — ve VARSA hangi zincir.
    ///
    /// <para>İki kaynağı birlikte gezer: (a) kullanıcının ağaçta kurduğu bağlar,
    /// (b) ağaçta YAPRAK görünen bir bileşenin veritabanındaki KENDİ reçetesi.
    /// İkincisi olmadan en sinsi durum kaçar: kullanıcı bileşeni yeni eklemiştir,
    /// ekranda çocuksuz görünür, ama o bileşenin kayıtlı reçetesi dolaylı olarak
    /// mamulün kendisine bağlıdır.</para>
    ///
    /// <para>Derinlik ve düğüm tavanı var; bozuk bir veri kümesi denetimi sonsuza
    /// sürüklemesin (kayıtlı reçetelerde zaten döngü olabilir).</para>
    /// </summary>
    private async Task EnsureTreeHasNoCycleAsync(
        SaveBomTreeNode root,
        IReadOnlyDictionary<int, string> codeById,
        CancellationToken cancellationToken)
    {
        var childCache = new Dictionary<(int ItemId, int BomKey), IReadOnlyCollection<int>>();
        var expansions = 0;

        async Task<IReadOnlyCollection<int>> StoredChildrenAsync(int itemId, int? pinnedBomId)
        {
            var key = (itemId, pinnedBomId ?? 0);
            if (childCache.TryGetValue(key, out var hit)) return hit;
            if (expansions++ > BomTreeMaxNodes) return Array.Empty<int>();
            var rows = pinnedBomId is > 0
                ? await _repository.GetBOMComponentLinesByBomIdAsync(pinnedBomId.Value, cancellationToken)
                : await _repository.GetBOMComponentLinesAsync(itemId, cancellationToken);
            var ids = rows.Select(r => r.ItemId).Where(x => x > 0).Distinct().ToArray();
            childCache[key] = ids;
            return ids;
        }

        string Label(int id) => codeById.TryGetValue(id, out var c) && !string.IsNullOrWhiteSpace(c)
            ? c : "#" + id;

        // path: kökten bu düğüme kadarki malzeme zinciri (sıra korunur — mesajda gösterilecek).
        async Task WalkAsync(int itemId, int? pinnedBomId, IReadOnlyList<SaveBomTreeNode>? submitted,
                             List<int> path, int depth)
        {
            var at = path.IndexOf(itemId);
            if (at >= 0)
            {
                var chain = path.Skip(at).Append(itemId).ToList();
                var text = string.Join(" → ", chain.Select(Label));
                throw new BomCycleException(
                    "Döngüsel reçete: " + text + ". Bu zincirdeki bağlardan birini kaldırın — "
                    + "bir malzeme, doğrudan ya da dolaylı olarak kendi bileşeni olamaz.",
                    chain);
            }
            if (depth >= BomTreeMaxDepth) return;

            path.Add(itemId);
            try
            {
                if (submitted is { Count: > 0 })
                {
                    foreach (var child in submitted)
                        await WalkAsync(child.ItemId, child.BomId, child.Children, path, depth + 1);
                }
                else
                {
                    // Ağaçta yaprak: kayıtlı reçetesinden devam et.
                    foreach (var childId in await StoredChildrenAsync(itemId, pinnedBomId))
                        await WalkAsync(childId, null, null, path, depth + 1);
                }
            }
            finally { path.RemoveAt(path.Count - 1); }
        }

        await WalkAsync(root.ItemId, root.BomId, root.Children, new List<int>(), 0);
    }

    private static void CollectItemIds(SaveBomTreeNode node, List<int> into)
    {
        if (node.ItemId > 0) into.Add(node.ItemId);
        foreach (var c in node.Children ?? Array.Empty<SaveBomTreeNode>()) CollectItemIds(c, into);
    }

    private static void CollectBomIds(SaveBomTreeNode node, List<int> into)
    {
        if (node.BomId is > 0) into.Add(node.BomId.Value);
        foreach (var c in node.Children ?? Array.Empty<SaveBomTreeNode>()) CollectBomIds(c, into);
    }

    /// <summary>
    /// Depodaki satırlarla gönderilen satırlar farklı mı. Sıra ÖNEMSİZ (kullanıcı
    /// satırları sürükleyip bırakmış olabilir, bu bir içerik değişikliği değildir),
    /// bu yüzden (malzeme, kombinasyon) anahtarına göre karşılaştırılır.
    /// </summary>
    private static bool LinesDiffer(
        IReadOnlyCollection<BOMLineWithName> stored, IReadOnlyList<SaveBOMLineRequest> submitted)
    {
        if (stored.Count != submitted.Count) return true;

        static string Key(int itemId, int? configId) => itemId + "/" + (configId ?? 0);

        var storedByKey = stored
            .GroupBy(l => Key(l.ItemId, l.ConfigId))
            .ToDictionary(g => g.Key, g => g.First());
        if (storedByKey.Count != stored.Count) return true;  // yinelenen bileşen — karşılaştırma güvenilmez, değişmiş say

        foreach (var s in submitted)
        {
            if (!storedByKey.TryGetValue(Key(s.ItemId, s.ConfigId), out var st)) return true;
            if (st.Quantity != s.Quantity) return true;
            if (st.ScrapRatio != s.ScrapRatio) return true;
            if ((st.ComponentBomId ?? 0) != (s.ComponentBomId ?? 0)) return true;
            if (!string.Equals(st.Note ?? "", s.Note ?? "", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Otomatik versiyon kodu. Kök mamulün koduna dayanır — "bu versiyon neden var"
    /// sorusunun cevabı koda bakınca görülsün diye ("V2" bunu söylemez).
    /// Aynı kod zaten varsa -2, -3 … eklenir; <c>UX_BOM_Version</c> (ItemId, ConfigId,
    /// VersionCode) tekilliği DB kısıtıdır, çakışma kaydı patlatırdı.
    /// </summary>
    private async Task<string> BuildDerivedVersionCodeAsync(
        int itemId, int? configId, string rootCode, CancellationToken cancellationToken)
    {
        var baseCode = (rootCode ?? "").Trim();
        if (baseCode.Length == 0) baseCode = "AGAC";
        if (baseCode.Length > 26) baseCode = baseCode[..26];

        var versions = await _repository.GetBomVersionsAsync(itemId, configId, cancellationToken);
        var taken = new HashSet<string>(
            versions.Where(v => v.VersionCode != null).Select(v => v.VersionCode!),
            StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseCode)) return baseCode;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = baseCode + "-" + i;
            if (candidate.Length > 30) candidate = baseCode[..(30 - ("-" + i).Length)] + "-" + i;
            if (!taken.Contains(candidate)) return candidate;
        }
        throw new ArgumentException(
            $"'{baseCode}' için otomatik versiyon kodu üretilemedi (çok fazla versiyon var).");
    }
}
