using System.Runtime.CompilerServices;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Ai.Tools;
using Microsoft.Extensions.AI;
// 'ChatResponse' adı hem Microsoft.Extensions.AI'de hem bizim Contracts'ta var — alias.
using ChatResponseDto = CalibraHub.Application.Contracts.ChatResponse;

namespace CalibraHub.Application.Services.Ai;

/// <summary>
/// 2026-05-23 — Chat use case implementation. Provider resolve → mesajları map →
/// stream cevabı dışarı yield et.
///
/// 2026-05-24: Tool calling eklendi. CalibroTools'taki read-only fonksiyonlar
/// AIFunctionFactory ile sarilir, FunctionInvokingChatClient wrapper'i tool çağrılarını
/// otomatik yonetir (model tool_call → method çalış → sonuç model'e geri → final metin).
/// </summary>
public sealed class AiChatService : IAiChatService
{
    private readonly IAiClientFactory _factory;
    private readonly CalibroTools _tools;
    private readonly CalibroContactTools _contactTools;
    private readonly CalibroItemTools _itemTools;
    private readonly CalibroDocumentTools _documentTools;
    private readonly IDocumentTextExtractor? _docExtractor;

    public AiChatService(
        IAiClientFactory factory,
        CalibroTools tools,
        CalibroContactTools contactTools,
        CalibroItemTools itemTools,
        CalibroDocumentTools documentTools,
        IDocumentTextExtractor? docExtractor = null)
    {
        _factory = factory;
        _tools = tools;
        _contactTools = contactTools;
        _itemTools = itemTools;
        _documentTools = documentTools;
        _docExtractor = docExtractor;
    }

    // 2026-05-24: Marker'lar — frontend ozel davranis icin pattern tanir.
    public const string ConfirmMarkerStart = "[[CALIBO_CONFIRM]]";
    public const string ConfirmMarkerEnd = "[[/CALIBO_CONFIRM]]";
    // Faz B — navigate tool sonucu URL marker'i ile yansitilir, frontend tab acar.
    public const string NavigateMarkerStart = "[[CALIBO_NAVIGATE]]";
    public const string NavigateMarkerEnd = "[[/CALIBO_NAVIGATE]]";

    private const string CaliboSystemPrompt = @"# Kimlik
Adın: Calibo
Rolün: CalibraHub ERP yazılımının yapay zeka asistanı
Dilin: Türkçe (her zaman Türkçe konuşursun)

Birisi adını sorarsa cevabın sadece ""Calibo"" olur. ""Sen Calibo'sun"" gibi bir ifade ASLA isim değildir — bu sana verilmiş bir hitap kalıbıdır.

# Yetenekler (tool'lar)
- ARAMA: search_items, search_contacts, count_documents (veri soruları için önce tool kullan)
- AKTİVİTE: get_recent_activity (son N gün özeti)
- NAVİGASYON: navigate_to_screen (kullanıcı bir ekrana gitmek isterse: '/Finance/Accounts', '/Logistics/MaterialCards' vb.)
- YAZMA — CARİ: create_contact, update_contact
- YAZMA — MALZEME: create_item, update_item
- YAZMA — BELGE: create_quote_draft, add_doc_line, set_doc_status

# Davranış kuralları
1. Veri sorularında ÖNCE tool çağır, sonra sonucu Türkçe özetle (ham JSON gösterme).
2. **YAZMA TOOL'LARI ÖNCESİ BİLGİ TOPLA**: Kayıt oluştururken EKSİK BİLGİYLE TOOL ÇAĞIRMA. Önce kullanıcıya zorunlu/önemli alanları sor, eksiksiz olunca tool'u çağır.
   - **Yeni cari** için: Ad + Vergi No + Telefon + Hesap tipi (müşteri/tedarikçi) sor
   - **Yeni malzeme** için: Ad + Tip (hammadde/mamul/yarı mamul/hizmet vs.) + Birim (adet/kg/metre vs.) + KDV oranı sor
   - **Yeni teklif** için: Cari (kim için?) + Kalemler (hangi malzeme, kaç adet, birim fiyat?) sor
   - **Yeni ihtiyaç kaydı** için: Kalemler (hangi malzeme, kaç adet?) + Talep eden personel sor (opsiyonel)
   Sadece 'yeni cari ekle' denirse hemen tool çağırma; 'Tabii, hangi bilgilerle? Şu alanlar gerekli: ad, vergi no, telefon, müşteri mi tedarikçi mi?' diye sor.
3. Yazma tool'larını her zaman confirm=false ile çağır — onay kartı otomatik çıkar.
4. Güncelleme/silme öncesi önce SEARCH tool'u ile ID'yi doğrula.
5. **EKRAN AÇMA**: Kullanıcı bir ekran/sayfa/kart açmak istediğinde MUTLAKA `navigate_to_screen` tool'unu çağır. SADECE 'açtım' yazıp tool çağırmamak YANLIŞTIR.
6. Tarih/sayı: Türkçe format (1.234,56 TL, dd.MM.yyyy).
7. Yapamayacağın işlemler için: 'Bunu şu an yapamam, manuel ekrandan yapabilirsiniz.'
8. Onay kartı kullanıcıya otomatik gösteriliyor — 'Onaylıyor musun?' diye sorma; kısa teyit yeter: 'Hazırladım, onay bekliyorum.'

# Örnek akışlar
Kullanıcı: 'Acme firmasını bul' → search_contacts(query='Acme') → 'Acme Ltd (ID 42) buldum.'
Kullanıcı: 'Acme'nin telefonunu 5559998877 yap' → search_contacts + update_contact → onay kartı.
Kullanıcı: 'Cariler ekranını aç' → navigate_to_screen(url='/Finance/Accounts') → 'Cariler ekranını açtım.' (tool ÇAĞIRILDI, sonra metin)
Kullanıcı: 'Satış teklifleri ekranını aç' → navigate_to_screen(url='/Sales/Quotes') → 'Açtım.'
Kullanıcı: 'Malzeme kartlarını göster' → navigate_to_screen(url='/Logistics/MaterialCards') → 'Açtım.'
Kullanıcı: 'Adın ne?' → 'Calibo.'";

    private AIFunction[] BuildTools() => new[]
    {
        // Read-only (Faz A)
        AIFunctionFactory.Create(_tools.SearchItemsAsync),
        AIFunctionFactory.Create(_tools.SearchContactsAsync),
        AIFunctionFactory.Create(_tools.CountAndListDocumentsAsync),
        AIFunctionFactory.Create(_tools.GetRecentActivityAsync),
        // Navigate (Faz B)
        AIFunctionFactory.Create(_tools.NavigateToScreenAsync),
        // Write — Cari (Faz C)
        AIFunctionFactory.Create(_contactTools.CreateContactAsync),
        AIFunctionFactory.Create(_contactTools.UpdateContactAsync),
        // Write — Malzeme (Faz C)
        AIFunctionFactory.Create(_itemTools.CreateItemAsync),
        AIFunctionFactory.Create(_itemTools.UpdateItemAsync),
        // Write — Belge (Faz C)
        AIFunctionFactory.Create(_documentTools.SetDocumentStatusAsync),
        AIFunctionFactory.Create(_documentTools.AddDocumentLineAsync),
        AIFunctionFactory.Create(_documentTools.CreateQuoteDraftAsync),
        AIFunctionFactory.Create(_documentTools.CreatePurchaseRequestAsync),
    };

    public async IAsyncEnumerable<string> AskStreamAsync(
        ChatRequest request,
        int? userId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 2026-05-24: DIREKT navigate — LLM tool calling'i guvenilmez (DeepSeek/Gemini bazen
        // "actim" deyip cagirmiyor). Pattern + URL mapping serveride biliniyorsa LLM'i bypass
        // edip marker'i dogrudan emit ediyoruz. Bu navigate islemini %100 guvenilir kilar.
        var lastUserMsg = request.Messages?.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (lastUserMsg != null && LooksLikeNavigateRequest(lastUserMsg.Content))
        {
            var directNav = ResolveDirectNavigateUrl(lastUserMsg.Content);
            if (directNav != null)
            {
                var navPayload = new { url = directNav.Value.Url, label = directNav.Value.Label };
                var markerJson = JsonSerializer.Serialize(navPayload);
                yield return $"**{directNav.Value.Label}** ekranını açıyorum.";
                yield return "\n" + NavigateMarkerStart + markerJson + NavigateMarkerEnd;
                yield break;
            }
            // URL cözümlenmediyse LLM'e dussun (belki ozel/tanimli olmayan ekran)
        }

        var client = await _factory.CreateAsync(request.ProviderCode, userId, ct).ConfigureAwait(false);
        if (client is null)
        {
            yield return "(AI yapılandırılmamış: Şirket Ayarları → Yapay Zeka sekmesinden bir provider ekleyin.)";
            yield break;
        }

        // 2026-05-24: Binary dokuman (xlsx/pdf/docx) attachment'lari text'e cevir
        var processedMessages = PreprocessBinaryAttachments(request.Messages);
        request = request with { Messages = processedMessages };

        // 2026-05-24: Custom tool loop (FunctionInvokingChatClient yerine).
        // Bunu kendimiz yazmak zorundayiz cunku:
        //  - Write tool'lardan needsConfirmation:true gelirse loop'u DURDURMAMIZ + frontend'e
        //    ozel marker emit etmemiz gerekiyor.
        //  - Audit log icin tool input/output'a erisimimiz olmali.
        //  - Permission check tool seviyesinde gomulebilmeli.
        var meaiMessages = MapMessages(request);
        var tools = BuildTools();
        var toolMap = tools.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        // 2026-05-24: Tool calling deterministik olsun diye dusuk temperature.
        // Default 1.0 (creative) yerine 0.3 — DeepSeek/Gemini gibi "yorumcu" modeller
        // tool cagirmak yerine metin uretmeye meyilli; dusuk temp bunu azaltir.
        var options = new ChatOptions
        {
            ModelId = request.Model,
            Tools = tools.Cast<AITool>().ToList(),
            Temperature = 0.3f,
        };

        // Eger navigate pattern matchledi ama URL cozumlemedi → LLM'e dustu. O zaman
        // tool'u FORCE et ki LLM mecbur kalsin (regex-based fallback).
        if (lastUserMsg != null && LooksLikeNavigateRequest(lastUserMsg.Content))
        {
            var navTool = tools.FirstOrDefault(t => t.Name == "navigate_to_screen");
            if (navTool != null)
            {
                options.ToolMode = ChatToolMode.RequireSpecific(navTool.Name);
            }
        }

        const int MaxIterations = 6;   // sonsuz dongu guvenligi
        try
        {
            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                // Bu turun assistant mesajini olustur (text + tool_calls birikir)
                var assistantContents = new List<AIContent>();
                bool errorOccurred = false;
                string? errorMessage = null;

                await using (var enumerator = client.GetStreamingResponseAsync(meaiMessages, options, ct).GetAsyncEnumerator(ct))
                {
                    while (true)
                    {
                        bool hasMore;
                        try { hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            errorMessage = TranslateProviderError(ex.Message, request.ProviderCode);
                            errorOccurred = true;
                            break;
                        }
                        if (!hasMore) break;

                        var update = enumerator.Current;
                        if (update.Contents == null) continue;
                        foreach (var c in update.Contents)
                        {
                            if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                            {
                                yield return tc.Text;
                                assistantContents.Add(tc);
                            }
                            else if (c is FunctionCallContent fcc)
                            {
                                assistantContents.Add(fcc);
                            }
                        }
                    }
                }

                if (errorOccurred) { yield return errorMessage!; yield break; }

                // Bu turda tool cagrisi var mi?
                var calls = assistantContents.OfType<FunctionCallContent>().ToList();
                if (calls.Count == 0)
                    yield break;  // tool yok → sohbet bitti

                // Assistant mesajini conversation'a ekle (toolun gelmesi icin)
                meaiMessages.Add(new ChatMessage(ChatRole.Assistant, assistantContents));

                // Her tool cagrisini calistir
                foreach (var call in calls)
                {
                    if (!toolMap.TryGetValue(call.Name, out var aiFunc))
                    {
                        meaiMessages.Add(new ChatMessage(ChatRole.Tool, new[]
                        {
                            (AIContent)new FunctionResultContent(call.CallId, $"Bilinmeyen tool: {call.Name}")
                        }));
                        continue;
                    }

                    object? toolResult;
                    try
                    {
                        var argsDict = call.Arguments ?? new Dictionary<string, object?>();
                        var rawArgs = argsDict.ToDictionary(kv => kv.Key, kv => kv.Value);
                        var invokeArgs = new AIFunctionArguments(rawArgs);
                        toolResult = await aiFunc.InvokeAsync(invokeArgs, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        toolResult = new { error = ex.Message };
                    }

                    // Confirm gerekli mi?
                    if (TryGetNeedsConfirmation(toolResult, out var confirmPayload))
                    {
                        // Frontend marker'i — JSON payload tek satirda
                        var json = JsonSerializer.Serialize(confirmPayload);
                        yield return "\n" + ConfirmMarkerStart + json + ConfirmMarkerEnd;
                        yield break;
                    }

                    // 2026-05-24: Navigate tool sonucu — frontend tab degisikligi/yeni tab acar.
                    if (TryGetNavigate(toolResult, out var navPayload))
                    {
                        var navJson = JsonSerializer.Serialize(navPayload);
                        yield return "\n" + NavigateMarkerStart + navJson + NavigateMarkerEnd;
                        // Devam — model normal cevap metni de uretebilir (loop kirilmaz)
                    }

                    // Normal tool sonucu — geri model'e
                    meaiMessages.Add(new ChatMessage(ChatRole.Tool, new[]
                    {
                        (AIContent)new FunctionResultContent(call.CallId, toolResult ?? "")
                    }));
                }
                // Loop devam — model artik tool sonucunu okuyup final cevabini verecek
            }

            yield return "(Tool zinciri max iterasyon limitine ulasti.)";
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// 2026-05-24 — Kullanici mesajinin "ekran ac/goster/git" turunde bir navigate
    /// talebi olup olmadigini regex ile tespit eder.
    /// </summary>
    private static bool LooksLikeNavigateRequest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();
        bool hasNavVerb =
            t.Contains("aç") || t.Contains("ac mi") || t.Contains("acabilir") ||
            t.Contains("göster") || t.Contains("goster") ||
            t.Contains("git") ||
            t.Contains("aç mı") || t.Contains("açar mı");
        bool hasScreenNoun =
            t.Contains("ekran") || t.Contains("sayfa") || t.Contains("menü") || t.Contains("menu") ||
            t.Contains("liste") || t.Contains("kart");
        return hasNavVerb && hasScreenNoun;
    }

    /// <summary>
    /// 2026-05-24 — DIREKT navigate: kullanici "X ekranını aç" dediginde, LLM tool'u
    /// cagiracak diye beklemek yerine sunucu kendi cozer ve marker emit eder.
    /// DeepSeek/Gemini gibi modeller tool calling'i bazen atliyor — buradaki rigid
    /// mapping %100 calisir. URL bulunamazsa null doner → LLM normal yola devam eder.
    /// </summary>
    private static (string Url, string Label)? ResolveDirectNavigateUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant()
            .Replace("ı", "i").Replace("ş", "s").Replace("ğ", "g")
            .Replace("ü", "u").Replace("ç", "c").Replace("ö", "o");

        // Sirasiyla kontrol — daha spesifik olanlar ONCE.
        // Satin alma siparisi
        if ((t.Contains("satin alma") || t.Contains("satinalma")) && t.Contains("siparis"))
            return ("/Purchase/Orders", "Satın Alma Siparişleri");
        // Satin alma teklif
        if ((t.Contains("satin alma") || t.Contains("satinalma")) && t.Contains("teklif"))
            return ("/Purchase/Quotes", "Satın Alma Teklifleri");
        // Ihtiyac kaydi
        if (t.Contains("ihtiyac") || t.Contains("talep"))
            return ("/Purchase/Requests", "İhtiyaç Kayıtları");
        // Satis siparisi
        if (t.Contains("satis siparis") || t.Contains("siparis"))
            return ("/Sales/Orders", "Satış Siparişleri");
        // Satis teklifi
        if (t.Contains("satis teklif") || t.Contains("teklif"))
            return ("/Sales/Quotes", "Satış Teklifleri");
        // Malzeme / stok karti
        if (t.Contains("malzeme") || t.Contains("stok kart") || t.Contains("urun kart"))
            return ("/Logistics/MaterialCards", "Malzeme Kartları");
        // Cari / musteri / tedarikci
        if (t.Contains("cari") || t.Contains("musteri") || t.Contains("tedarikci") || t.Contains("hesap"))
            return ("/Finance/Accounts", "Cari Hesaplar");
        // Sirket ayarlari
        if (t.Contains("sirket ayar") || t.Contains("ayarlar"))
            return ("/Admin/CompanySettings", "Şirket Ayarları");
        // Alan rehberi
        if (t.Contains("alan rehber") || t.Contains("widget"))
            return ("/Admin/WidgetCatalog", "Alan Rehberi");
        // Personel
        if (t.Contains("personel"))
            return ("/Production/Definitions", "Personel Tanımları");
        // Makine
        if (t.Contains("makine"))
            return ("/Logistics/Machines", "Makine Tanımları");
        // Depo / lokasyon
        if (t.Contains("depo") || t.Contains("lokasyon"))
            return ("/Logistics/Locations", "Lokasyonlar");
        // Operasyon
        if (t.Contains("operasyon"))
            return ("/Production/Operations", "Operasyonlar");

        return null;
    }

    /// <summary>
    /// 2026-05-24 — Provider hatalarini kullaniciya dostu Turkce metne cevirir.
    /// HTTP 401/402/403/404/429/500 ve provider-spesifik mesajlari yakalar.
    /// Tanimsiz hata icin ham mesaji kisaltir.
    /// </summary>
    private static string TranslateProviderError(string raw, string? providerCode)
    {
        if (string.IsNullOrEmpty(raw)) return "⚠ Bilinmeyen bir AI hatası oluştu.";

        var lower = raw.ToLowerInvariant();
        var providerLabel = (providerCode ?? "AI sağlayıcı").Trim();

        // HTTP 401 — Unauthorized (key yanlis/expired)
        if (lower.Contains("http 401") || lower.Contains("unauthorized") || lower.Contains("invalid api key") || lower.Contains("authentication"))
            return $"⚠ **API anahtarı geçersiz.** {providerLabel} reddetti. Şirket Ayarları → Yapay Zeka sekmesinden anahtarı kontrol edin.";

        // HTTP 402 — Payment / balance
        if (lower.Contains("http 402") || lower.Contains("insufficient balance") || lower.Contains("insufficient_quota") || lower.Contains("insufficient credit"))
            return $"💳 **{providerLabel} hesabının bakiyesi yetersiz.** Hesabınıza kredi yükleyin veya başka bir AI sağlayıcı seçin (sol üst dropdown).";

        // HTTP 403 — Forbidden
        if (lower.Contains("http 403") || lower.Contains("forbidden") || lower.Contains("permission denied"))
            return $"🚫 **{providerLabel} erişimi reddetti.** API anahtarınızın bu modele yetkisi olmayabilir.";

        // HTTP 404 — Model not found
        if (lower.Contains("http 404") || lower.Contains("model not found") || lower.Contains("does not exist"))
            return $"❓ **Model bulunamadı.** Şirket Ayarları'ndan {providerLabel} için doğru model adını ayarlayın.";

        // HTTP 429 — Rate limit / quota
        if (lower.Contains("http 429") || lower.Contains("quota") || lower.Contains("rate_limit") || lower.Contains("rate limit") || lower.Contains("too many requests"))
        {
            // Gemini free tier ozel mesaji
            if (providerLabel.Contains("gemini", StringComparison.OrdinalIgnoreCase) || lower.Contains("free_tier"))
                return "⏱ **Gemini ücretsiz kullanım limiti dolmuş** (dakikalık veya günlük). Birkaç dakika sonra tekrar deneyin veya billing planına geçin. Ücretsiz kotalar: günde 50, dakikada 15 istek.";
            return $"⏱ **{providerLabel} istek limiti aşıldı.** Lütfen birkaç dakika sonra tekrar deneyin.";
        }

        // HTTP 400 — Bad request (genelde input format)
        if (lower.Contains("http 400") || lower.Contains("bad request") || lower.Contains("invalid_request"))
            return $"⚠ **{providerLabel} isteği reddetti** (format hatası): {Truncate(raw, 200)}";

        // HTTP 500/502/503 — Server error
        if (lower.Contains("http 500") || lower.Contains("http 502") || lower.Contains("http 503") || lower.Contains("internal server error") || lower.Contains("server error") || lower.Contains("service unavailable"))
            return $"🔧 **{providerLabel} sunucusu geçici olarak yanıt veremiyor.** Birkaç saniye sonra tekrar deneyin.";

        // Network errors
        if (lower.Contains("failed to fetch") || lower.Contains("connection refused") || lower.Contains("name or service not known"))
            return $"🌐 **{providerLabel} sunucusuna bağlanılamıyor.** İnternet bağlantınızı veya endpoint adresini kontrol edin.";

        if (lower.Contains("timeout") || lower.Contains("timed out"))
            return $"⏳ **{providerLabel} yanıt süresi aştı.** Tekrar deneyin veya daha küçük/hızlı bir model seçin.";

        // Bilinmeyen hata — ham mesajin ozet kismi
        return $"⚠ AI hatası: {Truncate(raw, 300)}";
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";

    /// <summary>Tool sonucu needsConfirmation flag'i tasiyorsa payload'u return eder.</summary>
    private static bool TryGetNeedsConfirmation(object? result, out object payload)
    {
        payload = null!;
        if (result is null) return false;
        var type = result.GetType();
        var needsProp = type.GetProperty("needsConfirmation");
        if (needsProp == null) return false;
        var val = needsProp.GetValue(result);
        if (val is bool b && b) { payload = result; return true; }
        return false;
    }

    /// <summary>Navigate tool sonucu — frontend navigate marker'i emit eder.</summary>
    private static bool TryGetNavigate(object? result, out object payload)
    {
        payload = null!;
        if (result is null) return false;
        var type = result.GetType();
        var navProp = type.GetProperty("navigate");
        var urlProp = type.GetProperty("url");
        if (navProp == null || urlProp == null) return false;
        if (navProp.GetValue(result) is bool b && b)
        {
            payload = new
            {
                url = urlProp.GetValue(result)?.ToString(),
                label = type.GetProperty("label")?.GetValue(result)?.ToString(),
            };
            return true;
        }
        return false;
    }

    public async Task<ChatResponseDto> AskAsync(
        ChatRequest request,
        int? userId,
        CancellationToken ct)
    {
        var rawClient = await _factory.CreateAsync(request.ProviderCode, userId, ct).ConfigureAwait(false);
        if (rawClient is null)
            return new ChatResponseDto(false, null, "AI yapılandırılmamış.");

        var client = new FunctionInvokingChatClient(rawClient);
        try
        {
            // 2026-05-24: Binary dokuman extraction (xlsx/pdf/docx → text)
            request = request with { Messages = PreprocessBinaryAttachments(request.Messages) };
            var meaiMessages = MapMessages(request);
            var options = new ChatOptions
            {
                ModelId = request.Model,
                Tools = BuildTools(),
            };
            var resp = await client.GetResponseAsync(meaiMessages, options, ct).ConfigureAwait(false);
            var text = resp.Messages.FirstOrDefault()?.Text ?? string.Empty;
            return new ChatResponseDto(true, text, null);
        }
        catch (Exception ex)
        {
            return new ChatResponseDto(false, null, ex.Message);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// 2026-05-24 — Binary dokuman (xlsx/pdf/docx) attachment'lardan metin cikar, TextContent'e koy.
    /// Cikti: yeni ChatMessageDto listesi (record immutable).
    /// </summary>
    private IReadOnlyList<ChatMessageDto> PreprocessBinaryAttachments(IReadOnlyList<ChatMessageDto> messages)
    {
        if (_docExtractor == null) return messages;
        var newMessages = new List<ChatMessageDto>(messages.Count);
        foreach (var m in messages)
        {
            if (m.Attachments == null || m.Attachments.Count == 0)
            {
                newMessages.Add(m);
                continue;
            }
            var newAttachments = new List<ChatAttachmentDto>(m.Attachments.Count);
            bool changed = false;
            foreach (var a in m.Attachments)
            {
                if (!string.IsNullOrEmpty(a.TextContent) || string.IsNullOrEmpty(a.Base64Data))
                {
                    newAttachments.Add(a);
                    continue;
                }
                if (!_docExtractor.Supports(a.MimeType ?? "", a.Name ?? ""))
                {
                    newAttachments.Add(a);
                    continue;
                }
                try
                {
                    var bytes = Convert.FromBase64String(a.Base64Data);
                    var extracted = _docExtractor.ExtractText(bytes, a.MimeType ?? "", a.Name ?? "");
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        // Binary'yi at, TextContent'e cevir — boylece model "image" olarak gonderme dener.
                        newAttachments.Add(a with { Base64Data = null, TextContent = extracted });
                        changed = true;
                    }
                    else
                    {
                        newAttachments.Add(a);
                    }
                }
                catch
                {
                    newAttachments.Add(a);
                }
            }
            newMessages.Add(changed ? (m with { Attachments = newAttachments }) : m);
        }
        return newMessages;
    }

    private static List<ChatMessage> MapMessages(ChatRequest request)
    {
        var result = new List<ChatMessage>(capacity: request.Messages.Count + 2);

        // 2026-05-24: Calibo system prompt — her zaman ilk mesaj olarak gelir.
        result.Add(new ChatMessage(ChatRole.System, CaliboSystemPrompt));

        // Context varsa ek system mesajı olarak ekle (kullanıcının bulunduğu sayfa)
        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            result.Add(new ChatMessage(ChatRole.System,
                "Kullanıcının aktif sayfa bağlamı: " + request.Context));
        }

        foreach (var m in request.Messages)
        {
            var role = (m.Role ?? "user").Trim().ToLowerInvariant() switch
            {
                "assistant" => ChatRole.Assistant,
                "system"    => ChatRole.System,
                _           => ChatRole.User,
            };

            // 2026-05-24: Attachment desteği.
            //   - Text dosyalari (.txt/.md/.csv/.json/.sql vs.) icin TextContent içerikte mesaja eklenir.
            //   - Resimler (image/*) icin DataContent (base64 → byte[]) multimodal mesajda gonderilir.
            // Adapter image desteklemiyorsa (text-only model) gormez — sadece text gorur.
            var attachments = m.Attachments;
            if (attachments != null && attachments.Count > 0)
            {
                var contents = new List<AIContent>(capacity: attachments.Count + 1);

                // Text attachment'lari user mesajinin BASINA prepend et — model bagimsiz calisir
                var textPrefix = new System.Text.StringBuilder();
                foreach (var a in attachments)
                {
                    if (!string.IsNullOrEmpty(a.TextContent))
                    {
                        textPrefix.AppendLine("──────────────────────────────");
                        textPrefix.AppendLine($"📎 EKLENMIS DOSYA: {a.Name} ({a.MimeType})");
                        textPrefix.AppendLine("──────────────────────────────");
                        textPrefix.AppendLine(a.TextContent);
                        textPrefix.AppendLine();
                    }
                }

                var combinedText = textPrefix.Length > 0
                    ? textPrefix.ToString() + (m.Content ?? string.Empty)
                    : (m.Content ?? string.Empty);
                if (!string.IsNullOrEmpty(combinedText))
                    contents.Add(new TextContent(combinedText));

                // Resimler — DataContent olarak ekle. Adapter image-yetenekliyse modele iletir.
                foreach (var a in attachments)
                {
                    if (!string.IsNullOrEmpty(a.Base64Data))
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(a.Base64Data);
                            contents.Add(new DataContent(bytes, a.MimeType ?? "application/octet-stream"));
                        }
                        catch (FormatException)
                        {
                            // Gecersiz base64 — yoksay, log etmek istersek ileride logger eklenebilir.
                        }
                    }
                }

                if (contents.Count > 0)
                {
                    result.Add(new ChatMessage(role, contents));
                    continue;
                }
            }

            result.Add(new ChatMessage(role, m.Content ?? string.Empty));
        }
        return result;
    }
}
