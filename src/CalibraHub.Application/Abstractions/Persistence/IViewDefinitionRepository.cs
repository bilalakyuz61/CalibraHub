using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// ViewDefinition / ViewDefinitionRevision tabloları için salt-okunur CRUD soyutlaması.
///
/// KASITLI olarak yalnızca OKUMA metodları içerir: yazma (insert/update/revision-snapshot),
/// ViewBuilderService.ApplyAsync içindeki tek transaction'a (CREATE OR ALTER VIEW DDL'i ile
/// birlikte) bağlı kalması gerektiğinden doğrudan ViewBuilderService içinde (aynı
/// SqlConnection/SqlTransaction üzerinde) yapılır — ApprovalSqlQueryService/SqlApprovalFlowRepository
/// ile aynı gerekçe (bkz. ViewBuilderService doc-comment'i).
/// </summary>
public interface IViewDefinitionRepository
{
    /// <summary>IsActive=1 olan tüm override'lar.</summary>
    Task<IReadOnlyList<ViewDefinition>> ListActiveAsync(CancellationToken ct);

    Task<ViewDefinition?> GetByViewNameAsync(string viewName, CancellationToken ct);

    Task<ViewDefinition?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>En son revizyon (RevisionNo yerine Id DESC — insert sırası kronolojik).</summary>
    Task<ViewDefinitionRevision?> GetLatestRevisionAsync(int viewDefinitionId, CancellationToken ct);

    Task<int> CountRevisionsAsync(int viewDefinitionId, CancellationToken ct);

    /// <summary>Tüm ViewDefinitionId'ler için revizyon sayısı — liste ekranında N+1 sorgu yerine
    /// tek gruplu sorgu (bkz. ViewBuilderService.ListViewsAsync).</summary>
    Task<IReadOnlyDictionary<int, int>> CountRevisionsGroupedAsync(CancellationToken ct);
}
