using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Notes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class NotesController : Controller
{
    private readonly INoteRepository _noteRepository;
    private readonly IAttachmentRepository _attachmentRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IWebHostEnvironment _env;
    private const long MaxAttachmentBytes = 20L * 1024 * 1024; // 20 MB
    private const string NoteEntityType = "Note";

    public NotesController(
        INoteRepository noteRepository,
        IAttachmentRepository attachmentRepository,
        IUserProfileRepository userProfileRepository,
        IWebHostEnvironment env)
    {
        _noteRepository = noteRepository;
        _attachmentRepository = attachmentRepository;
        _userProfileRepository = userProfileRepository;
        _env = env;
    }

    private string LegacyAttachmentDir(Guid noteId) =>
        Path.Combine(_env.ContentRootPath, "note-attachments", noteId.ToString("N"));

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

        var storedName = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName);
        var attachment = new Attachment
        {
            EntityType    = NoteEntityType,
            EntityId      = noteId.ToString(),
            FileName      = Path.GetFileName(file.FileName),
            StoredName    = storedName,
            ContentType   = file.ContentType,
            FileSize      = file.Length,
            Description   = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedBy     = userId.ToString(),
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
    public async Task<IActionResult> DownloadAttachment(Guid id, CancellationToken cancellationToken)
    {
        var (companyId, userId) = GetCurrentUser();
        var attachment = await _attachmentRepository.GetByIdAsync(id, cancellationToken);
        if (attachment is null || attachment.EntityType != NoteEntityType) return NotFound();

        if (!Guid.TryParse(attachment.EntityId, out var noteId)) return NotFound();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (!IsOwner(note, companyId, userId)) return Forbid();

        var bytes = await _attachmentRepository.GetBinaryAsync(id, cancellationToken);
        if (bytes is { Length: > 0 })
            return File(bytes, attachment.ContentType ?? "application/octet-stream", attachment.FileName);

        // Legacy fallback — eski upload'lar file system'de (note_attachments tablosundan kopyalanmamis).
        var path = Path.Combine(LegacyAttachmentDir(noteId), attachment.StoredName);
        if (System.IO.File.Exists(path))
            return PhysicalFile(path, attachment.ContentType ?? "application/octet-stream", attachment.FileName);

        return NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAttachment(Guid id, CancellationToken cancellationToken)
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
                reminderCount = reminderCounts.TryGetValue(n.Id, out var c) ? c : 0,
            }),
        });
    }

    /// <summary>Not kaydet (yeni veya guncelle) — JSON body.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveJson([FromBody] SaveNoteInput input, CancellationToken cancellationToken)
    {
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
                EncryptionHint   = input.EncryptionHint
            };
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
        var userMap = new Dictionary<Guid, string>();
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
        var validTargets = new List<(Guid Id, string FullName)>();
        if (input.TargetUserIds is not null && input.TargetUserIds.Count > 0)
        {
            var allUsers = await _userProfileRepository.GetAllAsync(cancellationToken);
            var userLookup = allUsers
                .Where(u => u.CompanyId == companyId && u.IsActive)
                .ToDictionary(u => u.Id, u => u.FullName);
            foreach (var tid in input.TargetUserIds.Distinct())
            {
                if (tid == Guid.Empty) continue;
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
            return Json(new { success = true, imported = 0, folderId = (Guid?)null });

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

        int imported = 0, failed = 0;
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
                    UpdatedAt  = enexNote.Created ?? DateTime.Now
                };
                await _noteRepository.SaveAsync(note, cancellationToken);

                foreach (var res in enexNote.Attachments)
                {
                    if (res.Data.Length > MaxAttachmentBytes) continue;   // 20 MB sınırı
                    var storedName = Guid.NewGuid().ToString("N") + Path.GetExtension(res.FileName);
                    var attachment = new Attachment
                    {
                        EntityType    = NoteEntityType,
                        EntityId      = note.Id.ToString(),
                        FileName      = res.FileName,
                        StoredName    = storedName,
                        ContentType   = res.Mime,
                        FileSize      = res.Data.Length,
                        CreatedBy     = userId.ToString(),
                        BinaryContent = res.Data
                    };
                    await _attachmentRepository.AddAsync(attachment, cancellationToken);
                }

                imported++;
            }
            catch
            {
                failed++;
            }
        }

        return Json(new { success = true, imported, failed, folderId = folder.Id });
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

    public sealed record NoteIdInput(Guid Id);
    public sealed record DeleteNoteJsonInput(Guid Id);
    public sealed record TogglePinInput(Guid Id);
    public sealed record SaveFolderJsonInput(string Name, Guid? ParentFolderId);
    public sealed record RenameFolderJsonInput(Guid Id, string Name);
    public sealed record DeleteFolderJsonInput(Guid Id);

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

    private (int CompanyId, Guid UserId) GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var companyIdStr = User.FindFirstValue("company_id") ?? string.Empty;
        Guid.TryParse(userIdStr, out var userId);
        int.TryParse(companyIdStr, out var companyId);
        return (companyId, userId);
    }

    /// <summary>
    /// Not sahipligi kontrolu — HEM kullanici HEM sirket eslesmeli.
    /// Ayni userId farkli sirketlerde bulunabilir (ileride multi-tenant userlar);
    /// dolayisiyla sadece UserId check'i yeterli degil.
    /// </summary>
    private static bool IsOwner(Note? note, int companyId, Guid userId)
        => note is not null && note.UserId == userId && note.CompanyId == companyId;
}

public sealed record MoveNoteRequest(Guid NoteId, Guid? FolderId);
