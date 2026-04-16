using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// SQL View tabanli jenerik rehber (Lookup) persistence arayuzu.
///
/// Sorumluluklar:
///   - GuideMas katalogu (tanim tablosu) CRUD
///   - Dinamik SQL inşası ile SQL View uzerinde arama + sayfalama
///   - Tek value'dan display cozumleme (sayfa yukleme senaryosu)
///
/// SQL injection savunmasi: tum identifier'lar (ViewName, kolonlar, sortColumn)
/// regex allowlist'ten geciyor; search parametresi her zaman @Search olarak
/// parametreli, LIKE karakterleri escape ediliyor.
/// </summary>
public interface IGuideRepository
{
    Task<IReadOnlyCollection<GuideDefinition>> GetAllAsync(CancellationToken ct);
    Task<GuideDefinition?> GetByCodeAsync(string guideCode, CancellationToken ct);

    /// <summary>
    /// Dinamik arama. guide parametresi hem view adi hem kolon allowlist kaynagi.
    /// search null/bos ise WHERE clause yok, butun satirlar (paginate).
    /// hasMore icin pageSize+1 trick kullanilir (ayri SELECT COUNT yok).
    /// </summary>
    Task<GuideSearchResultDto> SearchAsync(
        GuideDefinition guide,
        string? search,
        int page,
        int pageSize,
        string? sortColumn,
        string? sortDirection,
        CancellationToken ct,
        IReadOnlyCollection<GuideConstraintDto>? constraints = null);

    /// <summary>
    /// Tek value'dan display cozumleme. Sayfa yuklemede WidgetTra'dan okunan
    /// kod (orn. 'CR-001') view'da aranir, DisplayColumn (orn. 'ABC Ltd.') donulur.
    /// </summary>
    Task<GuideResolveDto?> ResolveAsync(
        GuideDefinition guide,
        string value,
        CancellationToken ct);

    /// <summary>
    /// DB'deki cbv_Guide_% pattern'ine uyan tum SQL view'lari listeler.
    /// Admin "Yeni Rehber" modalindaki "SQL View Kaynagi" dropdown'unu besler.
    /// Her view icin kolon listesi de doner — kolon dropdown'lari icin.
    /// </summary>
    Task<IReadOnlyCollection<GuideViewInfoDto>> ListGuideViewsAsync(CancellationToken ct);

    /// <summary>
    /// Belirli bir SQL view'in kolon adlarini ceker.
    /// View adi allowlist'ten gecer — SQL injection guvenli.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetViewColumnsAsync(string viewName, CancellationToken ct);

    /// <summary>
    /// Rehber ekle (Id=0) veya guncelle (Id>0).
    /// GuideCode null ise GuideLabel'dan otomatik uretilir.
    /// Donus: kayit Id'si.
    /// </summary>
    Task<int> UpsertAsync(UpsertGuideRequest request, CancellationToken ct);

    /// <summary>
    /// Rehberi soft-delete ile devre disi birak (IsActive=0).
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// Startup auto-discovery — sys.views uzerinden 'cbv_Guide_%' pattern'ine uyan
    /// SQL view'larini tarar, GuideMas'ta kaydi olmayanlari otomatik ekler.
    ///
    /// Heuristic:
    ///   - GuideCode: view adinin 'v_Guide' prefix'i cikarildiktan sonra uppercased
    ///     (orn. 'v_GuideContactAccounts' → 'CONTACTACCOUNTS')
    ///   - GuideLabel: view adindan cikarilip bosluklu hali
    ///   - ValueColumn: view'in 1. kolonu (ORDINAL_POSITION=1)
    ///   - DisplayColumn: view'in 2. kolonu varsa, yoksa 1. kolonla ayni
    ///   - GridColumnsJson: view'in tum kolonlari JSON array olarak
    ///   - DefaultSortColumn: 1. kolon
    ///
    /// Idempotent: GuideMas'ta ayni GuideCode varsa atlanir. Bir kere eklendikten
    /// sonra admin elle SQL ile GuideMas satirini duzenleyebilir; startup bir daha
    /// uzerine yazmaz.
    ///
    /// Donus: otomatik eklenmis yeni guide sayisi (log icin).
    /// </summary>
    Task<int> DiscoverAndRegisterGuidesAsync(CancellationToken ct);
}
