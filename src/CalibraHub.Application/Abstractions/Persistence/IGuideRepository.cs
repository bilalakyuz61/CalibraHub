using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// SQL View tabanli jenerik rehber (Lookup) persistence arayuzu.
///
/// Sorumluluklar:
///   - GuideMas metadata cache okuma (PR 3: admin CRUD kaldirildi — UI direkt fiziksel
///     view'lar uzerinden calisiyor; GuideMas sadece startup auto-discovery ve
///     runtime resolve icin metadata cache rolu)
///   - Dinamik SQL inşası ile SQL View uzerinde arama + sayfalama
///   - Tek value'dan display cozumleme (sayfa yukleme senaryosu)
///
/// SQL injection savunmasi: tum identifier'lar (ViewName, kolonlar, sortColumn)
/// regex allowlist'ten geciyor; search parametresi her zaman @Search olarak
/// parametreli, LIKE karakterleri escape ediliyor.
/// </summary>
public interface IGuideRepository
{
    // PR 3: GetAllAsync kaldirildi — UI artik /api/guides/views uzerinden fiziksel
    // view listesini kullaniyor (GuideMas catalog gereksiz).
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
    /// Belirli bir kolonun farkli (DISTINCT) degerlerini doner — distinct
    /// filtre cipleri icin. SADECE GridColumnsJson icindeki kolonlar kabul edilir
    /// (allowlist guvenligi). Sonuclar alfabetik siralanir, NULL/bos atilir,
    /// max 200 satir doner (UI'i bogmamak icin).
    ///
    /// search non-empty ise CAST(...) COLLATE Turkish_CI_AI LIKE %search% filtresi
    /// uygulanir — kullanici popover'in arama kutusuna yazinca alfabetik kuyrukta
    /// kalmis degerler de bulunabilir.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetDistinctValuesAsync(
        GuideDefinition guide,
        string column,
        string? search,
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
    /// View kolonlarinin SQL veri tipini (nvarchar, int, decimal, datetime2, bit vb.)
    /// kolon adina map'ler. Frontend Alan Ayarlari modal'inda kolon yaninda kucuk
    /// chip olarak gosterilir. INFORMATION_SCHEMA.COLUMNS uzerinden okunur.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetViewColumnTypesAsync(string viewName, CancellationToken ct);

    // PR 3: UpsertAsync ve DeleteAsync kaldirildi — admin GuideMas CRUD UI
    // gereksiz; GuideMas startup auto-discovery ile besleniyor (DiscoverAndRegisterGuidesAsync).

    /// <summary>
    /// Startup auto-discovery — sys.views uzerinden 'cbv_Guide_%' pattern'ine uyan
    /// SQL view'larini tarar, GuideMas'ta kaydi olmayanlari otomatik ekler.
    ///
    /// Heuristic:
    ///   - GuideCode: view adinin 'v_Guide' prefix'i cikarildiktan sonra uppercased
    ///     (orn. 'v_GuideContacts' → 'CONTACTACCOUNTS')
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

    /// <summary>
    /// Standart rehber kuralini tum GuideMas kayitlarina uygular: view'da
    /// 'Code' kolonu varsa ValueColumn=Code, 'Name' kolonu varsa DisplayColumn=Name
    /// olarak guncellenir. Idempotent — degisiklik yoksa UPDATE yazilmaz.
    ///
    /// Standart rehberlerde zorunlu kolonlar (Id/Code/Name+) oldugu icin tum
    /// ekranlarda ayni davranis: rehberden secim → input'a Code, gorunume Name.
    /// Discovery heuristic'i de yeni kayitlari ayni kurala uyarak ekler.
    ///
    /// Donus: guncellenen GuideMas kayit sayisi.
    /// </summary>
    Task<int> NormalizeStandardColumnsAsync(CancellationToken ct);

    /// <summary>
    /// Rehber bazli varsayilan WHERE filter fragment'ini guncelle.
    /// Bu rehberin kullanildigi tum form alanlarinda runtime'da otomatik AND ile uygulanir.
    /// guideCode hem GuideCode hem ViewName ile eslesir (GetByCodeAsync ile ayni mantik).
    /// filterJson NULL veya bos ise filtre kaldirilir.
    /// Donus: etkilenen kayit sayisi (0 = guide bulunamadi).
    /// </summary>
    Task<int> SetDefaultFilterAsync(string guideCode, string? filterJson, CancellationToken ct);
}
