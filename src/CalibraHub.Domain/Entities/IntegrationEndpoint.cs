using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// REST endpoint katalogu. Bir auth profile'i (integration_api_profiles) altinda
/// belirli bir URL/method/body schema'sini tanimlar. Integration tablosu bu
/// kayitlarin hedef olarak referans verir.
///
/// Ornek: ApiProfileId = "Netsis Uretim" profile; UrlTemplate = "/api/v2/ItemSlips";
/// HttpMethod = "POST"; BodySchema = "{...JSON schema or ornek...}".
/// </summary>
[Description("REST endpoint katalogu. integration_api_profiles ile FK; URL/method/schema burada.")]
public sealed class IntegrationEndpoint
{
    public int Id { get; init; }

    /// <summary>FK -> integration_api_profiles.id (GUID — legacy table).</summary>
    public Guid ApiProfileId { get; set; }

    /// <summary>Kullaniciya gosterilen ad (orn. "Netsis Siparis POST").</summary>
    public required string Name { get; set; }

    /// <summary>HTTP method: GET / POST / PUT / PATCH / DELETE.</summary>
    public string HttpMethod { get; set; } = "POST";

    /// <summary>integration_api_profiles.base_url'e relative path. Orn. "/api/v2/ItemSlips".</summary>
    public required string UrlTemplate { get; set; }

    /// <summary>Ornek JSON body veya JSON Schema. Wizard Step 2'de tree view ile gosterilir.</summary>
    public string? BodySchema { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime? Updated { get; set; }
}
