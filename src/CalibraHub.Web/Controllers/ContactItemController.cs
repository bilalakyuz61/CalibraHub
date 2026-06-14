using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ContactItem (cari × stok eslestirmesi) JSON CRUD'leri.
/// ContactEdit sayfasi JS tarafindan satir-bazli (anlik) save akisiyla cagirir.
/// </summary>
[Authorize]
[Route("[controller]/[action]")]
public sealed class ContactItemController : Controller
{
    private readonly IContactItemRepository _repo;

    public ContactItemController(IContactItemRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    public async Task<IActionResult> ByContact(int contactId, CancellationToken ct)
    {
        if (contactId <= 0) return Json(Array.Empty<object>());
        var list = await _repo.GetByContactAsync(contactId, ct);
        return Json(list.Select(ToJson));
    }

    public sealed class SaveContactItemInput
    {
        public int? Id { get; set; }
        public int ContactId { get; set; }
        public int ItemId { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
    }

    [HttpPost]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ContactEdit)]
    public async Task<IActionResult> Save([FromBody] SaveContactItemInput? input, CancellationToken ct)
    {
        if (input == null || input.ContactId <= 0 || input.ItemId <= 0)
            return Json(new { success = false, message = "Cari ve stok zorunludur." });

        try
        {
            var entity = new ContactItem
            {
                Id         = input.Id ?? 0,
                ContactId  = input.ContactId,
                ItemId     = input.ItemId,
                VendorCode = NullIfBlank(input.VendorCode),
                VendorName = NullIfBlank(input.VendorName),
                Notes      = NullIfBlank(input.Notes),
                IsActive   = input.IsActive,
                CreatedAt  = DateTime.UtcNow,
            };

            int id;
            if (entity.Id > 0)
            {
                await _repo.UpdateAsync(entity, ct);
                id = entity.Id;
            }
            else
            {
                id = await _repo.AddAsync(entity, ct);
            }
            return Json(new { success = true, id });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    public sealed class DeleteContactItemBody { public int Id { get; set; } }

    [HttpPost]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ContactEdit)]
    public async Task<IActionResult> Delete([FromBody] DeleteContactItemBody? body, CancellationToken ct)
    {
        if (body == null || body.Id <= 0)
            return Json(new { success = false, message = "Gecerli Id gerekli." });
        try
        {
            await _repo.DeleteAsync(body.Id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static object ToJson(ContactItemListRow r) => new
    {
        id         = r.Id,
        contactId  = r.ContactId,
        itemId     = r.ItemId,
        itemCode   = r.ItemCode,
        itemName   = r.ItemName,
        vendorCode = r.VendorCode,
        vendorName = r.VendorName,
        notes      = r.Notes,
        isActive   = r.IsActive,
        createdAt  = r.CreatedAt,
        updatedAt  = r.UpdatedAt,
    };
}
