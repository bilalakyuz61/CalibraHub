using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface INoteRepository
{
    Task<IReadOnlyCollection<Note>> GetByUserAsync(int companyId, Guid userId, Guid? folderId, CancellationToken cancellationToken);
    Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(Note note, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task TogglePinAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NoteReminder>> GetRemindersAsync(Guid noteId, CancellationToken cancellationToken);
    Task AddReminderAsync(NoteReminder reminder, CancellationToken cancellationToken);
    Task DeleteReminderAsync(Guid reminderId, CancellationToken cancellationToken);

    /// <summary>Verilen not ID'leri icin gonderilmemis (is_sent=0) hatirlatici sayilarini tek query ile doner.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveReminderCountsAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NoteShare>> GetSharesAsync(Guid noteId, CancellationToken cancellationToken);
    Task AddShareAsync(NoteShare share, CancellationToken cancellationToken);
    Task DeleteShareAsync(Guid shareId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<(NoteReminder Reminder, Note Note)>> GetUnsentDueRemindersAsync(CancellationToken cancellationToken);
    Task MarkReminderSentAsync(Guid reminderId, DateTime sentAt, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Note>> GetTrashedAsync(int companyId, Guid userId, CancellationToken cancellationToken);
    Task<int> GetTrashedCountAsync(int companyId, Guid userId, CancellationToken cancellationToken);
    Task RestoreNoteAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task PermanentDeleteNoteAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task EmptyTrashAsync(int companyId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NoteFolder>> GetFoldersAsync(int companyId, Guid userId, CancellationToken cancellationToken);
    Task SaveFolderAsync(NoteFolder folder, CancellationToken cancellationToken);
    Task RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken);
    Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken);

}
