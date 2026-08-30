using CalibraHub.Application.Contracts;

namespace CalibraHub.Web.Models.Approval;

/// <summary>Belge–cari eşleştirme modalının modeli.</summary>
public sealed class ContactMatchViewModel
{
    public required int DocumentId { get; init; }
    public required string DocumentNumber { get; init; }
    public string? SenderName { get; init; }
    public string? SenderTaxNumber { get; init; }

    /// <summary>Aramada kullanılan metin (boşsa adaylar VKN/TC eşleşmesinden gelir).</summary>
    public string? Search { get; init; }

    /// <summary>Belgeye şu an bağlı cari (yoksa null).</summary>
    public EDocumentContactLinkDto? Current { get; init; }

    public IReadOnlyList<EDocumentContactCandidateDto> Candidates { get; init; }
        = Array.Empty<EDocumentContactCandidateDto>();
}
