using System.Security.Claims;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ContactPerson (cariye bagli iletisim kisileri) JSON CRUD'leri + Title lookup endpoint'leri.
/// Route'lar /Finance/... altina mount edilir; ContactEdit sayfasi tarafindan cagrilir.
/// </summary>
[Authorize]
// ContactPerson, Cari (CONTACT_EDIT) altyapısı — Cari yetkisi geçerli.
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.ContactEdit)]
public sealed class ContactPersonController : Controller
{
    private readonly IContactPersonRepository _repo;
    private readonly IContactPersonTitleRepository _titleRepo;

    public ContactPersonController(IContactPersonRepository repo, IContactPersonTitleRepository titleRepo)
    {
        _repo = repo;
        _titleRepo = titleRepo;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    // GET /Finance/ContactPersons?contactId={int}
    [HttpGet("/Finance/ContactPersons")]
    public async Task<IActionResult> List(int contactId, CancellationToken ct)
    {
        if (contactId <= 0) return Json(new { ok = true, data = Array.Empty<object>() });
        var rows = await _repo.GetByContactIdAsync(contactId, ct);
        return Json(new { ok = true, data = rows.Select(ToJson) });
    }

    // GET /Finance/ContactPersonTitles
    // Aktif unvanlari SortOrder, Name siralamasinda doner — modaldaki dropdown'i besler.
    [HttpGet("/Finance/ContactPersonTitles")]
    public async Task<IActionResult> ListTitles(CancellationToken ct)
    {
        var rows = await _titleRepo.GetAllActiveAsync(ct);
        return Json(new
        {
            ok = true,
            data = rows.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                sortOrder = t.SortOrder,
                isSystem = t.IsSystem,
            })
        });
    }

    public sealed class AddTitleRequest
    {
        public string? Name { get; set; }
    }

    // POST /Finance/AddContactPersonTitle  body: { name }
    // Server-side dedup (case-insensitive trim): mevcut varsa onu doner; yoksa
    // IsSystem=false, SortOrder=999 ile yeni kayit acar.
    [HttpPost("/Finance/AddContactPersonTitle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTitle([FromBody] AddTitleRequest? input, CancellationToken ct)
    {
        var name = (input?.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { ok = false, message = "Unvan adi bos olamaz." });
        if (name.Length > 100)
            name = name.Substring(0, 100);

        try
        {
            var existing = await _titleRepo.GetByNameAsync(name, ct);
            if (existing != null)
                return Json(new { ok = true, id = existing.Id, name = existing.Name, existed = true });

            var entity = new ContactPersonTitle
            {
                Name = name,
                SortOrder = 999,
                IsSystem = false,
                IsActive = true,
                CreatedById = CurrentUserId(),
            };
            var id = await _titleRepo.AddAsync(entity, ct);
            return Json(new { ok = true, id, name, existed = false });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed class SaveContactPersonRequest
    {
        public int Id { get; set; }
        public int ContactId { get; set; }
        public int? TitleId { get; set; }
        public string? Title { get; set; }
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Notes { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // POST /Finance/SaveContactPerson
    [HttpPost("/Finance/SaveContactPerson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveContactPersonRequest? input, CancellationToken ct)
    {
        if (input == null || input.ContactId <= 0)
            return Json(new { ok = false, message = "Cari secimi zorunlu." });

        var fullName = (input.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fullName))
            return Json(new { ok = false, message = "Ad Soyad zorunlu." });

        try
        {
            // Title cozumu: TitleId verilmisse onu kullan; verilmemisse Title (string)
            // varsa lookup'tan ada gore bul; yoksa otomatik olustur (auto-promote).
            int? titleId = input.TitleId;
            string titleName;

            if (titleId.HasValue && titleId.Value > 0)
            {
                var t = await _titleRepo.GetByIdAsync(titleId.Value, ct);
                if (t == null)
                    return Json(new { ok = false, message = "Secilen unvan bulunamadi." });
                titleName = t.Name;
            }
            else
            {
                var rawTitle = (input.Title ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(rawTitle))
                    return Json(new { ok = false, message = "Unvan zorunlu." });

                var existing = await _titleRepo.GetByNameAsync(rawTitle, ct);
                if (existing != null)
                {
                    titleId = existing.Id;
                    titleName = existing.Name;
                }
                else
                {
                    var newId = await _titleRepo.AddAsync(new ContactPersonTitle
                    {
                        Name = rawTitle.Length > 100 ? rawTitle.Substring(0, 100) : rawTitle,
                        SortOrder = 999,
                        IsSystem = false,
                        IsActive = true,
                        CreatedById = CurrentUserId(),
                    }, ct);
                    titleId = newId;
                    titleName = rawTitle;
                }
            }

            // 2026-05-29: Duplicate check — ayni cari + ayni TitleId aktif kayit varsa engelle.
            if (titleId.HasValue && titleId.Value > 0)
            {
                var dup = await _repo.ExistsByContactAndTitleAsync(input.ContactId, titleId.Value, input.Id > 0 ? input.Id : null, ct);
                if (dup)
                    return Json(new { ok = false, message = $"Bu carı için '{titleName}' ünvanıyla zaten kayıtlı bir kişi var." });
            }

            var entity = new ContactPerson
            {
                Id          = input.Id,
                ContactId   = input.ContactId,
                TitleId     = titleId,
                FullName    = fullName,
                Phone       = NullIfBlank(input.Phone),
                Email       = NullIfBlank(input.Email),
                Notes       = NullIfBlank(input.Notes),
                IsPrimary   = input.IsPrimary,
                IsActive    = input.IsActive,
                CreatedById = CurrentUserId(),
                UpdatedById = CurrentUserId(),
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

            var saved = await _repo.GetByIdAsync(id, ct);
            return Json(new { ok = true, data = saved is null ? null : ToJson(saved) });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    // POST /Finance/DeleteContactPerson?id={int}
    [HttpPost("/Finance/DeleteContactPerson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (id <= 0) return Json(new { ok = false, message = "Gecerli Id gerekli." });
        try
        {
            await _repo.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    // POST /Finance/DeleteContactPersonTitle?id={int}
    // Soft delete (IsActive=false). IsSystem=true (seed) silinebilir ama kullanima dikkat:
    // mevcut kullanılan bir unvan silinirse, o kayitlardaki TitleId NULL gibi davranır,
    // legacy Title string display'i kalır. Bu nedenle kullanim sayisi > 0 ise uyari ile reddet.
    [HttpPost("/Finance/DeleteContactPersonTitle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTitle(int id, CancellationToken ct)
    {
        if (id <= 0) return Json(new { ok = false, message = "Geçerli Id gerekli." });
        try
        {
            var title = await _titleRepo.GetByIdAsync(id, ct);
            if (title == null) return Json(new { ok = false, message = "Ünvan bulunamadı." });

            var usage = await _titleRepo.GetUsageCountAsync(id, ct);
            if (usage > 0)
                return Json(new { ok = false, message = $"Bu ünvan {usage} kişi tarafından kullanılıyor. Önce o kayıtlardan kaldırın." });

            await _titleRepo.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static object ToJson(ContactPerson p) => new
    {
        id        = p.Id,
        contactId = p.ContactId,
        title     = p.TitleName ?? string.Empty,  // frontend backward-compat alias
        titleId   = p.TitleId,
        titleName = p.TitleName ?? string.Empty,
        fullName  = p.FullName,
        phone     = p.Phone,
        email     = p.Email,
        notes     = p.Notes,
        isPrimary = p.IsPrimary,
        isActive  = p.IsActive,
        created   = p.Created,
        updated   = p.Updated,
    };
}
