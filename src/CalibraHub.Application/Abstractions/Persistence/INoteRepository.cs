using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface INoteRepository
{
    Task<IReadOnlyCollection<Note>> GetByUserAsync(int companyId, int userId, Guid? folderId, CancellationToken cancellationToken);
    /// <summary>İçerik (Content) sütunu olmadan metadata listesi döner — ilk yükleme performansı için.</summary>
    Task<IReadOnlyCollection<Note>> GetListByUserAsync(int companyId, int userId, CancellationToken cancellationToken);
    /// <summary>Tek bir notun içeriğini (Content + OcrText) döner — lazy load için.</summary>
    Task<(string Content, string? OcrText)?> GetContentByIdAsync(Guid noteId, int userId, CancellationToken cancellationToken);
    Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(Note note, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task TogglePinAsync(Guid id, int userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NoteReminder>> GetRemindersAsync(Guid noteId, CancellationToken cancellationToken);
    Task AddReminderAsync(NoteReminder reminder, CancellationToken cancellationToken);
    Task DeleteReminderAsync(Guid reminderId, CancellationToken cancellationToken);

    /// <summary>Verilen not ID'leri icin gonderilmemis (is_sent=0) hatirlatici sayilarini tek query ile doner.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveReminderCountsAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NoteShare>> GetSharesAsync(Guid noteId, CancellationToken cancellationToken);
    /// <summary>Kullanıcıya not paylaşımı ekler veya mevcut paylaşımın can_edit değerini günceller (upsert).</summary>
    Task AddShareAsync(NoteShare share, CancellationToken cancellationToken);
    Task UpdateSharePermissionAsync(Guid shareId, bool canEdit, CancellationToken cancellationToken);
    Task DeleteShareAsync(Guid shareId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<(NoteReminder Reminder, Note Note)>> GetUnsentDueRemindersAsync(CancellationToken cancellationToken);
    Task MarkReminderSentAsync(Guid reminderId, DateTime sentAt, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Note>> GetTrashedAsync(int companyId, int userId, CancellationToken cancellationToken);
    Task<int> GetTrashedCountAsync(int companyId, int userId, CancellationToken cancellationToken);
    Task RestoreNoteAsync(Guid id, int userId, CancellationToken cancellationToken);
    Task PermanentDeleteNoteAsync(Guid id, int userId, CancellationToken cancellationToken);
    Task EmptyTrashAsync(int companyId, int userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NoteFolder>> GetFoldersAsync(int companyId, int userId, CancellationToken cancellationToken);
    Task SaveFolderAsync(NoteFolder folder, CancellationToken cancellationToken);
    Task RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken);
    Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken);

    /// <summary>Genel link token'ına göre notu döner (IsDeleted=0 ve ShareIsPublic=1 şartıyla).</summary>
    Task<Note?> GetByShareTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>Notun genel link durumunu (token + isPublic + includeAttachments) günceller.</summary>
    Task SetSharePublicAsync(Guid noteId, bool isPublic, string? token, bool includeAttachments, CancellationToken cancellationToken);
}
