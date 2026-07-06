namespace CalibraHub.Application.Contracts;

/// <summary>Bölüm satırı — alt bölüm sayısıyla.</summary>
public sealed record LocationSectionDto(int Id, string? Code, string Name, bool IsActive, int SubSectionCount);

/// <summary>Alt bölüm satırı.</summary>
public sealed record LocationSubSectionDto(int Id, int SectionId, string? Code, string Name, bool IsActive);

/// <summary>Alt bölüm board satırı — bölüm adıyla (tüm alt bölümler listesi).</summary>
public sealed record LocationSubSectionListDto(int Id, int SectionId, string SectionName, string Name);
