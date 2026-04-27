using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Notes;

public sealed class NotesViewModel
{
    public IReadOnlyCollection<NoteListItem> Notes { get; init; } = [];
    public NoteEditorModel Editor { get; init; } = new();
    public IReadOnlyCollection<SelectListItem> ShareableUsers { get; init; } = [];
    public IReadOnlyCollection<NoteFolderItem> Folders { get; init; } = [];
    public Guid? SelectedFolderId { get; init; }
    public bool ShowTrash { get; init; }
    public IReadOnlyCollection<NoteListItem> TrashNotes { get; init; } = [];
    public int TrashCount { get; init; }
}

public sealed class NoteListItem
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public string ContentPreview { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
    public bool IsOwn { get; init; }
    public Guid? FolderId { get; init; }
}

public sealed class NoteEditorModel
{
    public Guid? Id { get; set; }
    public Guid? FolderId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public IReadOnlyCollection<NoteReminderItem> Reminders { get; set; } = [];
    public IReadOnlyCollection<NoteShareItem> Shares { get; set; } = [];
    public bool IsOwn { get; set; } = true;
}

public sealed class NoteReminderItem
{
    public Guid Id { get; init; }
    public DateTime RemindAt { get; init; }
    public bool IsSent { get; init; }
    public ReminderRecurrenceType RecurrenceType { get; init; }
    public string? RecurrenceData { get; init; }
}

public sealed class NoteShareItem
{
    public Guid Id { get; init; }
    public Guid SharedWithUserId { get; init; }
    public required string SharedWithUserName { get; init; }
    public DateTime SharedAt { get; init; }
}

public sealed class NoteFolderItem
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? ParentFolderId { get; init; }
    public int Depth { get; init; }
}

public sealed class SaveNoteInput
{
    public Guid? Id { get; set; }
    public Guid? FolderId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    // Mod 2 (E2E) — tum not sifreli ise true, Content alani JSON-wrap'li ciphertext
    public bool? IsFullyEncrypted { get; set; }
    public string? EncryptionHint { get; set; }
}

public sealed class AddReminderInput
{
    public Guid NoteId { get; set; }
    public DateTime RemindAt { get; set; }
    public ReminderRecurrenceType RecurrenceType { get; set; } = ReminderRecurrenceType.None;
    public string? RecurrenceData { get; set; }
    public ReminderDeliveryChannel DeliveryChannel { get; set; } = ReminderDeliveryChannel.InApp;
    public Guid? TargetUserId { get; set; }
}

public sealed class AddShareInput
{
    public Guid NoteId { get; set; }
    public Guid SharedWithUserId { get; set; }
}

public sealed class DeleteReminderJsonInput
{
    public Guid ReminderId { get; set; }
    public Guid NoteId { get; set; }
}

public sealed class SaveFolderInput
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
    public Guid? ReturnFolderId { get; set; }
}

public sealed class RenameFolderInput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ReturnFolderId { get; set; }
}

public sealed class DueReminderDto
{
    public Guid ReminderId { get; init; }
    public required string NoteTitle { get; init; }
    public DateTime RemindAt { get; init; }
    public int RecurrenceType { get; init; }
}
