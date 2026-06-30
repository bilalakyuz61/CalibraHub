using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Security;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Notes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.Notes)]
public sealed class NotesController : Controller
{
    private readonly INoteRepository _noteRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly INoteEncryptionService _noteEncryption;
    private readonly INoteOcrService _noteOcr;
    private readonly string _schema;
    private const long MaxAttachmentBytes = 20L * 1024 * 1024; // 20 MB

    public NotesController(
        INoteRepository noteRepository,
        IUserProfileRepository userProfileRepository,
        SqlServerConnectionFactory connectionFactory,
        INoteEncryptionService noteEncryption,
        INoteOcrService noteOcr,
        CalibraDatabaseOptions dbOptions)
    {
        _noteRepository = noteRepository;
        _userProfileRepository = userProfileRepository;
        _connectionFactory = connectionFactory;
        _noteEncryption = noteEncryption;
        _noteOcr = noteOcr;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    // -- Not ekleri � note_attachments (company DB) -----------------------------
    // Merkezi dbo.Attachment (master DB) yerine per-company note_attachments kullanilir.
    // FormId+RefId INT semasiyla uyumlu; not ekleri merkezi tabloya yazilmaz.

    private async Task<List<object>> GetNoteAttachmentsAsync(Guid noteId, CancellationToken ct)
    {
        var list = new List<object>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[FileName],[FileSize],[content_type],[description],[UploadedAt]
            FROM [{_schema}].[note_attachments]
            WHERE [note_id] = @NoteId AND ([IsActive] IS NULL OR [IsActive] = 1)
            ORDER BY [UploadedAt];
            """;
        cmd.Parameters.Add(new SqlParameter("@NoteId", noteId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new
            {
                id          = r.GetGuid(0).ToString(),
                fileName    = r.GetString(1),
                fileSize    = r.GetInt64(2),
                contentType = r.IsDBNull(3) ? null : r.GetString(3),
                description = r.IsDBNull(4) ? null : r.GetString(4),
                uploadedAt  = r.GetDateTime(5).ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            });
        }
        return list;
    }

    private async Task<(Guid AttachmentId, string FileName, string? ContentType, byte[]? Content)?> GetNoteAttachmentBinaryAsync(Guid attachmentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [FileName],[content_type],[binary_content]
            FROM [{_schema}].[note_attachments]
            WHERE [id] = @Id AND ([IsActive] IS NULL OR [IsActive] = 1);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", attachmentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var bytes = r.IsDBNull(2) ? null : (byte[])r[2];
        return (attachmentId, r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), bytes);
    }

    private async Task<Guid?> GetNoteAttachmentOwnerAsync(Guid attachmentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [note_id] FROM [{_schema}].[note_attachments] WHERE [id] = @Id AND ([IsActive] IS NULL OR [IsActive] = 1);";
        cmd.Parameters.Add(new SqlParameter("@Id", attachmentId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (Guid)result;
    }

    private async Task SoftDeleteNoteAttachmentAsync(Guid attachmentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE [{_schema}].[note_attachments] SET [IsActive] = 0 WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", attachmentId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SoftDeleteNoteAttachmentsByNoteAsync(Guid noteId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE [{_schema}].[note_attachments] SET [IsActive] = 0 WHERE [note_id] = @NoteId;";
        cmd.Parameters.Add(new SqlParameter("@NoteId", noteId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task InsertNoteAttachmentAsync(Guid noteId, string fileName, string? contentType, long fileSize, string? description, byte[] content, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO [{_schema}].[note_attachments]
                ([id],[note_id],[FileName],[stored_name],[content_type],[FileSize],[UploadedAt],[description],[binary_content],[IsActive])
            VALUES
                (@Id,@NoteId,@FileName,'',@ContentType,@FileSize,SYSUTCDATETIME(),@Description,@Content,1);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",          Guid.NewGuid()));
        cmd.Parameters.Add(new SqlParameter("@NoteId",      noteId));
        cmd.Parameters.Add(new SqlParameter("@FileName",    fileName));
        cmd.Parameters.Add(new SqlParameter("@ContentType", (object?)contentType ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@FileSize",    fileSize));
        cmd.Parameters.Add(new SqlParameter("@Description", (object?)description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Content",     (object)content) { SqlDbType = System.Data.SqlDbType.VarBinary });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    [HttpGet]
    public IActionResult Index()
    {
        // View tamamen React tarafından render edilir (mountNotesWorkspace).
        // Veri GetAllJson endpoint'i üzerinden yüklenir — burada DB çağrısı gerekmez.
        return View();
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SaveNoteInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();

        Note note;
        if (input.Id.HasValue)
        {
            var existing = await _noteRepository.GetByIdAsync(input.Id.Value, cancellationToken);
            if (!IsOwner(existing, companyId, userId))
                return RedirectToAction(nameof(Index));

            existing.Title = string.IsNullOrWhiteSpace(input.Title) ? "Adsız Not" : input.Title.Trim();
            existing.Content = input.Content ?? string.Empty;
            existing.FolderId = input.FolderId;
            existing.UpdatedAt = DateTime.Now;
            note = existing;
        }
        else
        {
            note = new Note
            {
                CompanyId = companyId,
                UserId = userId,
                FolderId = input.FolderId,
                Title = string.IsNullOrWhiteSpace(input.Title) ? "Adsız Not" : input.Title.Trim(),
                Content = input.Content ?? string.Empty
            };
        }

        await _noteRepository.SaveAsync(note, cancellationToken);
        return RedirectToAction(nameof(Index), new { id = note.Id, folderId = note.FolderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, Guid? folderId, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(id, cancellationToken);
        if (IsOwner(note, companyId, userId))
            await _noteRepository.DeleteAsync(id, cancellationToken);

        return RedirectToAction(nameof(Index), new { folderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        await _noteRepository.RestoreNoteAsync(id, userId, cancellationToken);
        return RedirectToAction(nameof(Index), new { trash = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePermanent(Guid id, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        await _noteRepository.PermanentDeleteNoteAsync(id, userId, cancellationToken);
        await SoftDeleteNoteAttachmentsByNoteAsync(id, cancellationToken);
        return RedirectToAction(nameof(Index), new { trash = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmptyTrash(CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        await _noteRepository.EmptyTrashAsync(companyId, userId, cancellationToken);
        return RedirectToAction(nameof(Index), new { trash = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddReminder(AddReminderInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (IsOwner(note, companyId, userId) && input.RemindAt > DateTime.Now)
        {
            var reminder = new NoteReminder
            {
                NoteId = input.NoteId,
                RemindAt = input.RemindAt,
                RecurrenceType = input.RecurrenceType,
                RecurrenceData = string.IsNullOrWhiteSpace(input.RecurrenceData) ? null : input.RecurrenceData.Trim()
            };
            await _noteRepository.AddReminderAsync(reminder, cancellationToken);
        }

        return RedirectToAction(nameof(Index), new { id = input.NoteId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReminder(Guid reminderId, Guid noteId, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (IsOwner(note, companyId, userId))
            await _noteRepository.DeleteReminderAsync(reminderId, cancellationToken);

        return RedirectToAction(nameof(Index), new { id = noteId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddShare(AddShareInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (IsOwner(note, companyId, userId) && input.SharedWithUserId != userId)
        {
            var share = new NoteShare
            {
                NoteId = input.NoteId,
                SharedWithUserId = input.SharedWithUserId
            };
            await _noteRepository.AddShareAsync(share, cancellationToken);
        }

        return RedirectToAction(nameof(Index), new { id = input.NoteId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteShare(Guid shareId, Guid noteId, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (IsOwner(note, companyId, userId))
            await _noteRepository.DeleteShareAsync(shareId, cancellationToken);

        return RedirectToAction(nameof(Index), new { id = noteId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFolder(SaveFolderInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            var folder = new NoteFolder
            {
                CompanyId = companyId,
                UserId = userId,
                Name = input.Name.Trim(),
                ParentFolderId = input.ParentFolderId
            };
            await _noteRepository.SaveFolderAsync(folder, cancellationToken);
            return RedirectToAction(nameof(Index), new { folderId = folder.Id });
        }

        return RedirectToAction(nameof(Index), new { folderId = input.ReturnFolderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameFolder(RenameFolderInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
            if (folders.Any(f => f.Id == input.Id))
                await _noteRepository.RenameFolderAsync(input.Id, input.Name.Trim(), cancellationToken);
        }
        return RedirectToAction(nameof(Index), new { folderId = input.ReturnFolderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFolder(Guid id, Guid? returnFolderId, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
        var folder = folders.FirstOrDefault(f => f.Id == id);
        if (folder is not null)
            await _noteRepository.DeleteFolderAsync(id, cancellationToken);

        var redirectFolderId = returnFolderId == id ? null : returnFolderId;
        return RedirectToAction(nameof(Index), new { folderId = redirectFolderId });
    }

    // ── Dosya Ekleri ──────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAttachments(Guid noteId, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (!IsOwner(note, companyId, userId)) return Forbid();

        var attachments = await GetNoteAttachmentsAsync(noteId, cancellationToken);
        return Json(attachments);
    }

    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024 + 65536)]
    public async Task<IActionResult> UploadAttachment(Guid noteId, IFormFile file, string? description, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { success = false, error = "Erişim reddedildi." });

        if (file is null || file.Length == 0)
            return Json(new { success = false, error = "Dosya boş olamaz." });

        if (file.Length > MaxAttachmentBytes)
            return Json(new { success = false, error = "Dosya boyutu 20 MB sınırını aşıyor." });

        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, cancellationToken);
            bytes = ms.ToArray();
        }

        var fileName = Path.GetFileName(file.FileName);
        var desc     = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await InsertNoteAttachmentAsync(noteId, fileName, file.ContentType, file.Length, desc, bytes, cancellationToken);
        return Json(new
        {
            success    = true,
            attachment = new
            {
                fileName   = fileName,
                fileSize   = file.Length,
                uploadedAt = DateTime.UtcNow.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                description = desc,
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(Guid id, bool inline = false, CancellationToken cancellationToken = default)
    {
        var (companyId, userId) = GetCurrentUser();
        var noteId = await GetNoteAttachmentOwnerAsync(id, cancellationToken);
        if (noteId is null) return NotFound();
        var note = await _noteRepository.GetByIdAsync(noteId.Value, cancellationToken);
        if (!IsOwner(note, companyId, userId)) return Forbid();

        var att = await GetNoteAttachmentBinaryAsync(id, cancellationToken);
        if (att is null || att.Value.Content is not { Length: > 0 }) return NotFound();

        var contentType = att.Value.ContentType ?? "application/octet-stream";
        if (inline)
        {
            Response.Headers.Append("Content-Disposition", $"inline; filename*=UTF-8''{Uri.EscapeDataString(att.Value.FileName)}");
            return File(att.Value.Content, contentType);
        }
        return File(att.Value.Content, contentType, att.Value.FileName);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAttachment(Guid id, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var noteId = await GetNoteAttachmentOwnerAsync(id, cancellationToken);
        if (noteId is null)
            return Json(new { success = false, error = "Dosya bulunamadi." });

        var note = await _noteRepository.GetByIdAsync(noteId.Value, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { success = false, error = "Erisim reddedildi." });

        await SoftDeleteNoteAttachmentAsync(id, cancellationToken);
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> DueReminders(CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        var due = await _noteRepository.GetUnsentDueRemindersAsync(cancellationToken);
        var userDue = due
            .Where(x => x.Note.UserId == userId)
            .Select(x => new DueReminderDto
            {
                ReminderId = x.Reminder.Id,
                NoteTitle = x.Note.Title,
                RemindAt = x.Reminder.RemindAt,
                RecurrenceType = (int)x.Reminder.RecurrenceType
            })
            .ToList();
        return Json(userDue);
    }

    // POST /Notes/SaveFolderAjax — AJAX klasör ekleme (ağaç refresh için)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFolderAjax(SaveFolderInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            var folder = new NoteFolder
            {
                CompanyId      = companyId,
                UserId         = userId,
                Name           = input.Name.Trim(),
                ParentFolderId = input.ParentFolderId
            };
            await _noteRepository.SaveFolderAsync(folder, cancellationToken);
            return Json(new { success = true, folderId = folder.Id });
        }
        return Json(new { success = false });
    }

    // POST /Notes/RenameFolderAjax — AJAX klasör yeniden adlandırma
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameFolderAjax(Guid id, string name, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
            if (folders.Any(f => f.Id == id))
                await _noteRepository.RenameFolderAsync(id, name.Trim(), cancellationToken);
        }
        return Json(new { success = true });
    }

    // POST /Notes/DeleteFolderAjax — AJAX klasör silme
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFolderAjax(Guid id, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
        if (folders.Any(f => f.Id == id))
            await _noteRepository.DeleteFolderAsync(id, cancellationToken);
        return Json(new { success = true });
    }

    // POST /Notes/MoveNote — Notu farklı klasöre taşı (sürükle-bırak)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveNote([FromBody] MoveNoteRequest req, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(req.NoteId, cancellationToken);
        if (note is null || note.UserId != userId || note.CompanyId != companyId)
            return NotFound();
        note.FolderId = req.FolderId;
        await _noteRepository.SaveAsync(note, cancellationToken);
        return Ok();
    }

    // ── React JSON API ─────────────────────────────────────────────────────

    /// <summary>İçerik olmadan not metadata listesi döner — React ilk yükleme için hızlı endpoint.</summary>
    [HttpGet]
    public async Task<IActionResult> GetListJson(CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var notes = await _noteRepository.GetListByUserAsync(companyId, userId, cancellationToken);
        var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
        var currentUser = await _userProfileRepository.GetByIdAsync(userId, cancellationToken);

        var noteIds = notes.Select(n => n.Id).ToArray();
        var reminderCounts = await _noteRepository.GetActiveReminderCountsAsync(noteIds, cancellationToken);

        return Json(new
        {
            currentUserName = currentUser?.FullName ?? string.Empty,
            companyId = companyId,
            folders = folders.Select(f => new { id = f.Id, name = f.Name, parentId = f.ParentFolderId }),
            notes = notes.Select(n => new
            {
                id = n.Id,
                folderId = n.FolderId,
                title = n.Title,
                // content intentionally omitted — fetched on demand via GetContentJson
                createdAt = n.CreatedAt,
                updatedAt = n.UpdatedAt,
                isPinned = n.IsPinned,
                isFullyEncrypted = n.IsFullyEncrypted,
                encryptionHint = n.EncryptionHint,
                tags = n.Tags,
                linkedEntityType  = n.LinkedEntityType,
                linkedEntityId    = n.LinkedEntityId,
                linkedEntityLabel = n.LinkedEntityLabel,
                visibility        = n.Visibility,
                isOwner           = n.UserId == userId,
                reminderCount = reminderCounts.TryGetValue(n.Id, out var c) ? c : 0,
                shareToken               = n.ShareToken,
                shareIsPublic            = n.ShareIsPublic,
                shareIncludeAttachments  = n.ShareIncludeAttachments,
                ocrText                  = n.OcrText,
            }),
        });
    }

    /// <summary>Tek bir notun şifresi çözülmüş içeriğini döner — lazy load için.</summary>
    [HttpGet]
    public async Task<IActionResult> GetContentJson(Guid noteId, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        var result = await _noteRepository.GetContentByIdAsync(noteId, userId, cancellationToken);
        if (result is null) return NotFound();
        return Json(new { content = result.Value.Content, ocrText = result.Value.OcrText });
    }

    /// <summary>Tum klasor ve notlari JSON olarak doner (React bilesenine ilk yukleme icin).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAllJson(CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var notes = await _noteRepository.GetByUserAsync(companyId, userId, null, cancellationToken);
        var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
        var currentUser = await _userProfileRepository.GetByIdAsync(userId, cancellationToken);

        var noteIds = notes.Select(n => n.Id).ToArray();
        var reminderCounts = await _noteRepository.GetActiveReminderCountsAsync(noteIds, cancellationToken);

        return Json(new
        {
            currentUserName = currentUser?.FullName ?? string.Empty,
            companyId = companyId,
            folders = folders.Select(f => new
            {
                id = f.Id,
                name = f.Name,
                parentId = f.ParentFolderId,
            }),
            notes = notes.Select(n => new
            {
                id = n.Id,
                folderId = n.FolderId,
                title = n.Title,
                content = n.Content,
                createdAt = n.CreatedAt,
                updatedAt = n.UpdatedAt,
                isPinned = n.IsPinned,
                isFullyEncrypted = n.IsFullyEncrypted,
                encryptionHint = n.EncryptionHint,
                tags = n.Tags,
                linkedEntityType  = n.LinkedEntityType,
                linkedEntityId    = n.LinkedEntityId,
                linkedEntityLabel = n.LinkedEntityLabel,
                visibility        = n.Visibility,
                isOwner           = n.UserId == userId,
                reminderCount = reminderCounts.TryGetValue(n.Id, out var c) ? c : 0,
                shareToken               = n.ShareToken,
                shareIsPublic            = n.ShareIsPublic,
                shareIncludeAttachments  = n.ShareIncludeAttachments,
                ocrText                  = n.OcrText,
            }),
        });
    }

    /// <summary>Not kaydet (yeni veya guncelle) — JSON body.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveJson([FromBody] SaveNoteInput input, CancellationToken cancellationToken)
    {
        if (input is null)
            return Json(new { success = false, message = "Geçersiz istek gövdesi." });

        var (companyId, userId) = GetCurrentUser();

        Note note;
        if (input.Id.HasValue)
        {
            var existing = await _noteRepository.GetByIdAsync(input.Id.Value, cancellationToken);
            if (!IsOwner(existing, companyId, userId))
                return Json(new { success = false, message = "Not bulunamadi." });

            existing.Title = string.IsNullOrWhiteSpace(input.Title) ? "Adsız Not" : input.Title.Trim();
            existing.Content = input.Content ?? string.Empty;
            existing.FolderId = input.FolderId;
            existing.UpdatedAt = DateTime.Now;
            existing.IsFullyEncrypted = input.IsFullyEncrypted ?? existing.IsFullyEncrypted;
            existing.EncryptionHint   = input.EncryptionHint  ?? existing.EncryptionHint;
            existing.Tags = input.Tags ?? existing.Tags;
            existing.LinkedEntityType  = input.LinkedEntityType;
            existing.LinkedEntityId    = input.LinkedEntityId;
            existing.LinkedEntityLabel = input.LinkedEntityLabel;
            existing.Visibility        = input.Visibility;
            note = existing;
        }
        else
        {
            note = new Note
            {
                CompanyId = companyId,
                UserId = userId,
                FolderId = input.FolderId,
                Title = string.IsNullOrWhiteSpace(input.Title) ? "Adsız Not" : input.Title.Trim(),
                Content = input.Content ?? string.Empty,
                IsFullyEncrypted = input.IsFullyEncrypted ?? false,
                EncryptionHint   = input.EncryptionHint,
                Tags = input.Tags,
                LinkedEntityType  = input.LinkedEntityType,
                LinkedEntityId    = input.LinkedEntityId,
                LinkedEntityLabel = input.LinkedEntityLabel,
                Visibility        = input.Visibility,
            };
        }

        // E2E şifreli notlarda içerik zaten ciphertext — OCR yapılamaz.
        // Normal notlarda HTML içindeki base64 görseller varsa OCR metni çıkar.
        if (!note.IsFullyEncrypted)
        {
            try
            {
                var ocrText = await _noteOcr.ExtractTextFromImagesAsync(note.Content, cancellationToken);
                note.OcrText = ocrText;
            }
            catch { /* OCR hatası kaydetmeyi engellemesin */ }
        }
        else
        {
            note.OcrText = null; // E2E notlarda önceki OCR metnini temizle
        }

        await _noteRepository.SaveAsync(note, cancellationToken);
        return Json(new { success = true, id = note.Id, isFullyEncrypted = note.IsFullyEncrypted });
    }

    /// <summary>Not sabitleme durumunu degistir — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> TogglePinJson([FromBody] TogglePinInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        // Ownership check: sirket + kullanici eslesmesi
        var existing = await _noteRepository.GetByIdAsync(input.Id, cancellationToken);
        if (!IsOwner(existing, companyId, userId))
            return Json(new { success = false, message = "Erisim reddedildi." });
        await _noteRepository.TogglePinAsync(input.Id, userId, cancellationToken);
        var note = await _noteRepository.GetByIdAsync(input.Id, cancellationToken);
        return Json(new { success = true, isPinned = note?.IsPinned ?? false });
    }

    /// <summary>Not sil — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteJson([FromBody] DeleteNoteJsonInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.Id, cancellationToken);
        if (IsOwner(note, companyId, userId))
            await _noteRepository.DeleteAsync(input.Id, cancellationToken);
        return Json(new { success = true });
    }

    /// <summary>Belirtilen notun tum hatirlaticilarini doner — JSON.</summary>
    [HttpGet]
    public async Task<IActionResult> RemindersJson(Guid noteId, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { success = false, message = "Erisim reddedildi.", reminders = Array.Empty<object>() });

        var reminders = await _noteRepository.GetRemindersAsync(noteId, cancellationToken);

        // Target display isimlerini cekmek icin tek seferlik sirket user map'i
        var userMap = new Dictionary<int, string>();
        if (reminders.Any(r => r.TargetUserIds.Count > 0))
        {
            var users = await _userProfileRepository.GetAllAsync(cancellationToken);
            foreach (var u in users)
            {
                if (u.CompanyId == companyId) userMap[u.Id] = u.FullName;
            }
        }

        return Json(new
        {
            success   = true,
            reminders = reminders
                .OrderBy(r => r.RemindAt)
                .Select(r => new
                {
                    id              = r.Id,
                    remindAt        = r.RemindAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                    isSent          = r.IsSent,
                    sentAt          = r.SentAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    recurrenceType  = (int)r.RecurrenceType,
                    recurrenceData  = r.RecurrenceData,
                    deliveryChannel = (int)r.DeliveryChannel,
                    targets         = r.TargetUserIds
                        .Select(tid => new
                        {
                            id       = tid,
                            fullName = userMap.TryGetValue(tid, out var nm) ? nm : tid.ToString(),
                        }),
                })
        });
    }

    /// <summary>Sirket icindeki aktif kullanicilarin listesi — hatirlatici hedef secimi icin.</summary>
    [HttpGet]
    public async Task<IActionResult> CompanyUsersJson(CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var users = await _userProfileRepository.GetAllAsync(cancellationToken);
        return Json(users
            .Where(u => u.CompanyId == companyId && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                id       = u.Id,
                fullName = u.FullName,
                email    = u.Email,
                isSelf   = u.Id == userId,
            }));
    }

    // ── Kullanıcı bazlı paylaşım (note_shares) ─────────────────────────────

    /// <summary>Bir notun kullanıcı paylaşım listesini döner (kullanıcı adları dahil).</summary>
    [HttpGet]
    public async Task<IActionResult> GetNoteSharesJson(Guid noteId, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { ok = false, error = "Erişim reddedildi." });

        var shares = await _noteRepository.GetSharesAsync(noteId, cancellationToken);
        var users  = await _userProfileRepository.GetAllAsync(cancellationToken);
        var userMap = users.ToDictionary(u => u.Id);

        var result = shares.Select(s =>
        {
            userMap.TryGetValue(s.SharedWithUserId, out var u);
            return new
            {
                shareId  = s.Id,
                userId   = s.SharedWithUserId,
                fullName = u?.FullName ?? "?",
                email    = u?.Email ?? "",
                canEdit  = s.CanEdit,
                sharedAt = s.SharedAt.ToString("yyyy-MM-dd HH:mm"),
            };
        }).ToList();

        return Json(new { ok = true, shares = result });
    }

    /// <summary>Nota kullanıcı paylaşımı ekle veya güncelle (upsert).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUserShareJson([FromBody] SaveUserShareInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { ok = false, error = "Erişim reddedildi." });
        if (input.UserId == userId)
            return Json(new { ok = false, error = "Notu kendinizle paylaşamazsınız." });

        var share = new NoteShare
        {
            Id = Guid.NewGuid(),
            NoteId = input.NoteId,
            SharedWithUserId = input.UserId,
            SharedAt = DateTime.UtcNow,
            CanEdit = input.CanEdit,
        };
        await _noteRepository.AddShareAsync(share, cancellationToken);

        var users = await _userProfileRepository.GetAllAsync(cancellationToken);
        var u = users.FirstOrDefault(x => x.Id == input.UserId);
        return Json(new
        {
            ok = true,
            share = new
            {
                shareId  = share.Id,
                userId   = share.SharedWithUserId,
                fullName = u?.FullName ?? "?",
                email    = u?.Email ?? "",
                canEdit  = share.CanEdit,
                sharedAt = share.SharedAt.ToString("yyyy-MM-dd HH:mm"),
            }
        });
    }

    /// <summary>Kullanıcı paylaşım iznini (canEdit) günceller.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSharePermissionJson([FromBody] UpdateSharePermissionInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { ok = false, error = "Erişim reddedildi." });

        await _noteRepository.UpdateSharePermissionAsync(input.ShareId, input.CanEdit, cancellationToken);
        return Json(new { ok = true });
    }

    /// <summary>Kullanıcı paylaşımını kaldırır.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveUserShareJson([FromBody] RemoveUserShareInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { ok = false, error = "Erişim reddedildi." });

        await _noteRepository.DeleteShareAsync(input.ShareId, cancellationToken);
        return Json(new { ok = true });
    }

    /// <summary>
    /// Mevcut bir notun görsellerini OCR ile yeniden işler ve ocr_text'i günceller.
    /// Özellikle OCR özelliği aktif edilmeden önce kaydedilmiş notlar için kullanılır.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReOcrNoteJson([FromBody] ReOcrNoteInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (note is null || note.CompanyId != companyId || note.IsFullyEncrypted)
            return Json(new { ok = false, ocrText = (string?)null });

        try
        {
            var ocrText = await _noteOcr.ExtractTextFromImagesAsync(note.Content, cancellationToken);
            note.OcrText = ocrText;
            await _noteRepository.SaveAsync(note, cancellationToken);
            return Json(new { ok = true, ocrText });
        }
        catch
        {
            return Json(new { ok = false, ocrText = (string?)null });
        }
    }

    /// <summary>Nota hatirlatici ekle — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> AddReminderJson([FromBody] AddReminderInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { success = false, message = "Erisim reddedildi." });
        if (input.RemindAt <= DateTime.Now)
            return Json(new { success = false, message = "Hatirlatma zamani gelecekte olmali." });

        // Hedef kullanicilar — her biri sirket icinde ve aktif olmali
        var validTargets = new List<(int Id, string FullName)>();
        if (input.TargetUserIds is not null && input.TargetUserIds.Count > 0)
        {
            var allUsers = await _userProfileRepository.GetAllAsync(cancellationToken);
            var userLookup = allUsers
                .Where(u => u.CompanyId == companyId && u.IsActive)
                .ToDictionary(u => u.Id, u => u.FullName);
            foreach (var tid in input.TargetUserIds.Distinct())
            {
                if (tid <= 0) continue;
                if (!userLookup.TryGetValue(tid, out var fn))
                    return Json(new { success = false, message = "Gecersiz hedef kullanici." });
                validTargets.Add((tid, fn));
            }
        }

        var reminder = new NoteReminder
        {
            NoteId          = input.NoteId,
            RemindAt        = input.RemindAt,
            RecurrenceType  = input.RecurrenceType,
            RecurrenceData  = string.IsNullOrWhiteSpace(input.RecurrenceData) ? null : input.RecurrenceData.Trim(),
            DeliveryChannel = input.DeliveryChannel,
            TargetUserIds   = validTargets.Select(v => v.Id).ToArray(),
        };
        await _noteRepository.AddReminderAsync(reminder, cancellationToken);

        return Json(new
        {
            success  = true,
            reminder = new
            {
                id              = reminder.Id,
                remindAt        = reminder.RemindAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                isSent          = false,
                sentAt          = (string?)null,
                recurrenceType  = (int)reminder.RecurrenceType,
                recurrenceData  = reminder.RecurrenceData,
                deliveryChannel = (int)reminder.DeliveryChannel,
                targets         = validTargets.Select(v => new { id = v.Id, fullName = v.FullName }),
            }
        });
    }

    /// <summary>Hatirlatici sil — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteReminderJson([FromBody] DeleteReminderJsonInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { success = false, message = "Erisim reddedildi." });

        await _noteRepository.DeleteReminderAsync(input.ReminderId, cancellationToken);
        return Json(new { success = true });
    }

    /// <summary>Klasor kaydet — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveFolderJson([FromBody] SaveFolderJsonInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        if (string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Klasor adi bos olamaz." });

        var folder = new NoteFolder
        {
            CompanyId = companyId,
            UserId = userId,
            Name = input.Name.Trim(),
            ParentFolderId = input.ParentFolderId
        };
        await _noteRepository.SaveFolderAsync(folder, cancellationToken);
        return Json(new { success = true, id = folder.Id });
    }

    /// <summary>Klasor yeniden adlandir — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> RenameFolderJson([FromBody] RenameFolderJsonInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        if (string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Klasor adi bos olamaz." });

        var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
        if (folders.Any(f => f.Id == input.Id))
            await _noteRepository.RenameFolderAsync(input.Id, input.Name.Trim(), cancellationToken);
        return Json(new { success = true });
    }

    /// <summary>Klasor sil — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteFolderJson([FromBody] DeleteFolderJsonInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
        if (folders.Any(f => f.Id == input.Id))
            await _noteRepository.DeleteFolderAsync(input.Id, cancellationToken);
        return Json(new { success = true });
    }

    /// <summary>Evernote .enex dosyasından notları içe aktarır.</summary>
    [HttpPost]
    [RequestSizeLimit(200 * 1024 * 1024)]   // 200 MB — büyük .enex dosyaları için
    public async Task<IActionResult> ImportEvernote(IFormFile file, Guid? folderId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return Json(new { success = false, error = "Dosya boş olamaz." });

        if (!file.FileName.EndsWith(".enex", StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, error = "Yalnızca .enex dosyaları desteklenir." });

        var (companyId, userId) = GetCurrentUser();

        List<EnexNote> enexNotes;
        try
        {
            await using var stream = file.OpenReadStream();
            enexNotes = EnexImporter.Parse(stream);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = $"Dosya okunamadı: {"Islem sirasinda bir hata olustu."}" });
        }

        if (enexNotes.Count == 0)
            return Json(new { success = true, imported = 0, folderId = (int?)null });

        NoteFolder folder;
        var existingFolders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);

        if (folderId.HasValue)
        {
            // Seçili klasör — sahiplik kontrolü
            var target = existingFolders.FirstOrDefault(f => f.Id == folderId.Value);
            if (target is null)
                return Json(new { success = false, error = "Klasör bulunamadı." });
            folder = target;
        }
        else
        {
            // Klasör seçili değil — dosya adından türet, yoksa oluştur
            var folderName = Path.GetFileNameWithoutExtension(file.FileName).Trim();
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "Evernote İçe Aktarma";

            folder = existingFolders.FirstOrDefault(f =>
                string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase))
                ?? new NoteFolder { CompanyId = companyId, UserId = userId, Name = folderName };

            if (folder.Id == Guid.Empty)
                await _noteRepository.SaveFolderAsync(folder, cancellationToken);
        }

        int imported = 0, failed = 0, totalSkippedAttachments = 0;
        foreach (var enexNote in enexNotes)
        {
            try
            {
                var note = new Note
                {
                    CompanyId  = companyId,
                    UserId     = userId,
                    FolderId   = folder.Id,
                    Title      = string.IsNullOrWhiteSpace(enexNote.Title) ? "Adsız Not" : enexNote.Title.Trim(),
                    Content    = enexNote.HtmlContent,
                    CreatedAt  = enexNote.Created ?? DateTime.Now,
                    UpdatedAt  = enexNote.Updated ?? enexNote.Created ?? DateTime.Now,
                    Tags       = enexNote.Tags.Count > 0
                                    ? System.Text.Json.JsonSerializer.Serialize(enexNote.Tags)
                                    : null,
                };
                await _noteRepository.SaveAsync(note, cancellationToken);

                // EnexImporter zaten 20 MB üstünü atladı; buradaki liste temiz
                foreach (var res in enexNote.Attachments)
                {
                    await InsertNoteAttachmentAsync(note.Id, res.FileName, res.Mime, res.Data.Length, null, res.Data, cancellationToken);
                }

                totalSkippedAttachments += enexNote.SkippedAttachmentCount;
                imported++;
            }
            catch
            {
                failed++;
            }
        }

        return Json(new { success = true, imported, failed, skippedAttachments = totalSkippedAttachments, folderId = folder.Id });
    }

    /// <summary>Çöp kutusundaki (is_deleted=1) notları döner — JSON.</summary>
    [HttpGet]
    public async Task<IActionResult> TrashedJson(CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var trashed = await _noteRepository.GetTrashedAsync(companyId, userId, cancellationToken);
        return Json(trashed.Select(n => new
        {
            id               = n.Id,
            folderId         = n.FolderId,
            title            = n.Title,
            content          = n.Content,
            createdAt        = n.CreatedAt,
            updatedAt        = n.UpdatedAt,
            isPinned         = n.IsPinned,
            isFullyEncrypted = n.IsFullyEncrypted,
        }));
    }

    /// <summary>Çöp kutusundan notu geri yükler — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> RestoreNoteJson([FromBody] NoteIdInput input, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        await _noteRepository.RestoreNoteAsync(input.Id, userId, cancellationToken);
        return Json(new { success = true });
    }

    /// <summary>Notu kalıcı olarak siler (DB'den kaldırır) — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> PermanentDeleteNoteJson([FromBody] NoteIdInput input, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        await _noteRepository.PermanentDeleteNoteAsync(input.Id, userId, cancellationToken);
        await SoftDeleteNoteAttachmentsByNoteAsync(input.Id, cancellationToken);
        return Json(new { success = true });
    }

    /// <summary>Notun genel link durumunu değiştirir — isPublic=true ise token üretir, false ise pasife alır.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSharePublicJson([FromBody] SetSharePublicInput input, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { ok = false, error = "Not bulunamadı." });

        string? token = note!.ShareToken;
        if (input.IsPublic && string.IsNullOrEmpty(token))
            token = Guid.NewGuid().ToString("N"); // 32 hex char, 128-bit random

        await _noteRepository.SetSharePublicAsync(input.NoteId, input.IsPublic, token, input.ShareIncludeAttachments, cancellationToken);

        return Json(new { ok = true, shareToken = input.IsPublic ? token : note.ShareToken, shareIsPublic = input.IsPublic, shareIncludeAttachments = input.ShareIncludeAttachments });
    }

    /// <summary>Genel link ile not görüntüleme — login gerektirmez. cid=companyId, t=token.</summary>
    [AllowAnonymous]
    [HttpGet("/Notes/Public")]
    public async Task<IActionResult> PublicShare(int cid, string t, CancellationToken cancellationToken)
    {
        if (cid <= 0 || string.IsNullOrWhiteSpace(t) || t.Length > 40)
            return NotFound();

        // Güvenlik başlıkları: token arama motoru tarafından indekslenmesin;
        // harici linklere tıklanınca Referer ile token sızdırılmasın.
        Response.Headers.Append("X-Robots-Tag", "noindex, nofollow");
        Response.Headers.Append("Referrer-Policy", "no-referrer");
        Response.Headers.Append("X-Frame-Options", "DENY");

        // Company'ye ait per-company bağlantıyı direkt aç — HTTP context claim'e gerek yok
        var connStr = _connectionFactory.ResolveConnectionStringForCompany(cid);
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [CompanyId], [UserId], [Title], [Content], [Created], [Updated], [FolderId],
                   [IsPinned], [IsFullyEncrypted], [EncryptionHint], [Tags],
                   [linked_entity_type], [linked_entity_id], [linked_entity_label], [visibility],
                   [share_token], [share_is_public], [share_include_attachments]
            FROM [{_schema}].[notes]
            WHERE [share_token] = @Token AND [share_is_public] = 1 AND [IsDeleted] = 0;
            """;
        cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Token", t));
        Note? note = null;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            // Temel alanları manuel map et — içeriği şifre çözme servisiyle oku
            var rawContent = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            note = new Note
            {
                Id                = reader.GetGuid(0),
                CompanyId         = reader.GetInt32(1),
                UserId            = reader.GetInt32(2),
                Title             = reader.GetString(3),
                Content           = _noteEncryption.Unprotect(rawContent) ?? string.Empty,
                CreatedAt         = reader.GetDateTime(5),
                UpdatedAt         = reader.GetDateTime(6),
                FolderId          = reader.IsDBNull(7) ? null : reader.GetGuid(7),
                IsPinned          = !reader.IsDBNull(8) && reader.GetBoolean(8),
                IsFullyEncrypted  = !reader.IsDBNull(9) && reader.GetBoolean(9),
                ShareToken               = reader.IsDBNull(16) ? null : reader.GetString(16),
                ShareIsPublic            = !reader.IsDBNull(17) && reader.GetBoolean(17),
                ShareIncludeAttachments  = !reader.IsDBNull(18) && reader.GetBoolean(18),
            };
        }

        if (note == null) return NotFound();
        if (note.IsFullyEncrypted) return View("PublicEncrypted", note);

        // Not sahibinin adını çek
        await using var ownerConn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await ownerConn.OpenAsync(cancellationToken);
        await using var ownerCmd = ownerConn.CreateCommand();
        ownerCmd.CommandText = $"SELECT [FullName] FROM [{_schema}].[Users] WHERE [Id] = @Id";
        ownerCmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Id", note.UserId));
        var ownerName = await ownerCmd.ExecuteScalarAsync(cancellationToken) as string;
        ViewBag.OwnerName  = ownerName ?? "Bilinmeyen Kullanıcı";
        ViewBag.CompanyId  = cid;
        ViewBag.ShareToken = note.ShareToken;

        // Ekler — sadece share_include_attachments = 1 ise yükle
        if (note.ShareIncludeAttachments)
        {
            ViewBag.Attachments = await GetNoteAttachmentsAsync(note.Id, cancellationToken);
        }

        return View("Public", note);
    }

    /// <summary>Herkese açık notta paylaşılan eki indir — login gerektirmez; share token + ek sahipliği doğrulanır.</summary>
    [AllowAnonymous]
    [HttpGet("/Notes/PublicAttachment")]
    public async Task<IActionResult> PublicAttachment(int cid, string t, Guid aid, CancellationToken cancellationToken)
    {
        if (cid <= 0 || string.IsNullOrWhiteSpace(t) || t.Length > 40 || aid == Guid.Empty)
            return NotFound();

        // Token doğrula + share_include_attachments kontrolü
        var connStr = _connectionFactory.ResolveConnectionStringForCompany(cid);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id] FROM [{_schema}].[notes]
            WHERE [share_token] = @Token AND [share_is_public] = 1 AND [share_include_attachments] = 1 AND [IsDeleted] = 0;
            """;
        cmd.Parameters.Add(new SqlParameter("@Token", t));
        var noteIdObj = await cmd.ExecuteScalarAsync(cancellationToken);
        if (noteIdObj is null) return NotFound();
        var noteId = (Guid)noteIdObj;

        // Ek bu nota ait mi? (note_attachments company DB'de)
        var ownerNoteId = await GetNoteAttachmentOwnerAsync(aid, cancellationToken);
        if (ownerNoteId is null || ownerNoteId.Value != noteId) return NotFound();

        var att = await GetNoteAttachmentBinaryAsync(aid, cancellationToken);
        if (att is null || att.Value.Content is not { Length: > 0 }) return NotFound();

        var contentType = att.Value.ContentType ?? "application/octet-stream";
        Response.Headers.Append("Content-Disposition", $"attachment; filename*=UTF-8''{Uri.EscapeDataString(att.Value.FileName)}");
        return File(att.Value.Content, contentType);
    }

    /// <summary>Mevcut notu kopyalar — aynı başlık + " (Kopya)", aynı içerik/klasör, isPinned=false, isFullyEncrypted=false.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloneNoteJson([FromBody] CloneNoteRequest req, CancellationToken ct)
    {
        try
        {
            var (companyId, userId) = GetCurrentUser();
            var source = await _noteRepository.GetByIdAsync(req.NoteId, ct);
            if (!IsOwner(source, companyId, userId))
                return Json(new { ok = false, error = "Not bulunamadı veya erişim reddedildi." });

            var clone = new Note
            {
                CompanyId        = companyId,
                UserId           = userId,
                FolderId         = source.FolderId,
                Title            = source.Title + " (Kopya)",
                Content          = source.Content,
                IsPinned         = false,
                IsFullyEncrypted = false,
                EncryptionHint   = null,
                Tags             = source.Tags,
            };

            await _noteRepository.SaveAsync(clone, ct);

            return Json(new
            {
                ok   = true,
                note = new
                {
                    id        = clone.Id,
                    title     = clone.Title,
                    content   = clone.Content,
                    folderId  = clone.FolderId,
                    createdAt = clone.CreatedAt,
                    updatedAt = clone.UpdatedAt,
                    tags      = clone.Tags,
                }
            });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>Kayit baglantisi icin entity arama — type: Personnel | Machine | Contact | Document.</summary>
    [HttpGet]
    public async Task<IActionResult> EntitySearchJson(string type, string q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Json(Array.Empty<object>());

        var search = q.Trim();
        var results = await SearchEntitiesAsync(type, search, cancellationToken);
        return Json(results);
    }

    private async Task<IEnumerable<object>> SearchEntitiesAsync(string type, string search, CancellationToken cancellationToken)
    {
        string? sql = type switch
        {
            "Personnel" => $"SELECT TOP 10 [Id], [FullName] AS [Label] FROM [{_schema}].[Personnel] WHERE [IsActive]=1 AND [FullName] LIKE '%'+@q+'%' ORDER BY [FullName]",
            "Machine"   => $"SELECT TOP 10 [Id], [Name] AS [Label] FROM [{_schema}].[Machine] WHERE [IsActive]=1 AND [Name] LIKE '%'+@q+'%' ORDER BY [Name]",
            "Contact"   => $"SELECT TOP 10 [Id], [AccountTitle] AS [Label] FROM [{_schema}].[Contact] WHERE [IsActive]=1 AND [AccountTitle] LIKE '%'+@q+'%' ORDER BY [AccountTitle]",
            "Document"  => $"SELECT TOP 10 [Id], [DocumentNumber] AS [Label] FROM [{_schema}].[Document] WHERE [IsActive]=1 AND [DocumentNumber] LIKE '%'+@q+'%' ORDER BY [Created] DESC",
            _           => null
        };
        if (sql == null) return Array.Empty<object>();

        var list = new List<object>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@q", search));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new { id = reader.GetInt32(0), label = reader.GetString(1) });
        }
        return list;
    }

    public sealed record NoteIdInput(Guid Id);
    public sealed record DeleteNoteJsonInput(Guid Id);
    public sealed record SetSharePublicInput(Guid NoteId, bool IsPublic, bool ShareIncludeAttachments = false);
    public sealed record TogglePinInput(Guid Id);
    public sealed record SaveFolderJsonInput(string Name, Guid? ParentFolderId);
    public sealed record RenameFolderJsonInput(Guid Id, string Name);
    public sealed record DeleteFolderJsonInput(Guid Id);
    public sealed record CloneNoteRequest(Guid NoteId);
    public sealed record SaveUserShareInput(Guid NoteId, int UserId, bool CanEdit = false);
    public sealed record UpdateSharePermissionInput(Guid NoteId, Guid ShareId, bool CanEdit);
    public sealed record RemoveUserShareInput(Guid NoteId, Guid ShareId);
    public sealed record ReOcrNoteInput(Guid NoteId);

    private static IReadOnlyCollection<NoteFolderItem> FlattenFolders(IReadOnlyCollection<NoteFolder> folders)
    {
        var result = new List<NoteFolderItem>();
        AddChildren(null, 0);
        return result;

        void AddChildren(Guid? parentId, int depth)
        {
            foreach (var f in folders.Where(x => x.ParentFolderId == parentId).OrderBy(x => x.Name))
            {
                result.Add(new NoteFolderItem { Id = f.Id, Name = f.Name, ParentFolderId = f.ParentFolderId, Depth = depth });
                AddChildren(f.Id, depth + 1);
            }
        }
    }

    private (int CompanyId, int UserId) GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var companyIdStr = User.FindFirstValue("company_id") ?? string.Empty;
        int.TryParse(userIdStr, out var userId);
        int.TryParse(companyIdStr, out var companyId);
        return (companyId, userId);
    }

    /// <summary>
    /// Not sahipligi kontrolu — HEM kullanici HEM sirket eslesmeli.
    /// Ayni userId farkli sirketlerde bulunabilir (ileride multi-tenant userlar);
    /// dolayisiyla sadece UserId check'i yeterli degil.
    /// </summary>
    private static bool IsOwner(Note? note, int companyId, int userId)
        => note is not null && note.UserId == userId && note.CompanyId == companyId;
}

public sealed record MoveNoteRequest(Guid NoteId, Guid? FolderId);
