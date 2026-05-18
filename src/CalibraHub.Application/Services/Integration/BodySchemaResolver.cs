using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// IBodySchemaResolver implementasyonu — Netsis NetOpenX Describe + POST probe fallback.
///
/// Auth: IIntegrationAuthHandler ortak handler'ı kullanir — OAuth2Password,
/// Bearer, Basic, ApiKey hepsi destekli. 401 alirsa token cache invalidate
/// edip 1 retry yapar.
///
/// Akis:
///   1. URL'den Resource cikar (eg. /api/v2/ItemSlips -> ItemSlips)
///   2. POST {baseUrl}/api/v2/{Resource}/Describe at — basariliysa parse + sterilize
///   3. Basarisizsa POST-probe: orijinal endpoint'e bos body {} gonder, hata mesajini parse et
///   4. Hicbiri olmazsa Success=false dondur — frontend kullaniciyi Sablon Galerisi'ne yonlendirir
/// </summary>
public sealed class BodySchemaResolver : IBodySchemaResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIntegrationAuthHandler _auth;

    public BodySchemaResolver(IHttpClientFactory httpClientFactory, IIntegrationAuthHandler auth)
    {
        _httpClientFactory = httpClientFactory;
        _auth = auth;
    }

    public async Task<BodySchemaResolveResult> ResolveAsync(
        IntegrationEndpoint endpoint,
        IntegrationApiProfile profile,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // 1) Netsis Services Swagger Discovery — EN GÜVENİLİR katman
        // GET /api/v2/services/{Resource}?expandLevel=full
        // Gerçek paths + definitions ile tam Swagger doc döner; sample generate edilir.
        // Netsis NoxRest dokümanında resmi olarak destekleniyor.
        var services = await TryServicesSwaggerAsync(endpoint, profile, ct);
        if (services.Success)
        {
            sw.Stop();
            return services with { DurationMs = (int)sw.ElapsedMilliseconds };
        }

        // 2) Definitions Type — Resource'un type definition'ı doğrudan
        // GET /api/v2/definitions/{TypeName} — daha hafif, sadece body model'i
        var definitions = await TryDefinitionsTypeAsync(endpoint, profile, ct);
        if (definitions.Success)
        {
            sw.Stop();
            return definitions with { DurationMs = (int)sw.ElapsedMilliseconds };
        }

        // 3) Describe katmani — alan listesi (eski yöntem, bazı sürümlerde
        // IsSuccessful=false zarf döner — services tercih edilir)
        var describe = await TryDescribeAsync(endpoint, profile, ct);
        if (describe.Success)
        {
            sw.Stop();
            return describe with { DurationMs = (int)sw.ElapsedMilliseconds };
        }

        // 4) Sample GET — Resource'un mevcut bir kaydini cek, sterilize et
        // Netsis standardı: ?limit=1 (Top= değil)
        var sample = await TrySampleGetAsync(endpoint, profile, ct);
        if (sample.Success)
        {
            sw.Stop();
            return sample with { DurationMs = (int)sw.ElapsedMilliseconds };
        }

        // 5) POST Probe (bos body gonder, hata mesajini parse et)
        var probe = await TryProbeAsync(endpoint, profile, ct);
        sw.Stop();

        if (probe.Success)
            return probe with { DurationMs = (int)sw.ElapsedMilliseconds };

        return new BodySchemaResolveResult
        {
            Success = false,
            Source = "Failed",
            ErrorMessage =
                $"Services: {services.ErrorMessage ?? "yok"} | " +
                $"Definitions: {definitions.ErrorMessage ?? "yok"} | " +
                $"Describe: {describe.ErrorMessage ?? "yok"} | " +
                $"Sample GET: {sample.ErrorMessage ?? "yok"} | " +
                $"Probe: {probe.ErrorMessage ?? "yok"}",
            DurationMs = (int)sw.ElapsedMilliseconds,
        };
    }

    // ── 1) Netsis Services Swagger Discovery ──────────────────────────────

    /// <summary>
    /// EN GÜVENİLİR katman: Netsis NoxRest'in built-in Swagger discovery API'si.
    ///   GET /api/v2/services/{Resource}?expandLevel=full
    /// Resmi NoxRest dokümanında destekleniyor (NOXRest_Swagger_Definition.txt).
    /// Dönen Swagger JSON'dan request body schema'sını çıkarıp sample generate eder.
    /// $ref'leri çözer (definitions zinciri), nested object/array doğru handle eder.
    /// </summary>
    private async Task<BodySchemaResolveResult> TryServicesSwaggerAsync(
        IntegrationEndpoint endpoint, IntegrationApiProfile profile, CancellationToken ct)
    {
        var resource = ExtractResource(endpoint.UrlTemplate);
        if (string.IsNullOrWhiteSpace(resource))
            return new BodySchemaResolveResult
            {
                Success = false, Source = "Services",
                ErrorMessage = "URL'den Resource cikarilamadi.",
            };

        var url = (profile.BaseUrl ?? "").TrimEnd('/')
                + $"/api/v2/services/{resource}?expandLevel=full";

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var (resp, raw) = await SendWithAuthRetryAsync(client, url, "{}", profile, ct, "GET");

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                {
                    return new BodySchemaResolveResult
                    {
                        Success = false, Source = "Services",
                        ErrorMessage = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}",
                        HttpStatusCode = (int)resp.StatusCode,
                        RawResponse = raw,
                    };
                }

                var (bodyJson, fields) = ParseServicesSwagger(raw, resource, endpoint.HttpMethod ?? "POST");
                if (string.IsNullOrWhiteSpace(bodyJson))
                {
                    return new BodySchemaResolveResult
                    {
                        Success = false, Source = "Services",
                        ErrorMessage = "Swagger doc parse edilemedi (paths/definitions bulunamadi).",
                        HttpStatusCode = (int)resp.StatusCode,
                        RawResponse = raw,
                    };
                }

                return new BodySchemaResolveResult
                {
                    Success = true, Source = "Services",
                    BodyJson = bodyJson,
                    Fields = fields,
                    HttpStatusCode = (int)resp.StatusCode,
                };
            }
        }
        catch (Exception ex)
        {
            return new BodySchemaResolveResult
            {
                Success = false, Source = "Services", ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Swagger JSON parse — paths içinde method (post/put/...) için body schema'sını
    /// bul, definitions ile $ref'leri çöz, sample JSON üret + field metadata listele.
    /// Returns: (bodyJson, fields)
    /// </summary>
    private static (string? bodyJson, IReadOnlyList<BodySchemaFieldMeta>? fields)
        ParseServicesSwagger(string raw, string resource, string httpMethod)
    {
        try
        {
            var doc = JsonNode.Parse(raw);
            if (doc is not JsonObject root) return (null, null);

            var definitions = root["definitions"] as JsonObject;

            var paths = root["paths"] as JsonObject;
            if (paths is null) return (null, null);

            JsonNode? bodySchema = null;
            string method = (httpMethod ?? "POST").ToLowerInvariant();

            foreach (var pathEntry in paths)
            {
                var pathName = pathEntry.Key;
                if (!pathName.Equals("/" + resource, StringComparison.OrdinalIgnoreCase) &&
                    !pathName.StartsWith("/" + resource + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (pathEntry.Value is not JsonObject pathObj) continue;
                if (pathObj[method] is not JsonObject op) continue;
                if (op["parameters"] is not JsonArray parms) continue;

                foreach (var p in parms)
                {
                    if (p is not JsonObject po) continue;
                    if ((po["in"]?.GetValue<string>() ?? "") == "body")
                    {
                        bodySchema = po["schema"];
                        break;
                    }
                }
                if (bodySchema is not null) break;
            }

            if (bodySchema is null) return (null, null);

            var sample  = BuildSampleFromSchema(bodySchema, definitions, new HashSet<string>());
            var fields  = new List<BodySchemaFieldMeta>();
            ExtractFields(bodySchema, definitions, "", new HashSet<string>(), fields);

            var bodyJson = sample?.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return (bodyJson, fields);
        }
        catch { return (null, null); }
    }

    /// <summary>
    /// Swagger schema'sını recursive gez, her leaf alan için BodySchemaFieldMeta üret.
    /// Path notation: "FatUst.CariKod" / "Kalems[].StokKodu"
    /// Required: parent schema'nin "required":["X","Y"] dizisinde varsa true.
    /// Infinite loop koruması: aynı $ref ikinci kez ziyaret edilmez.
    /// </summary>
    private static void ExtractFields(
        JsonNode? schema, JsonObject? definitions, string parentPath,
        HashSet<string> visited, List<BodySchemaFieldMeta> sink)
    {
        if (schema is null) return;
        if (schema is not JsonObject obj) return;

        // $ref çöz
        if (obj["$ref"]?.GetValue<string>() is string refPath)
        {
            var typeName = refPath.Split('/').LastOrDefault() ?? "";
            if (string.IsNullOrEmpty(typeName)) return;
            if (!visited.Add(typeName)) return; // döngü
            try
            {
                var resolved = definitions?[typeName] as JsonObject;
                if (resolved is not null)
                    ExtractFields(resolved, definitions, parentPath, visited, sink);
            }
            finally { visited.Remove(typeName); }
            return;
        }

        var type = obj["type"]?.GetValue<string>() ?? "object";

        // required set — bu seviyede zorunlu alanlar
        var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (obj["required"] is JsonArray reqArr)
        {
            foreach (var r in reqArr)
                if (r is JsonValue rv && rv.GetValue<JsonElement>().ValueKind == JsonValueKind.String)
                    requiredSet.Add(rv.GetValue<string>());
        }

        if (type == "object" && obj["properties"] is JsonObject props)
        {
            foreach (var kv in props)
            {
                var propName = kv.Key;
                var propPath = string.IsNullOrEmpty(parentPath) ? propName : $"{parentPath}.{propName}";
                var propSchema = kv.Value as JsonObject;
                if (propSchema is null) continue;

                var propType = propSchema["type"]?.GetValue<string>() ?? "object";
                bool isRequired = requiredSet.Contains(propName);

                if (propType == "object" || propSchema["$ref"] is not null)
                {
                    // Nested object — descend
                    ExtractFields(propSchema, definitions, propPath, visited, sink);
                }
                else if (propType == "array")
                {
                    var itemPath = propPath + "[]";
                    ExtractFields(propSchema["items"], definitions, itemPath, visited, sink);
                }
                else
                {
                    // Leaf — meta üret
                    int? maxLen = propSchema["maxLength"]?.GetValue<JsonElement>().ValueKind switch
                    {
                        JsonValueKind.Number => propSchema["maxLength"]!.GetValue<int>(),
                        _ => (int?)null,
                    };
                    string? enumStr = null;
                    if (propSchema["enum"] is JsonArray enumArr && enumArr.Count > 0)
                        enumStr = enumArr.ToJsonString();

                    sink.Add(new BodySchemaFieldMeta(
                        Path:      propPath,
                        Type:      propType,
                        Required:  isRequired,
                        MaxLength: maxLen,
                        Enum:      enumStr));
                }
            }
        }
    }

    /// <summary>
    /// Swagger schema'yı sample JSON'a çevir — recursive, $ref destekli.
    /// Infinite loop koruması: aynı $ref ikinci kez ziyaret edilmez (visited set).
    /// </summary>
    private static JsonNode? BuildSampleFromSchema(
        JsonNode? schema, JsonObject? definitions, HashSet<string> visited)
    {
        if (schema is null) return null;
        if (schema is not JsonObject obj) return null;

        // $ref çöz
        if (obj["$ref"]?.GetValue<string>() is string refPath)
        {
            // "#/definitions/ItemSlips" → "ItemSlips"
            var typeName = refPath.Split('/').LastOrDefault() ?? "";
            if (string.IsNullOrEmpty(typeName)) return null;
            if (!visited.Add(typeName)) return new JsonObject(); // döngü kırıcı
            try
            {
                var resolved = definitions?[typeName] as JsonObject;
                if (resolved is null) return null;
                return BuildSampleFromSchema(resolved, definitions, visited);
            }
            finally { visited.Remove(typeName); }
        }

        var type = obj["type"]?.GetValue<string>() ?? "object";

        switch (type)
        {
            case "object":
            {
                var result = new JsonObject();
                if (obj["properties"] is JsonObject props)
                {
                    foreach (var kv in props)
                    {
                        var sample = BuildSampleFromSchema(kv.Value, definitions, visited);
                        // Null değerleri de yaz (kullanıcıya hangi alan olduğunu göstermek için)
                        if (sample is JsonValue v)
                        {
                            // string/number/bool — JsonValue klonu
                            result[kv.Key] = JsonNode.Parse(v.ToJsonString());
                        }
                        else if (sample is JsonObject || sample is JsonArray)
                        {
                            result[kv.Key] = JsonNode.Parse(sample.ToJsonString());
                        }
                        else
                        {
                            result[kv.Key] = JsonValue.Create("");
                        }
                    }
                }
                return result;
            }

            case "array":
            {
                var arr = new JsonArray();
                var item = BuildSampleFromSchema(obj["items"], definitions, visited);
                if (item is not null)
                {
                    // JSON node'lar tek bir parent'a sahip olur, klonlamak gerekiyor
                    arr.Add(JsonNode.Parse(item.ToJsonString()));
                }
                return arr;
            }

            case "integer":
            case "number":
                return JsonValue.Create(0);

            case "boolean":
                return JsonValue.Create(false);

            case "string":
                // enum varsa ilk değeri al
                if (obj["enum"] is JsonArray enumArr && enumArr.Count > 0)
                    return JsonNode.Parse(enumArr[0]!.ToJsonString());
                return JsonValue.Create("");

            default:
                return JsonValue.Create("");
        }
    }

    // ── 2) Definitions Type ────────────────────────────────────────────────

    /// <summary>
    /// İkinci yedek: Resource'un type definition'ı doğrudan.
    ///   GET /api/v2/definitions/{TypeName}
    /// Daha hafif, sadece body model'i için yeterli.
    /// </summary>
    private async Task<BodySchemaResolveResult> TryDefinitionsTypeAsync(
        IntegrationEndpoint endpoint, IntegrationApiProfile profile, CancellationToken ct)
    {
        var resource = ExtractResource(endpoint.UrlTemplate);
        if (string.IsNullOrWhiteSpace(resource))
            return new BodySchemaResolveResult
            {
                Success = false, Source = "Definitions",
                ErrorMessage = "URL'den Resource cikarilamadi.",
            };

        var url = (profile.BaseUrl ?? "").TrimEnd('/') + $"/api/v2/definitions/{resource}";

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var (resp, raw) = await SendWithAuthRetryAsync(client, url, "{}", profile, ct, "GET");

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                {
                    return new BodySchemaResolveResult
                    {
                        Success = false, Source = "Definitions",
                        ErrorMessage = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}",
                        HttpStatusCode = (int)resp.StatusCode,
                        RawResponse = raw,
                    };
                }

                // Cevap: doğrudan type schema (no envelope)
                var node = JsonNode.Parse(raw);
                var sample = BuildSampleFromSchema(node, null, new HashSet<string>());
                if (sample is null) return new BodySchemaResolveResult
                {
                    Success = false, Source = "Definitions",
                    ErrorMessage = "Definition cevabi parse edilemedi.",
                    HttpStatusCode = (int)resp.StatusCode,
                    RawResponse = raw,
                };

                return new BodySchemaResolveResult
                {
                    Success = true, Source = "Definitions",
                    BodyJson = sample.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    HttpStatusCode = (int)resp.StatusCode,
                };
            }
        }
        catch (Exception ex)
        {
            return new BodySchemaResolveResult
            {
                Success = false, Source = "Definitions", ErrorMessage = ex.Message,
            };
        }
    }

    // ── Sample GET ────────────────────────────────────────────────────────

    /// <summary>
    /// Resource'un mevcut bir kaydini GET ile cekip sterilize eder. Describe
    /// sadece alan listesi verir; bu metot **dolu bir ornek body** uretir
    /// (gercek aktarim sablonu).
    ///
    /// Akis:
    ///   1. GET {baseUrl}/api/v2/{Resource}?Top=1 (varsa OData/NoxRest filter destegi)
    ///   2. Cevap zarfindan ilk kaydi al (Data/Items/Records/dogrudan array)
    ///   3. Sterilize et — degerleri "" / 0 / false / null yap, yapi korunur
    ///   4. Sonuc body schema olarak doner
    /// </summary>
    private async Task<BodySchemaResolveResult> TrySampleGetAsync(
        IntegrationEndpoint endpoint, IntegrationApiProfile profile, CancellationToken ct)
    {
        var resource = ExtractResource(endpoint.UrlTemplate);
        if (string.IsNullOrWhiteSpace(resource))
            return new BodySchemaResolveResult
            {
                Success = false, Source = "SampleGet",
                ErrorMessage = "URL'den Resource cikarilamadi.",
            };

        // NetOpenX standardı: ?limit=N (NOXRest_Yazilimi.txt'de açıkça yazıyor)
        // Top=N OData/Microsoft Graph standardıdır, NetOpenX desteklemez.
        var sampleUrl = (profile.BaseUrl ?? "").TrimEnd('/') + $"/api/v2/{resource}?limit=1";

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var (resp, raw) = await SendWithAuthRetryAsync(client, sampleUrl, "{}", profile, ct, "GET");

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                {
                    return new BodySchemaResolveResult
                    {
                        Success = false,
                        Source = "SampleGet",
                        ErrorMessage = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}",
                        HttpStatusCode = (int)resp.StatusCode,
                        RawResponse = raw,
                    };
                }

                var bodyJson = ParseSamplePayload(raw);
                if (string.IsNullOrWhiteSpace(bodyJson))
                {
                    return new BodySchemaResolveResult
                    {
                        Success = false,
                        Source = "SampleGet",
                        ErrorMessage = $"Cevapta veri bulunamadi (Resource={resource} bos olabilir veya format taninamadi).",
                        HttpStatusCode = (int)resp.StatusCode,
                        RawResponse = raw,
                    };
                }

                return new BodySchemaResolveResult
                {
                    Success = true,
                    Source = "SampleGet",
                    BodyJson = bodyJson,
                    HttpStatusCode = (int)resp.StatusCode,
                };
            }
        }
        catch (Exception ex)
        {
            return new BodySchemaResolveResult
            {
                Success = false, Source = "SampleGet", ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// GET cevabini parse — ilk kaydi cekip sterilize et.
    /// NoxRest format: {"IsSuccessful":true,"Data":[...]} veya {"Data":{Items:[...]}}
    /// veya dogrudan [...] (array). Her durumu destekle, ilk elementi sterilize et.
    /// </summary>
    private static string? ParseSamplePayload(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // IsSuccessful=false zarf
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("IsSuccessful", out var okEl) &&
                okEl.ValueKind == JsonValueKind.False)
            {
                return null;
            }

            JsonElement? dataNode = null;

            // Varyant: {"Data": [...]} veya {"Data": {...}} veya {"Data": "string"}
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Data", out var d))
            {
                if (d.ValueKind == JsonValueKind.String)
                {
                    var inner = d.GetString();
                    if (string.IsNullOrWhiteSpace(inner)) return null;
                    var innerNode = JsonNode.Parse(inner);
                    return SterilizeFirstRecord(innerNode);
                }
                dataNode = d;
            }
            // Varyant: dogrudan root array
            else if (root.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                dataNode = root;
            }

            if (dataNode is null) return null;
            var node = JsonNode.Parse(dataNode.Value.GetRawText());
            return SterilizeFirstRecord(node);
        }
        catch { return null; }
    }

    /// <summary>
    /// Eger node array ise ilk elementini, object ise kendisini sterilize eder.
    /// Items/Records/value gibi yaygin "asil veri" sarmal alanlarini da kontrol eder.
    /// </summary>
    private static string? SterilizeFirstRecord(JsonNode? node)
    {
        if (node is null) return null;

        // Sarmal alan kontrol — {"Items":[...]} / {"Records":[...]} / {"value":[...]}
        if (node is JsonObject obj)
        {
            foreach (var key in new[] { "Items", "Records", "value", "results" })
            {
                if (obj.TryGetPropertyValue(key, out var inner) && inner is JsonArray)
                {
                    var arr = (JsonArray)inner!;
                    if (arr.Count > 0)
                    {
                        var first = arr[0];
                        if (first is not null)
                            return Sterilize(JsonNode.Parse(first.ToJsonString()));
                    }
                }
            }
            // Sarmal yok — object'in kendisi tek kayit
            return Sterilize(node);
        }

        if (node is JsonArray arr2)
        {
            if (arr2.Count == 0) return null;
            var first = arr2[0];
            if (first is null) return null;
            return Sterilize(JsonNode.Parse(first.ToJsonString()));
        }

        return null;
    }

    // ── 1) Describe ────────────────────────────────────────────────────────

    private async Task<BodySchemaResolveResult> TryDescribeAsync(
        IntegrationEndpoint endpoint, IntegrationApiProfile profile, CancellationToken ct)
    {
        var resource = ExtractResource(endpoint.UrlTemplate);
        if (string.IsNullOrWhiteSpace(resource))
            return new BodySchemaResolveResult
            {
                Success = false, Source = "Describe",
                ErrorMessage = "URL'den Resource cikarilamadi.",
            };

        var describeUrl = (profile.BaseUrl ?? "").TrimEnd('/') + $"/api/v2/{resource}/Describe";

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // 1. Deneme: POST (Netsis NoxRest standart davranisi)
            var (resp, raw) = await SendWithAuthRetryAsync(client, describeUrl, "{}", profile, ct, "POST");

            // POST 405 dondurduyse GET dene — bazi NoxRest versionlari Describe'i GET ile destekler
            if (resp.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                resp.Dispose();
                (resp, raw) = await SendWithAuthRetryAsync(client, describeUrl, "{}", profile, ct, "GET");
            }

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                {
                    return new BodySchemaResolveResult
                    {
                        Success = false,
                        Source = "Describe",
                        ErrorMessage = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} (POST + GET denendi)",
                        HttpStatusCode = (int)resp.StatusCode,
                        RawResponse = raw,
                    };
                }

                // NetOpenX Describe genellikle {"Data":"<json string>"} formati doner
                var bodyJson = ParseDescribePayload(raw);
                if (string.IsNullOrWhiteSpace(bodyJson))
                {
                    return new BodySchemaResolveResult
                    {
                        Success = false,
                        Source = "Describe",
                        ErrorMessage = "Describe cevabi parse edilemedi.",
                        HttpStatusCode = (int)resp.StatusCode,
                        RawResponse = raw,
                    };
                }

                return new BodySchemaResolveResult
                {
                    Success = true,
                    Source = "Describe",
                    BodyJson = bodyJson,
                    HttpStatusCode = (int)resp.StatusCode,
                };
            }
        }
        catch (Exception ex)
        {
            return new BodySchemaResolveResult
            {
                Success = false,
                Source = "Describe",
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Describe cevabini parse + sterilize eder.
    /// NetOpenX format: {"IsSuccessful":true,"Data":"{\"FatUst\":{...}}", "ErrorMessage":null}
    /// Bazi versionlarda dogrudan JSON object donebilir; her iki durumu da destekle.
    /// IsSuccessful=false ise null doner — caller (ResolveAsync) Sample GET'e dusurur.
    /// </summary>
    private static string? ParseDescribePayload(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Netsis NoxRest zarfi: IsSuccessful kontrol et
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("IsSuccessful", out var okEl) &&
                okEl.ValueKind == JsonValueKind.False)
            {
                // Describe basarisiz — null don, caller fallback'e gecsin
                return null;
            }

            // Varyant 1: {"Data": "<json string>"}
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("Data", out var dataEl))
            {
                if (dataEl.ValueKind == JsonValueKind.Null)
                    return null;  // Data null — Describe içerik döndüremedi
                if (dataEl.ValueKind == JsonValueKind.String)
                {
                    var inner = dataEl.GetString();
                    if (string.IsNullOrWhiteSpace(inner)) return null;
                    var node = JsonNode.Parse(inner);
                    return Sterilize(node);
                }
                if (dataEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var node = JsonNode.Parse(dataEl.GetRawText());
                    return Sterilize(node);
                }
            }

            // Varyant 2: dogrudan JSON object/array — IsSuccessful zarfı yok ama içerik var mı?
            if (root.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                // IsSuccessful + ErrorCode + ErrorDesc gibi sadece zarf field'lari iceriyorsa null
                if (root.ValueKind == JsonValueKind.Object && IsOnlyEnvelope(root))
                    return null;

                var node = JsonNode.Parse(root.GetRawText());
                return Sterilize(node);
            }
        }
        catch { /* parse hatasi — null don */ }
        return null;
    }

    /// <summary>
    /// Cevap sadece zarf field'larini iceriyorsa true (gercek schema icermez).
    /// IsSuccessful, ErrorCode, ErrorDesc, ErrorMessage, Data — bunlardan baska key yoksa zarf.
    /// </summary>
    private static bool IsOnlyEnvelope(JsonElement obj)
    {
        var envelopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IsSuccessful", "ErrorCode", "ErrorDesc", "ErrorMessage", "Data", "Code", "Message",
        };
        foreach (var prop in obj.EnumerateObject())
        {
            if (!envelopeKeys.Contains(prop.Name)) return false;
        }
        return true;
    }

    private static string? Sterilize(JsonNode? node)
    {
        if (node is null) return null;
        var sanitized = SterilizeNode(node);
        return sanitized?.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode? SterilizeNode(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var kv in obj) result[kv.Key] = SterilizeNode(kv.Value);
            return result;
        }
        if (node is JsonArray arr)
        {
            var result = new JsonArray();
            if (arr.Count > 0)
            {
                var sample = SterilizeNode(arr[0]);
                if (sample is not null) result.Add(sample);
            }
            return result;
        }
        if (node is JsonValue val)
        {
            var el = val.GetValue<JsonElement>();
            return el.ValueKind switch
            {
                JsonValueKind.String => JsonValue.Create(""),
                JsonValueKind.Number => JsonValue.Create(0),
                JsonValueKind.True or JsonValueKind.False => JsonValue.Create(false),
                _ => null,
            };
        }
        return null;
    }

    // ── 2) Probe ───────────────────────────────────────────────────────────

    private async Task<BodySchemaResolveResult> TryProbeAsync(
        IntegrationEndpoint endpoint, IntegrationApiProfile profile, CancellationToken ct)
    {
        var url = (profile.BaseUrl ?? "").TrimEnd('/') + "/" + (endpoint.UrlTemplate ?? "").TrimStart('/');
        var method = (endpoint.HttpMethod ?? "POST").ToUpperInvariant();
        if (method is not ("POST" or "PUT" or "PATCH"))
            return new BodySchemaResolveResult
            {
                Success = false, Source = "Probe",
                ErrorMessage = "Probe sadece POST/PUT/PATCH metotlari icin.",
            };

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var (resp, raw) = await SendWithAuthRetryAsync(client, url, "{}", profile, ct, method);

            using (resp)
            {
                // 400/422 beklenir — probe basarili sayilmaz; sadece raw'i dondur
                // (V2: hata mesajindan zorunlu alanlari otomatik cikar)
                return new BodySchemaResolveResult
                {
                    Success = false,
                    Source = "Probe",
                    ErrorMessage = $"Probe basarisiz (HTTP {(int)resp.StatusCode}). Manuel sablon veya Galeri kullanin.",
                    HttpStatusCode = (int)resp.StatusCode,
                    RawResponse = raw,
                };
            }
        }
        catch (Exception ex)
        {
            return new BodySchemaResolveResult
            {
                Success = false, Source = "Probe", ErrorMessage = ex.Message,
            };
        }
    }

    // ── Ortak: Auth uygulanmis POST + 401 retry ────────────────────────────

    private async Task<(HttpResponseMessage Response, string Body)> SendWithAuthRetryAsync(
        HttpClient client, string url, string body, IntegrationApiProfile profile,
        CancellationToken ct, string method = "POST")
    {
        var (resp, raw) = await SendOnceAsync(client, url, body, profile, method, ct).ConfigureAwait(false);

        // 401 — token expired olabilir; cache invalidate + 1 retry
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            resp.Dispose();
            _auth.InvalidateToken(profile.Id);
            (resp, raw) = await SendOnceAsync(client, url, body, profile, method, ct).ConfigureAwait(false);
        }

        return (resp, raw);
    }

    private async Task<(HttpResponseMessage Response, string Body)> SendOnceAsync(
        HttpClient client, string url, string body, IntegrationApiProfile profile,
        string method, CancellationToken ct)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url);
        // GET/DELETE/HEAD'a body koyma — bazi sunucular reddediyor
        var m = method.ToUpperInvariant();
        if (m is "POST" or "PUT" or "PATCH")
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        await _auth.ApplyAuthAsync(req, profile, ct).ConfigureAwait(false);
        var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        var raw  = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (resp, raw);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// URL'den Resource adini cikar.
    /// "/api/v2/ItemSlips" → "ItemSlips"
    /// "/api/v2/ARPs/CustomerRisk" → "ARPs"
    /// "/api/v2/Items/Describe" → "Items"
    /// </summary>
    private static string? ExtractResource(string? urlTemplate)
    {
        if (string.IsNullOrWhiteSpace(urlTemplate)) return null;
        var parts = urlTemplate.Trim('/').Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "v2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parts[i], "v1", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 1];
            }
        }
        return parts.LastOrDefault();
    }
}
