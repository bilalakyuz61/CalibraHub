using System.Globalization;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Ai;

/// <summary>
/// 2026-08-01 — Doğal dil komuttan onay akışı TASLAĞI üretir.
///
/// Desen: AiChatService.cs'deki "sahte tool + ToolMode.RequireSpecific" yöntemiyle
/// aynı (native JSON mode yok — bkz. CLAUDE.md). Tek bir "emit_approval_flow" tool'u
/// tanımlanır ve zorla çağırtılır; FunctionInvokingChatClient SARILMAZ, gelen
/// FunctionCallContent.Arguments elle parse edilir. nodesJson/edgesJson/rulesJson
/// parametreleri JSON-STRING'dir (CalibroDocumentTools.CreateQuoteDraftAsync'teki
/// "lines" parametresi ile aynı precedent — nested veri array/object değil, JSON
/// string olarak modelden istenir).
///
/// Sağlayıcı: IAiClientFactory.CreateAsync(providerCode: null, ...) → Company
/// Settings'teki aktif/varsayılan AI provider'a düşer (Groq'a hardcode YOK).
///
/// İsim→ID doğrulama: modelin döndürdüğü approverId/approverName gerçek users/
/// departments listesine karşı doğrulanır. Eşleşmezse approverId=null bırakılır,
/// approverLabel'e "(seçilmedi)" eklenir ve warnings'e yazılır — FUZZY MATCH YOK
/// (CLAUDE.md ID-tabanlı eşleştirme + sessiz-kırık kuralı: belirsiz eşleşmeyi
/// sessizce "en yakın" kullanıcıya bağlamak yanlış onaylayıcıya iş düşürebilir).
/// </summary>
public sealed class ApprovalFlowAiGeneratorService : IApprovalFlowAiGeneratorService
{
    private const string ToolName = "emit_approval_flow";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAiClientFactory _clientFactory;
    private readonly ILogger<ApprovalFlowAiGeneratorService> _logger;

    public ApprovalFlowAiGeneratorService(IAiClientFactory clientFactory, ILogger<ApprovalFlowAiGeneratorService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<ApprovalFlowAiGenerationResult> GenerateAsync(
        string command,
        string? documentKind,
        IReadOnlyList<ApprovalFlowAiUserRef> users,
        IReadOnlyList<ApprovalFlowAiDepartmentRef> departments,
        int? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
            return Fail("Komut boş olamaz.");

        IChatClient? client;
        try
        {
            // providerCode: null → Company Settings'teki aktif AI sağlayıcısı (hardcode yok).
            client = await _clientFactory.CreateAsync(null, userId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Onay akışı AI üretimi — AI istemcisi oluşturulamadı.");
            return Fail("Yapay zeka istemcisi oluşturulamadı.");
        }
        if (client is null)
            return Fail("Yapay zeka yapılandırılmamış. Şirket Ayarları → Yapay Zeka sekmesinden bir sağlayıcı ekleyin.");

        try
        {
            var tool = BuildEmitTool();
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildSystemPrompt(users, departments, documentKind)),
                new(ChatRole.User, command),
            };
            var options = new ChatOptions
            {
                Tools = new List<AITool> { tool },
                ToolMode = ChatToolMode.RequireSpecific(ToolName),
                Temperature = 0.2f,
            };

            Microsoft.Extensions.AI.ChatResponse response;
            try
            {
                response = await client.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Onay akışı AI üretimi — model çağrısı başarısız. Komut: {Command}", command);
                return Fail("Yapay zeka sağlayıcısına ulaşılamadı. Lütfen tekrar deneyin.");
            }

            var call = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .FirstOrDefault(c => string.Equals(c.Name, ToolName, StringComparison.OrdinalIgnoreCase));
            if (call is null)
            {
                _logger.LogWarning("Onay akışı AI üretimi — model tool çağırmadı. Komut: {Command}", command);
                return Fail("Yapay zeka bir akış taslağı üretemedi. Komutu farklı ifade edip tekrar deneyin.");
            }

            var args = call.Arguments ?? new Dictionary<string, object?>();
            var nodesRaw = GetString(args, "nodesJson");
            var edgesRaw = GetString(args, "edgesJson");
            var rulesRaw = GetString(args, "rulesJson");

            List<AiNodeInput> aiNodes;
            List<AiEdgeInput> aiEdges;
            List<AiRuleInput> aiRules;
            try
            {
                aiNodes = string.IsNullOrWhiteSpace(nodesRaw) ? new() : (JsonSerializer.Deserialize<List<AiNodeInput>>(nodesRaw!, JsonOpts) ?? new());
                aiEdges = string.IsNullOrWhiteSpace(edgesRaw) ? new() : (JsonSerializer.Deserialize<List<AiEdgeInput>>(edgesRaw!, JsonOpts) ?? new());
                aiRules = string.IsNullOrWhiteSpace(rulesRaw) ? new() : (JsonSerializer.Deserialize<List<AiRuleInput>>(rulesRaw!, JsonOpts) ?? new());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Onay akışı AI üretimi — model çıktısı parse edilemedi. nodesJson={Nodes} edgesJson={Edges} rulesJson={Rules}",
                    nodesRaw, edgesRaw, rulesRaw);
                return Fail("Yapay zeka çıktısı okunamadı. Komutu sadeleştirip tekrar deneyin.");
            }

            if (aiNodes.Count == 0)
                return Fail("Yapay zeka hiçbir adım üretmedi. Komutu daha açık şekilde yazıp tekrar deneyin.");

            var warnings = new List<string>();
            var (nodes, edges) = BuildDesignerNodesAndEdges(aiNodes, aiEdges, users, departments, warnings);
            var rules = BuildRules(aiRules, departments, warnings);

            return new ApprovalFlowAiGenerationResult(true, nodes, edges, rules, warnings, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Onay akışı AI üretimi başarısız. Komut: {Command}", command);
            return Fail("Yapay zeka akış üretimi sırasında bir hata oluştu.");
        }
        finally
        {
            client.Dispose();
        }
    }

    private static ApprovalFlowAiGenerationResult Fail(string error)
        => new(false, Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<string>(), error);

    // ── Sahte tool tanımı ────────────────────────────────────────────────────
    // Delegate hiç invoke edilmez (FunctionInvokingChatClient kullanılmıyor) —
    // sadece JSON schema + isim üretmek için AIFunctionFactory'ye veriliyor.
    private static AIFunction BuildEmitTool()
    {
        return AIFunctionFactory.Create(
            EmitApprovalFlowStub,
            ToolName,
            "Doğal dil komuttan üretilen onay akışı taslağını (node/edge/rule listeleri, her biri JSON string) döndürür. " +
            "Bu fonksiyon SADECE veri taşımak için tanımlıdır, gerçek bir işlem yapmaz.");
    }

    [System.ComponentModel.Description("Onay akışı taslağı emit eder.")]
    private static object EmitApprovalFlowStub(
        [System.ComponentModel.Description(
            "Node dizisi, JSON STRING olarak (örn: \"[{\\\"tempId\\\":\\\"n1\\\",\\\"type\\\":\\\"start\\\"}, ...]\"). " +
            "Her node: { tempId (zorunlu, benzersiz kısa kod, örn 'n1'), type ('start'|'step'|'decision'|'end'|'parallel'|'notification'|'integration'), " +
            "name (adım/düğüm adı), approverType ('AnyUser'|'SpecificUser'|'Department'|'ManagerOfRequester', sadece type='step'), " +
            "approverId (SpecificUser→users[].id değeri STRING; Department→departments[].id değeri STRING; diğerlerinde null), " +
            "approverName (komutta geçen ham isim/departman adı — eşleşme kontrolü için, approverId doldurulsa bile yaz), " +
            "nodeData (opsiyonel, tip'e özel ek alanlar objesi: decision için {condition, conditionRules:[{field,op,value}]}, " +
            "notification için {notificationType,recipientMode,subject,body}, integration için {integrationId,integrationName,haltOnError}, parallel için {mode}). " +
            "Zorunlu: bir 'start' ve bir 'end' node'u MUTLAKA listede olmalı.")]
        string nodesJson,
        [System.ComponentModel.Description(
            "Edge (bağlantı) dizisi, JSON STRING olarak. Her edge: { from (kaynak node tempId), to (hedef node tempId), " +
            "kind ('default'|'true'|'false') }. 'true'/'false' sadece decision veya step (onay/red dalları) node'undan çıkan edge'lerde kullanılır, diğerlerinde 'default'.")]
        string edgesJson,
        [System.ComponentModel.Description(
            "Akışı tetikleyen kural dizisi, JSON STRING olarak. Her kural: { ruleType ('Always'|'MinAmount'|'MaxAmount'|'AmountRange'|'SenderTaxNo'|'Department'), " +
            "minAmount (sayı, MinAmount/AmountRange için), maxAmount (sayı, MaxAmount/AmountRange için), taxNo (metin, SenderTaxNo için), " +
            "departmentNames (dizi, Department için — komutta geçen departman adları) }. Komutta tutar/departman/VKN koşulu geçmiyorsa tek kural: {ruleType:'Always'}.")]
        string rulesJson)
        => new { };

    // ── System prompt ────────────────────────────────────────────────────────
    private static string BuildSystemPrompt(
        IReadOnlyList<ApprovalFlowAiUserRef> users,
        IReadOnlyList<ApprovalFlowAiDepartmentRef> departments,
        string? documentKind)
    {
        var usersJson = JsonSerializer.Serialize(users.Select(u => new { u.Id, u.Name }));
        var deptJson = JsonSerializer.Serialize(departments.Select(d => new { d.Id, d.Name }));

        return $@"# Kimlik
Sen CalibraHub ERP'sinde onay akışı (approval flow) TASLAĞI üreten bir yapay zeka motorusun.
Kullanıcının Türkçe doğal dil komutundan `{ToolName}` tool'unu ÇAĞIRARAK bir süreç şeması üretirsin.
Ürettiğin şey KAYDEDİLMEZ — kullanıcı önce tasarımcıda gözden geçirip kendi kaydeder (TASLAK/önizleme).

# Node türleri (nodesJson içindeki 'type' alanı)
- start: Akışın başlangıcı. Her akışta TAM OLARAK 1 adet, listenin başında olmalı.
- step: Bir kişi/departmanın onay/red verdiği adım. approverType + approverId/approverName gerekir.
- decision: Koşula göre iki yola (true/false) ayrılan karar noktası. nodeData.conditionRules doldurulur.
- parallel: Birden çok adımın eşzamanlı yürütülmesi/birleşmesi (split/join). Basit akışlarda kullanma.
- notification: Mail/WhatsApp bildirimi gönderir, onay gerektirmez.
- integration: Tanımlı bir entegrasyonu tetikler (bu MVP'de integrationId genelde bilinmez, null bırak — sadece kullanıcı açıkça 'X entegrasyonunu çalıştır' derse kullan).
- end: Akışın bitişi. Her akışta TAM OLARAK 1 adet, listenin sonunda olmalı.

# ApproverType (yalnızca type='step' node'larında)
- AnyUser: Sisteme giriş yapan HERHANGİ bir kullanıcı onaylayabilir. Komut belirli bir kişi/departman/amir belirtmiyorsa bunu kullan.
- SpecificUser: Belirli, isimlendirilmiş bir kişi. approverId = o kişinin users[] listesindeki id'si (STRING), approverName = komutta geçen isim.
- Department: Belirli bir departmanın ÜYESİ olan herhangi biri. SADECE komutta spesifik bir departman adı geçiyorsa kullan ('departman', 'bölüm' gibi genel kelimelerle DEĞİL). approverId = departments[] id'si (STRING), approverName = departman adı.
- ManagerOfRequester: Talebi oluşturan kişinin DİREKT AMİRİ. Komutta 'amirine', 'yöneticisine', 'müdürüne' gibi genel (isim/departman belirtmeyen) bir ifade varsa bunu kullan. approverId HER ZAMAN null bırakılır (runtime'da otomatik çözülür).

ÖNEMLİ: 'amir/müdür/yönetici onaylasın' gibi genel bir ifadeyi ASLA Department'a düşürme — bu ManagerOfRequester'dır. Sadece 'Satın Alma departmanı onaylasın' gibi departman adı AÇIKÇA geçiyorsa Department kullan.

# Kullanıcı ve departman listesi (approverId eşlemesi İÇİN SADECE BU LİSTELERİ KULLAN — listede olmayan birini uydurma)
users (id, name): {usersJson}
departments (id, name): {deptJson}
Komutta geçen isim bu listelerdeki isimlerle TAM olarak eşleşmiyorsa approverId'yi BOŞ bırak, approverName'e komutta geçen ham ismi yaz (sistem bunu ayrıca kullanıcıya soracak) — ASLA en yakın/benzer isme tahminle eşleştirme yapma.

# Karar (decision) koşul alanları — conditionRules: [{{field, op, value}}]
Alanlar (field/label/tip):
- amount / Toplam Tutar / numeric
- documentDate / Belge Tarihi / date
- taxNo / Tedarikçi-Müşteri VKN / text
- contactName / Tedarikçi-Müşteri Adı / text
- user.departmentId / Talep Eden Departmanı / lookup(departments)
- user.userId / Belgeyi Oluşturan / lookup(users)
- line.itemCode / Stok Kodu / text (kalem, herhangi biri)
- line.itemName / Stok Adı / text (kalem, herhangi biri)
- line.quantity / Miktar / numeric (kalem, herhangi biri)
- line.unitPrice / Birim Fiyat / numeric (kalem, herhangi biri)
- line.lineTotal / Satır Tutarı / numeric (kalem, herhangi biri)
- lineCount / Kalem Sayısı (toplam) / numeric
- lineMaxTotal / En Büyük Satır Tutarı / numeric
- lineSumQty / Toplam Miktar / numeric
Operatörler tipe göre: numeric/sql → eq,neq,gt,gte,lt,lte,between (between value=""min,max""); text → eq,neq,contains,startswith; date → eq,before,after,between; lookup → eq,in (in value=virgüllü id listesi).
value HER ZAMAN string'dir (sayı olsa bile '5000' gibi yaz).

# Kurallar (rulesJson) — akışın hangi belge/koşulda devreye gireceği
ruleType seçenekleri: Always (koşulsuz), MinAmount (tutar ≥), MaxAmount (tutar ≤), AmountRange (min-max arası), SenderTaxNo (belirli VKN), Department (belirli departman/lar).
Komutta açık bir tutar/VKN/departman eşiği geçmiyorsa tek kural üret: {{ruleType:'Always'}}.

# Yerleşim / kimlik
- Her node'a benzersiz kısa tempId ver (örn n1, n2, n3...). Edge'ler bu tempId'lere referans verir.
- Node listesini akışın MANTIKSAL SIRASINA göre ver (start önce, end son) — X/Y konumunu SEN hesaplama, backend hesaplar.
- Basit doğrusal akışlarda (dallanma yoksa) her step'ten bir sonrakine 'default' kind ile tek edge çiz.
- Bir step/decision'dan onay/red veya evet/hayır dalları varsa: step'ten çıkan edge kind='true' (onay) / kind='false' (red); decision'dan çıkan edge kind='true' (evet) / kind='false' (hayır). Red/hayır dalı genelde end'e veya bir notification node'una bağlanır.

# Bağlam
Belge türü: {(string.IsNullOrWhiteSpace(documentKind) ? "belirtilmedi (kullanıcı tasarımcıda seçecek)" : documentKind)}

# Zorunlu davranış
HER ZAMAN `{ToolName}` tool'unu çağır — metin cevap verme, açıklama yazma. nodesJson/edgesJson/rulesJson üçü de geçerli JSON dizi STRING'i olmalı (boş dizi olsa bile ""[]"" yaz, asla boş bırakma).";
    }

    // ── AI çıktı DTO'ları (JSON deserialize hedefi) ─────────────────────────
    private sealed class AiNodeInput
    {
        public string? TempId { get; set; }
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? ApproverType { get; set; }
        public string? ApproverId { get; set; }
        public string? ApproverName { get; set; }
        public JsonElement? NodeData { get; set; }
    }

    private sealed class AiEdgeInput
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Kind { get; set; }
    }

    private sealed class AiRuleInput
    {
        public string? RuleType { get; set; }
        public string? RuleValue { get; set; }
        public decimal? MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
        public string? TaxNo { get; set; }
        public List<string>? DepartmentNames { get; set; }
    }

    // ── Node/Edge dönüşümü (React Flow şekli — BuildDesignerInitialPayload ile birebir) ──
    private static (List<object> Nodes, List<object> Edges) BuildDesignerNodesAndEdges(
        List<AiNodeInput> aiNodes,
        List<AiEdgeInput> aiEdges,
        IReadOnlyList<ApprovalFlowAiUserRef> users,
        IReadOnlyList<ApprovalFlowAiDepartmentRef> departments,
        List<string> warnings)
    {
        // Tip normalize + eksik start/end güvenlik ağı (LLM bazen boilerplate node'ları atlar).
        foreach (var n in aiNodes) n.Type = NormalizeNodeType(n.Type);

        if (!aiNodes.Any(n => n.Type == "start"))
        {
            aiNodes.Insert(0, new AiNodeInput { TempId = "__auto_start__", Type = "start" });
            warnings.Add("Başlangıç düğümü modelin çıktısında yoktu, otomatik eklendi.");
        }
        if (!aiNodes.Any(n => n.Type == "end"))
        {
            aiNodes.Add(new AiNodeInput { TempId = "__auto_end__", Type = "end" });
            warnings.Add("Bitiş düğümü modelin çıktısında yoktu, otomatik eklendi.");
        }

        // tempId → nihai client id ('ai_1', 'ai_2', ...). Bilinmeyen/boş tempId'ler de üretilir.
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seq = 1;
        foreach (var n in aiNodes)
        {
            var key = string.IsNullOrWhiteSpace(n.TempId) ? Guid.NewGuid().ToString("N") : n.TempId!.Trim();
            if (!idMap.ContainsKey(key))
                idMap[key] = $"ai_{seq++}";
        }

        // Edge'leri map'le — bilinmeyen tempId referans eden edge atlanır (sessiz kaybolmasın, uyar).
        var mappedEdges = new List<(string Source, string Target, string Kind)>();
        foreach (var e in aiEdges)
        {
            if (string.IsNullOrWhiteSpace(e.From) || string.IsNullOrWhiteSpace(e.To)) continue;
            if (!idMap.TryGetValue(e.From.Trim(), out var src) || !idMap.TryGetValue(e.To.Trim(), out var tgt))
            {
                warnings.Add($"Geçersiz bağlantı atlandı (bilinmeyen düğüm referansı: '{e.From}' → '{e.To}').");
                continue;
            }
            mappedEdges.Add((src, tgt, NormalizeEdgeKind(e.Kind)));
        }

        // Start/End güvenlik ağı: start'ın çıkışı, end'in girişi yoksa zincire otomatik bağla.
        var startId = idMap[aiNodes.First(n => n.Type == "start").TempId ?? ""];
        var endId = idMap[aiNodes.First(n => n.Type == "end").TempId ?? ""];
        if (!mappedEdges.Any(e => e.Source == startId))
        {
            var firstOther = aiNodes.FirstOrDefault(n => n.Type != "start" && n.Type != "end");
            if (firstOther != null)
                mappedEdges.Add((startId, idMap[firstOther.TempId ?? ""], "default"));
        }
        if (!mappedEdges.Any(e => e.Target == endId))
        {
            var lastOther = aiNodes.LastOrDefault(n => n.Type != "start" && n.Type != "end");
            if (lastOther != null)
                mappedEdges.Add((idMap[lastOther.TempId ?? ""], endId, "default"));
        }

        // Dikey/dallı yerleşim: edge'lerden en-uzun-yol seviyesi (level) hesapla, aynı seviyedeki
        // node'ları yatayda ortala. Basit DAG relaxation — döngü olsa bile maxIter ile sınırlı.
        var orderedIds = aiNodes.Select(n => idMap[n.TempId ?? ""]).Distinct().ToList();
        var levels = ComputeLevels(orderedIds, mappedEdges.Select(e => (e.Source, e.Target)).ToList());
        var byLevel = new Dictionary<int, List<string>>();
        foreach (var id in orderedIds)
        {
            var lvl = levels.TryGetValue(id, out var l) ? l : 0;
            if (!byLevel.TryGetValue(lvl, out var list)) byLevel[lvl] = list = new List<string>();
            list.Add(id);
        }
        var positionById = new Dictionary<string, (int X, int Y)>();
        foreach (var (level, idsInLevel) in byLevel)
        {
            for (var i = 0; i < idsInLevel.Count; i++)
            {
                var x = (int)Math.Round(260 + (i - (idsInLevel.Count - 1) / 2.0) * 300);
                var y = 80 + level * 170;
                positionById[idsInLevel[i]] = (x, y);
            }
        }

        // Node type → sourceHandle çözümü (BuildDesignerInitialPayload ile aynı mantık: step onay/red, decision true/false).
        var typeById = aiNodes.ToDictionary(n => idMap[n.TempId ?? ""], n => n.Type ?? "step");

        var nodes = new List<object>();
        foreach (var n in aiNodes)
        {
            var id = idMap[n.TempId ?? ""];
            var pos = positionById.TryGetValue(id, out var p) ? p : (260, 80);
            var data = BuildNodeData(n, users, departments, warnings);
            nodes.Add(new
            {
                id,
                type = n.Type ?? "step",
                position = new { x = pos.Item1, y = pos.Item2 },
                data,
            });
        }

        var edges = new List<object>();
        var edgeSeq = 1;
        foreach (var (source, target, kind) in mappedEdges)
        {
            typeById.TryGetValue(source, out var srcType);
            string? sourceHandle = (srcType, kind) switch
            {
                ("step", "true") => "approve",
                ("step", "false") => "reject",
                ("decision", "true") => "true",
                ("decision", "false") => "false",
                _ => null,
            };
            string? label = (srcType, kind) switch
            {
                ("step", "true") => "Onay",
                ("step", "false") => "Red",
                ("decision", "true") => "Evet",
                ("decision", "false") => "Hayır",
                _ => null,
            };
            edges.Add(new
            {
                id = $"e_ai_{edgeSeq++}",
                source,
                sourceHandle,
                target,
                targetHandle = (string?)null,
                label,
                data = new { edgeKind = kind, condition = (string?)null },
            });
        }

        return (nodes, edges);
    }

    private static Dictionary<string, int> ComputeLevels(List<string> nodeIds, List<(string From, string To)> edges)
    {
        var level = nodeIds.ToDictionary(id => id, _ => 0);
        var maxIter = Math.Max(1, nodeIds.Count);
        for (var iter = 0; iter < maxIter; iter++)
        {
            var changed = false;
            foreach (var (from, to) in edges)
            {
                if (!level.ContainsKey(from) || !level.ContainsKey(to)) continue;
                var candidate = level[from] + 1;
                if (candidate > level[to])
                {
                    level[to] = candidate;
                    changed = true;
                }
            }
            if (!changed) break;
        }
        return level;
    }

    private static Dictionary<string, object?> BuildNodeData(
        AiNodeInput n,
        IReadOnlyList<ApprovalFlowAiUserRef> users,
        IReadOnlyList<ApprovalFlowAiDepartmentRef> departments,
        List<string> warnings)
    {
        Dictionary<string, object?> data = (n.Type ?? "step") switch
        {
            "step" => new()
            {
                ["stepName"] = string.IsNullOrWhiteSpace(n.Name) ? "Adım" : n.Name,
                ["approverType"] = "AnyUser",
                ["approverId"] = null,
                ["approverLabel"] = null,
            },
            "decision" => new() { ["condition"] = "", ["conditionRules"] = new List<object>() },
            "notification" => new()
            {
                ["notificationType"] = "mail",
                ["recipientMode"] = "creator",
                ["recipientId"] = null,
                ["recipientLabel"] = null,
                ["customEmail"] = null,
                ["customPhone"] = null,
                ["subject"] = string.IsNullOrWhiteSpace(n.Name) ? "" : n.Name,
                ["body"] = "",
            },
            "parallel" => new() { ["mode"] = "auto" },
            "integration" => new()
            {
                ["integrationId"] = null,
                ["integrationName"] = null,
                ["recordIdSource"] = "entity",
                ["customRecordId"] = null,
                ["haltOnError"] = true,
            },
            _ => new(), // start / end
        };

        // Tip'e özel ek alanlar (AI'nin nodeData'sı) — var olan default alanların üzerine merge edilir.
        if (n.NodeData is { ValueKind: JsonValueKind.Object } nodeDataEl)
        {
            foreach (var prop in nodeDataEl.EnumerateObject())
                data[prop.Name] = ConvertJsonElement(prop.Value);
        }

        if ((n.Type ?? "step") == "step")
            ResolveApprover(data, n, users, departments, warnings);

        return data;
    }

    private static void ResolveApprover(
        Dictionary<string, object?> data,
        AiNodeInput n,
        IReadOnlyList<ApprovalFlowAiUserRef> users,
        IReadOnlyList<ApprovalFlowAiDepartmentRef> departments,
        List<string> warnings)
    {
        var approverType = NormalizeApproverType(n.ApproverType);
        data["approverType"] = approverType;
        var stepLabel = string.IsNullOrWhiteSpace(n.Name) ? "Adım" : n.Name!;

        if (approverType is "AnyUser" or "ManagerOfRequester")
        {
            data["approverId"] = null;
            data["approverLabel"] = n.ApproverName;
            return;
        }

        if (approverType == "SpecificUser")
        {
            var match = MatchUser(n.ApproverId, n.ApproverName, users);
            if (match is null)
            {
                data["approverId"] = null;
                data["approverLabel"] = BuildUnresolvedLabel(n.ApproverName);
                warnings.Add($"'{stepLabel}' adımında '{n.ApproverName ?? n.ApproverId ?? "belirtilen"}' onaylayıcısı bulunamadı, tasarımcıda seçin.");
            }
            else
            {
                data["approverId"] = match.Id.ToString(CultureInfo.InvariantCulture);
                data["approverLabel"] = match.Name;
            }
            return;
        }

        if (approverType == "Department")
        {
            var matches = MatchDepartments(n.ApproverId, n.ApproverName, departments);
            if (matches.Count == 0)
            {
                data["approverId"] = null;
                data["approverLabel"] = BuildUnresolvedLabel(n.ApproverName);
                warnings.Add($"'{stepLabel}' adımında '{n.ApproverName ?? n.ApproverId ?? "belirtilen"}' departmanı bulunamadı, tasarımcıda seçin.");
            }
            else
            {
                data["approverId"] = string.Join(",", matches.Select(m => m.Id.ToString(CultureInfo.InvariantCulture)));
                data["approverLabel"] = string.Join(", ", matches.Select(m => m.Name));
            }
        }
    }

    private static string BuildUnresolvedLabel(string? approverName)
        => string.IsNullOrWhiteSpace(approverName) ? "(seçilmedi)" : $"{approverName} (seçilmedi)";

    private static ApprovalFlowAiUserRef? MatchUser(string? approverId, string? approverName, IReadOnlyList<ApprovalFlowAiUserRef> users)
    {
        if (!string.IsNullOrWhiteSpace(approverId) && int.TryParse(approverId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            var byId = users.FirstOrDefault(u => u.Id == id);
            if (byId is not null) return byId;
        }
        var name = !string.IsNullOrWhiteSpace(approverName) ? approverName : approverId;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return users.FirstOrDefault(u => string.Equals(u.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static List<ApprovalFlowAiDepartmentRef> MatchDepartments(
        string? approverId, string? approverName, IReadOnlyList<ApprovalFlowAiDepartmentRef> departments)
    {
        var result = new List<ApprovalFlowAiDepartmentRef>();

        // approverId virgüllü id listesi olabilir.
        if (!string.IsNullOrWhiteSpace(approverId))
        {
            foreach (var part in approverId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    var match = departments.FirstOrDefault(d => d.Id == id);
                    if (match is not null && !result.Any(r => r.Id == match.Id)) result.Add(match);
                }
            }
        }
        if (result.Count > 0) return result;

        // İsimden dene — virgülle ayrılmış birden çok departman adı da desteklenir.
        var name = approverName ?? approverId;
        if (string.IsNullOrWhiteSpace(name)) return result;
        foreach (var part in name.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = departments.FirstOrDefault(d => string.Equals(d.Name?.Trim(), part.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null && !result.Any(r => r.Id == match.Id)) result.Add(match);
        }
        return result;
    }

    // ── Rules ─────────────────────────────────────────────────────────────
    private static List<object> BuildRules(List<AiRuleInput> aiRules, IReadOnlyList<ApprovalFlowAiDepartmentRef> departments, List<string> warnings)
    {
        var result = new List<object>();
        foreach (var r in aiRules)
        {
            var type = NormalizeRuleType(r.RuleType);
            if (type is null)
            {
                warnings.Add($"Bilinmeyen kural türü atlandı: '{r.RuleType}'.");
                continue;
            }

            string value;
            switch (type)
            {
                case "MinAmount":
                    var min = r.MinAmount ?? TryParseDecimal(r.RuleValue);
                    if (min is null) { warnings.Add("Minimum tutar kuralı için geçerli bir tutar bulunamadı, atlandı."); continue; }
                    value = min.Value.ToString(CultureInfo.InvariantCulture);
                    break;
                case "MaxAmount":
                    var max = r.MaxAmount ?? TryParseDecimal(r.RuleValue);
                    if (max is null) { warnings.Add("Maksimum tutar kuralı için geçerli bir tutar bulunamadı, atlandı."); continue; }
                    value = max.Value.ToString(CultureInfo.InvariantCulture);
                    break;
                case "AmountRange":
                    if (r.MinAmount is null || r.MaxAmount is null) { warnings.Add("Tutar aralığı kuralı için min/maks tutar eksik, atlandı."); continue; }
                    value = $"{r.MinAmount.Value.ToString(CultureInfo.InvariantCulture)},{r.MaxAmount.Value.ToString(CultureInfo.InvariantCulture)}";
                    break;
                case "SenderTaxNo":
                    if (string.IsNullOrWhiteSpace(r.TaxNo) && string.IsNullOrWhiteSpace(r.RuleValue))
                    { warnings.Add("VKN kuralı için değer bulunamadı, atlandı."); continue; }
                    value = (r.TaxNo ?? r.RuleValue)!.Trim();
                    break;
                case "Department":
                    var depIds = ResolveDepartmentRuleIds(r.DepartmentNames, r.RuleValue, departments);
                    if (depIds.Count == 0) { warnings.Add("Departman kuralı için eşleşen departman bulunamadı, atlandı."); continue; }
                    value = string.Join(",", depIds);
                    break;
                default: // Always
                    value = "";
                    break;
            }

            result.Add(new { id = 0, ruleType = type, ruleValue = value, isActive = true });
        }

        // Frontend'deki başlangıç davranışıyla tutarlı: hiç kural üretilmediyse koşulsuz "Always" varsayılır.
        if (result.Count == 0)
            result.Add(new { id = 0, ruleType = "Always", ruleValue = "", isActive = true });

        return result;
    }

    private static List<int> ResolveDepartmentRuleIds(List<string>? names, string? ruleValueFallback, IReadOnlyList<ApprovalFlowAiDepartmentRef> departments)
    {
        var ids = new List<int>();
        if (names is { Count: > 0 })
        {
            foreach (var n in names)
            {
                var match = departments.FirstOrDefault(d => string.Equals(d.Name?.Trim(), n.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is not null && !ids.Contains(match.Id)) ids.Add(match.Id);
            }
        }
        if (ids.Count == 0 && !string.IsNullOrWhiteSpace(ruleValueFallback))
        {
            foreach (var part in ruleValueFallback.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                    && departments.Any(d => d.Id == id) && !ids.Contains(id))
                    ids.Add(id);
        }
        return ids;
    }

    private static decimal? TryParseDecimal(string? s)
        => !string.IsNullOrWhiteSpace(s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    // ── Normalize helpers ────────────────────────────────────────────────────
    private static string NormalizeNodeType(string? type)
    {
        var t = (type ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "start" or "step" or "decision" or "end" or "parallel" or "notification" or "integration" => t,
            _ => "step",
        };
    }

    private static string NormalizeEdgeKind(string? kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        return k is "true" or "false" ? k : "default";
    }

    private static string NormalizeApproverType(string? approverType)
    {
        var t = (approverType ?? "").Trim();
        return t.ToLowerInvariant() switch
        {
            "specificuser" => "SpecificUser",
            "department" => "Department",
            "managerofrequester" => "ManagerOfRequester",
            _ => "AnyUser",
        };
    }

    private static string? NormalizeRuleType(string? ruleType)
    {
        var t = (ruleType ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "always" => "Always",
            "minamount" => "MinAmount",
            "maxamount" => "MaxAmount",
            "amountrange" => "AmountRange",
            "sendertaxno" => "SenderTaxNo",
            "department" => "Department",
            "" => "Always",
            _ => null,
        };
    }

    // ── JSON args helpers (CalibroDocumentTools.GetString ile aynı desen) ───
    private static string? GetString(IDictionary<string, object?> a, string k)
        => a.TryGetValue(k, out var v) && v != null ? v.ToString() : null;

    // 2026-08-01: JsonElement → CLR object (array/object/scalar recursive) —
    // ApprovalFlowController.ConvertJsonElement ile aynı desen (private, controller'dan
    // erişilemediği için burada küçük bir kopyası tutulur).
    private static object? ConvertJsonElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String: return el.GetString();
            case JsonValueKind.Number: return el.TryGetInt64(out var l) ? l : el.GetDouble();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in el.EnumerateArray()) list.Add(ConvertJsonElement(item));
                return list;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var p in el.EnumerateObject()) dict[p.Name] = ConvertJsonElement(p.Value);
                return dict;
            default: return null;
        }
    }
}
