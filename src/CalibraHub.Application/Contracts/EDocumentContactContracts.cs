namespace CalibraHub.Application.Contracts;

/// <summary>Belgeye bagli carinin ekranda gosterilecek ozeti.</summary>
public sealed record EDocumentContactLinkDto(int ContactId, string AccountCode, string AccountTitle);

/// <summary>Cari eslestirme adayi (VKN/TC ile bulunan ya da aramayla listelenen cari).</summary>
public sealed record EDocumentContactCandidateDto(
    int ContactId,
    string AccountCode,
    string AccountTitle,
    string? TaxNumber,
    string? IdentityNumber,
    string? City,
    bool IsTaxMatch);

/// <summary>Toplu eslestirme sonucu.</summary>
public sealed record EDocumentContactMatchResultDto(int Matched, int Ambiguous, int Unmatched);
