using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
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
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IWebHostEnvironment _env;
    private const long MaxAttachmentBytes = 20L * 1024 * 1024; // 20 MB

    public NotesController(INoteRepository noteRepository, IUserProfileRepository userProfileRepository, IWebHostEnvironment env)
    {
        _noteRepository = noteRepository;
        _userProfileRepository = userProfileRepository;
        _env = env;
    }

    private string AttachmentDir(Guid noteId) =>
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
            if (existing is null || existing.UserId != userId)
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
        var (_, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(id, cancellationToken);
        if (note is not null && note.UserId == userId)
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
        var (_, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (note is not null && note.UserId == userId && input.RemindAt > DateTime.Now)
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
        var (_, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (note is not null && note.UserId == userId)
            await _noteRepository.DeleteReminderAsync(reminderId, cancellationToken);

        return RedirectToAction(nameof(Index), new { id = noteId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddShare(AddShareInput input, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.NoteId, cancellationToken);
        if (note is not null && note.UserId == userId && input.SharedWithUserId != userId)
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
        var (_, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (note is not null && note.UserId == userId)
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
        var (_, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (note is null || note.UserId != userId) return Forbid();

        var attachments = await _noteRepository.GetAttachmentsAsync(noteId, cancellationToken);
        return Json(attachments.Select(a => new
        {
            id          = a.Id,
            fileName    = a.FileName,
            fileSize    = a.FileSize,
            contentType = a.ContentType,
            uploadedAt  = a.UploadedAt.ToString("dd.MM.yyyy HH:mm"),
            description = a.Description
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20 * 1024 * 1024 + 65536)]
    public async Task<IActionResult> UploadAttachment(Guid noteId, IFormFile file, string? description, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(noteId, cancellationToken);
        if (note is null || note.UserId != userId)
            return Json(new { success = false, error = "Erişim reddedildi." });

        if (file is null || file.Length == 0)
            return Json(new { success = false, error = "Dosya boş olamaz." });

        if (file.Length > MaxAttachmentBytes)
            return Json(new { success = false, error = "Dosya boyutu 20 MB sınırını aşıyor." });

        var ext         = Path.GetExtension(file.FileName);
        var storedName  = Guid.NewGuid().ToString("N") + ext;
        var attachment  = new NoteAttachment
        {
            NoteId      = noteId,
            FileName    = Path.GetFileName(file.FileName),
            StoredName  = storedName,
            ContentType = file.ContentType,
            FileSize    = file.Length,
            UploadedAt  = DateTime.Now,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        };

        var dir  = AttachmentDir(noteId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, storedName);

        await using (var fs = System.IO.File.Create(path))
            await file.CopyToAsync(fs, cancellationToken);

        await _noteRepository.AddAttachmentAsync(attachment, cancellationToken);
        return Json(new
        {
            success    = true,
            attachment = new
            {
                id          = attachment.Id,
                fileName    = attachment.FileName,
                fileSize    = attachment.FileSize,
                uploadedAt  = attachment.UploadedAt.ToString("dd.MM.yyyy HH:mm"),
                description = attachment.Description
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(Guid id, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        var attachment = await _noteRepository.GetAttachmentByIdAsync(id, cancellationToken);
        if (attachment is null) return NotFound();

        var note = await _noteRepository.GetByIdAsync(attachment.NoteId, cancellationToken);
        if (note is null || note.UserId != userId) return Forbid();

        var path = Path.Combine(AttachmentDir(attachment.NoteId), attachment.StoredName);
        if (!System.IO.File.Exists(path)) return NotFound();

        return PhysicalFile(path, attachment.ContentType ?? "application/octet-stream", attachment.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(Guid id, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        var attachment = await _noteRepository.GetAttachmentByIdAsync(id, cancellationToken);
        if (attachment is null) return Json(new { success = false, error = "Dosya bulunamadı." });

        var note = await _noteRepository.GetByIdAsync(attachment.NoteId, cancellationToken);
        if (note is null || note.UserId != userId)
            return Json(new { success = false, error = "Erişim reddedildi." });

        var path = Path.Combine(AttachmentDir(attachment.NoteId), attachment.StoredName);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

        await _noteRepository.DeleteAttachmentAsync(id, cancellationToken);
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

        return Json(new
        {
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
                updatedAt = n.UpdatedAt,
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
            if (existing is null || existing.UserId != userId)
                return Json(new { success = false, message = "Not bulunamadi." });

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
        return Json(new { success = true, id = note.Id });
    }

    /// <summary>Not sil — JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteJson([FromBody] DeleteNoteJsonInput input, CancellationToken cancellationToken)
    {
        var (_, userId) = GetCurrentUser();
        var note = await _noteRepository.GetByIdAsync(input.Id, cancellationToken);
        if (note is not null && note.UserId == userId)
            await _noteRepository.DeleteAsync(input.Id, cancellationToken);
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

    public sealed record DeleteNoteJsonInput(Guid Id);
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
}

public sealed record MoveNoteRequest(Guid NoteId, Guid? FolderId);
