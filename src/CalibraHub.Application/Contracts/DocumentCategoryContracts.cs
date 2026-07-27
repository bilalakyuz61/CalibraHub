namespace CalibraHub.Application.Contracts;

/// <summary>Kullanıcı kod girmez kuralı — bu tabloda Code alanı yok, ValueId olarak Id taşınır.</summary>
public sealed record DocumentCategoryDto(int Id, int? ParentId, string Name, int SortOrder, bool IsActive);

public sealed record CreateDocumentCategoryRequest(int? ParentId, string Name, int SortOrder = 0, bool IsActive = true);

public sealed record UpdateDocumentCategoryRequest(int Id, int? ParentId, string Name, int SortOrder, bool IsActive);
