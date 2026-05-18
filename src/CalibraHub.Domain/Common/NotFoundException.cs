namespace CalibraHub.Domain.Common;

/// <summary>
/// Istenen kaynak bulunamadi — repository veya service "yok" sinyali.
/// HTTP 404 mapping'i icin (ApiExceptionMiddleware kullanir).
///
/// Ornek:
///   var doc = await _repo.GetByIdAsync(id)
///       ?? throw new NotFoundException($"Document bulunamadi: {id}");
/// </summary>
public sealed class NotFoundException : Exception
{
    public string? ResourceType { get; }
    public string? ResourceId { get; }

    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} bulunamadi: {resourceId}")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }
}
