using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class DefinitionsController : Controller
{
    private readonly ICardGroupRepository _repo;

    public DefinitionsController(ICardGroupRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    public IActionResult CardGroups() => View();

    [HttpGet]
    public async Task<IActionResult> GetAllCardGroups(int cardType, CancellationToken ct)
    {
        if (cardType is not (1 or 2)) return BadRequest();
        var all = new List<CardGroupDto>();
        for (int level = 1; level <= 5; level++)
        {
            var groups = await _repo.GetByLevelAsync(cardType, level, null, ct);
            all.AddRange(groups.Select(g => new CardGroupDto(
                g.Id, g.CardType, g.Level, g.ParentId, null, g.Code, g.Description)));
        }
        return Json(all);
    }

    [HttpGet]
    public async Task<IActionResult> GetCardGroups(
        int cardType, int level, int? parentId, CancellationToken ct)
    {
        if (cardType is not (1 or 2) || level is < 1 or > 5)
            return BadRequest();

        var groups = await _repo.GetByLevelAsync(cardType, level, parentId, ct);

        // For level > 1, also fetch parent codes for display
        IReadOnlyDictionary<int, string> parentCodes = new Dictionary<int, string>();
        if (level > 1)
        {
            var parents = await _repo.GetByLevelAsync(cardType, level - 1, null, ct);
            parentCodes = parents.ToDictionary(p => p.Id, p => p.Code);
        }

        var result = groups.Select(g => new CardGroupDto(
            g.Id, g.CardType, g.Level, g.ParentId,
            g.ParentId.HasValue && parentCodes.TryGetValue(g.ParentId.Value, out var pc) ? pc : null,
            g.Code, g.Description)).ToList();

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetParentCandidates(int cardType, int level, CancellationToken ct)
    {
        if (cardType is not (1 or 2) || level is < 2 or > 5)
            return BadRequest();

        var parents = await _repo.GetByLevelAsync(cardType, level - 1, null, ct);
        return Json(parents.Select(p => new { p.Id, p.Code, p.Description }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCardGroup([FromBody] SaveCardGroupRequest req, CancellationToken ct)
    {
        if (req.CardType is not (1 or 2)) return BadRequest(new { error = "Geçersiz kart tipi." });
        if (req.Level is < 1 or > 5)     return BadRequest(new { error = "Seviye 1–5 arasında olmalıdır." });
        if (string.IsNullOrWhiteSpace(req.Code)) return BadRequest(new { error = "Grup kodu boş olamaz." });
        if (req.Code.Length > 20) return BadRequest(new { error = "Grup kodu en fazla 20 karakter olabilir." });
        // ParentId is optional for all levels — allows groups without a parent link

        var code = req.Code.Trim().ToUpperInvariant();

        // Düzenleme ise mevcut kaydı al
        CardGroup? existing = req.Id.HasValue ? await _repo.GetByIdAsync(req.Id.Value, ct) : null;
        if (req.Id.HasValue && existing is null) return NotFound();

        // Aynı üst grup altında kod tekrarı kontrolü
        var checkType = existing?.CardType ?? req.CardType;
        var checkLevel = existing?.Level ?? req.Level;
        var checkParent = existing?.ParentId ?? req.ParentId;
        var siblings = await _repo.GetByLevelAsync(checkType, checkLevel, checkParent, ct);
        if (siblings.Any(g => string.Equals(g.Code, code, StringComparison.OrdinalIgnoreCase)
                              && g.Id != (req.Id ?? 0)))
            return BadRequest(new { error = $"Bu grup altında '{code}' kodu zaten kullanılıyor." });

        if (req.Id.HasValue)
        {

            await _repo.UpdateAsync(new CardGroup
            {
                Id = existing.Id,
                CardType = existing.CardType,
                Level = existing.Level,
                ParentId = existing.ParentId,
                Code = code,
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim()
            }, ct);
        }
        else
        {
            await _repo.AddAsync(new CardGroup
            {
                CardType = req.CardType,
                Level = req.Level,
                ParentId = req.ParentId,
                Code = code,
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim()
            }, ct);
        }

        return Ok(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCardGroup([FromBody] DeleteCardGroupRequest req, CancellationToken ct)
    {
        if (await _repo.HasChildrenAsync(req.Id, ct))
            return BadRequest(new { error = "Bu grubun alt grupları var. Önce alt grupları silin." });

        await _repo.DeleteAsync(req.Id, ct);
        return Ok(new { success = true });
    }

    // ── Card-group entity mappings ────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetCardGroupMappings(int entityType, string entityId, CancellationToken ct)
    {
        if (entityType is not (1 or 2) || string.IsNullOrWhiteSpace(entityId))
            return BadRequest();
        var rows = await _repo.GetEntityMappingsAsync(entityType, entityId, ct);
        return Json(rows.Select(r => new
        {
            level       = r.Level,
            cardGroupId = r.CardGroupId,
            code        = r.Code,
            description = r.Description
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCardGroupMappings([FromBody] SaveCardGroupMappingsRequest req, CancellationToken ct)
    {
        if (req.EntityType is not (1 or 2) || string.IsNullOrWhiteSpace(req.EntityId))
            return BadRequest(new { error = "Geçersiz parametre." });

        var levels = req.Levels
            .Select(l => (l.Level, l.CardGroupId))
            .ToList();

        await _repo.SaveEntityMappingsAsync(req.EntityType, req.EntityId, levels, ct);
        return Ok(new { success = true });
    }
}
