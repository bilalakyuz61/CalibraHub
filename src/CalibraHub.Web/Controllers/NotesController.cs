using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Security;
using CalibraHub.Application.Abstractions.Services;
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
    private readonly IAttachmentRepository _attachmentRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly INoteEncryptionService _noteEncryption;
    private readonly INoteOcrService _noteOcr;
    private readonly string _schema;
    private const long MaxAttachmentBytes = 20L * 1024 * 1024; // 20 MB
    private const string NoteEntityType = "Note";

    public NotesController(
        INoteRepository noteRepository,
        IAttachmentRepository attachmentRepository,
        IUserProfileRepository userProfileRepository,
        SqlServerConnectionFactory connectionFactory,
        INoteEncryptionService noteEncryption,
        INoteOcrService noteOcr,
        CalibraDatabaseOptions dbOptions)
    {
        _noteRepository = noteRepository;
        _attachmentRepository = attachmentRepository;
        _userProfileRepository = userProfileRepository;
        _connectionFactory = connectionFactory;
        _noteEncryption = noteEncryption;
        _noteOcr = noteOcr;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    [HttpGet]
    public async Task<IActionResult> Index(Guid? id, Guid? folderId, bool trash = false, CancellationToken cancellationToken = default)
    {
        var (companyId, userId) = GetCurrentUser();
        var notes = await _noteRepository.GetByUserAsync(companyId, userId, null, cancellationToken);
        var allUsers = await _userProfileRepository.GetAllAsync(cancellationToken);
        var folders = await _noteRepository.GetFoldersAsync(companyId, userId, cancellationToken);
        var trashCount = await _noteRepository.GetTrashedCountAsync(companyId, userId, cancellationToken);

        NoteEditorModel editor;
        if (id.HasValue)
        {
            var note = await _noteRepository.GetByIdAsync(id.Value, cancellationToken);
            if (note is not null)
            {
                var reminders = await _noteRepository.GetRemindersAsync(note.Id, cancellationToken);
                var shares = await _noteRepository.GetSharesAsync(note.Id, cancellationToken);

                editor = new NoteEditorModel
                {
                    Id = note.Id,
                    FolderId = note.FolderId,
                    Title = note.Title,
                    Content = note.Content,
                    IsOwn = note.UserId == userId,
                    Reminders = reminders.Select(r => new NoteReminderItem
                    {
                        Id = r.Id,
                        RemindAt = r.RemindAt,
                        IsSent = r.IsSent,
                        RecurrenceType = r.RecurrenceType,
                        RecurrenceData = r.RecurrenceData
                    }).ToList(),
                    Shares = shares.Select(s =>
                    {
                        var sharedUser = allUsers.FirstOrDefault(u => u.Id == s.SharedWithUserId);
                        return new NoteShareItem
                        {
                            Id = s.Id,
                            SharedWithUserId = s.SharedWithUserId,
                            SharedWithUserName = sharedUser?.FullName ?? s.SharedWithUserId.ToString(),
                            SharedAt = s.SharedAt
                        };
                    }).ToList()
                };
            }
            else
            {
                editor = new NoteEditorModel { FolderId = folderId };
            }
        }
        else
        {
            editor = new NoteEditorModel { FolderId = folderId };
        }

        var shareableUsers = allUsers
            .Where(u => u.Id != userId && u.IsActive)
            .Select(u => new SelectListItem(u.FullName, u.Id.ToString()))
            .ToList();

        var noteListItems = notes.Select(n => new NoteListItem
        {
            Id = n.Id,
            Title = n.Title,
            ContentPreview = n.Content.Length > 80 ? n.Content[..80] + "…" : n.Content,
            UpdatedAt = n.UpdatedAt,
            IsOwn = n.UserId == userId,
            FolderId = n.FolderId
        }).ToList();

        IReadOnlyCollection<NoteListItem> trashItems = [];
        if (trash)
        {
            var trashed = await _noteRepository.GetTrashedAsync(companyId, userId, cancellationToken);
            trashItems = trashed.Select(n => new NoteListItem
            {
                Id = n.Id,
                Title = n.Title,
                ContentPreview = n.Content.Length > 80 ? n.Content[..80] + "…" : n.Content,
                UpdatedAt = n.UpdatedAt,
                IsOwn = true,
                FolderId = n.FolderId
            }).ToList();
        }

        var viewModel = new NotesViewModel
        {
            Notes = noteListItems,
            Editor = editor,
            ShareableUsers = shareableUsers,
            Folders = FlattenFolders(folders),
            SelectedFolderId = folderId,
            ShowTrash = trash,
            TrashNotes = trashItems,
            TrashCount = trashCount
        };

        return View(viewModel);
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
        await _attachmentRepository.DeleteByEntityAsync(NoteEntityType, id.ToString(), cancellationToken);
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

        var attachments = await _attachmentRepository.GetByEntityAsync(NoteEntityType, noteId.ToString(), cancellationToken);
        return Json(attachments.Select(a => new
        {
            id          = a.Id,
            fileName    = a.FileName,
            fileSize    = a.FileSize,
            contentType = a.ContentType,
            uploadedAt  = a.Created.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            description = a.Description
        }));
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

        var attachment = new Attachment
        {
            EntityType    = NoteEntityType,
            EntityId      = noteId.ToString(),
            FileName      = Path.GetFileName(file.FileName),
            ContentType   = file.ContentType,
            FileSize      = file.Length,
            Description   = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            BinaryContent = bytes
        };

        await _attachmentRepository.AddAsync(attachment, cancellationToken);
        return Json(new
        {
            success    = true,
            attachment = new
            {
                id          = attachment.Id,
                fileName    = attachment.FileName,
                fileSize    = attachment.FileSize,
                uploadedAt  = attachment.Created.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                description = attachment.Description
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int id, bool inline = false, CancellationToken cancellationToken = default)
    {
        var (companyId, userId) = GetCurrentUser();
        var attachment = await _attachmentRepository.GetByIdAsync(id, cancellationToken);
        if (attachment is null || attachment.EntityType != NoteEntityType) return NotFound();

        if (!Guid.TryParse(attachment.EntityId, out var noteId)) return NotFound();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (!IsOwner(note, companyId, userId)) return Forbid();

        var bytes = await _attachmentRepository.GetBinaryAsync(id, cancellationToken);
        if (bytes is not { Length: > 0 }) return NotFound();

        var contentType = attachment.ContentType ?? "application/octet-stream";
        if (inline)
        {
            Response.Headers.Append("Content-Disposition", $"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}");
            return File(bytes, contentType);
        }
        return File(bytes, contentType, attachment.FileName);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAttachment(int id, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var attachment = await _attachmentRepository.GetByIdAsync(id, cancellationToken);
        if (attachment is null || attachment.EntityType != NoteEntityType)
            return Json(new { success = false, error = "Dosya bulunamadı." });

        if (!Guid.TryParse(attachment.EntityId, out var noteId))
            return Json(new { success = false, error = "Dosya bulunamadı." });

        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (!IsOwner(note, companyId, userId))
            return Json(new { success = false, error = "Erişim reddedildi." });

        await _attachmentRepository.DeleteAsync(id, cancellationToken);
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
            return Json(new { success = false, error = $"Dosya okunamadı: {ex.Message}" });
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
                    var attachment = new Attachment
                    {
                        EntityType    = NoteEntityType,
                        EntityId      = note.Id.ToString(),
                        FileName      = res.FileName,
                        ContentType   = res.Mime,
                        FileSize      = res.Data.Length,
                        BinaryContent = res.Data
                    };
                    await _attachmentRepository.AddAsync(attachment, cancellationToken);
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
        await _attachmentRepository.DeleteByEntityAsync(NoteEntityType, input.Id.ToString(), cancellationToken);
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
            var attachments = await _attachmentRepository.GetByEntityAsync(NoteEntityType, note.Id.ToString(), cancellationToken);
            ViewBag.Attachments = attachments
                .Where(a => a.IsActive)
                .Select(a => new { a.Id, a.FileName, a.FileSize, a.ContentType, a.Description })
                .ToList();
        }

        return View("Public", note);
    }

    /// <summary>Herkese açık notta paylaşılan eki indir — login gerektirmez; share token + ek sahipliği doğrulanır.</summary>
    [AllowAnonymous]
    [HttpGet("/Notes/PublicAttachment")]
    public async Task<IActionResult> PublicAttachment(int cid, string t, int aid, CancellationToken cancellationToken)
    {
        if (cid <= 0 || string.IsNullOrWhiteSpace(t) || t.Length > 40 || aid <= 0)
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

        // Ek bu nota ait mi?
        var attachment = await _attachmentRepository.GetByIdAsync(aid, cancellationToken);
        if (attachment is null || !attachment.IsActive) return NotFound();
        if (attachment.EntityType != NoteEntityType || attachment.EntityId != noteId.ToString()) return NotFound();

        var bytes = await _attachmentRepository.GetBinaryAsync(aid, cancellationToken);
        if (bytes is null || bytes.Length == 0) return NotFound();

        var contentType = attachment.ContentType ?? "application/octet-stream";
        Response.Headers.Append("Content-Disposition", $"attachment; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}");
        return File(bytes, contentType);
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
            return Json(new { ok = false, error = ex.Message });
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
