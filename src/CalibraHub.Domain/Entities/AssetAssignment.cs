using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Varlık zimmet hareketi — bir varlığın bir personele zimmetlenmesi. AssignDate=zimmete verme tarihi, ReturnDate=geri alma tarihi (NULL ise halen zimmette). Zimmet geçmişi (hareketler) bu kayıtlardan izlenir; aktif zimmet = ReturnDate IS NULL.")]
public sealed class AssetAssignment
{
    public int Id { get; init; }
    public int CompanyId { get; init; }

    /// <summary>FK Asset.Id — zimmetlenen varlık.</summary>
    public int AssetId { get; init; }

    /// <summary>FK Personnel.Id — zimmetlenen personel (opsiyonel). Zimmet kişiye VEYA departmana yapılabilir.</summary>
    public int? PersonnelId { get; init; }

    /// <summary>FK Department.Id — zimmet anındaki sorumlu departman (opsiyonel). Personel yoksa zimmet hedefi budur.</summary>
    public int? DepartmentId { get; init; }

    /// <summary>FK Location.Id — zimmet anındaki fiziki lokasyon (opsiyonel). Zimmet hareketiyle birlikte seçilir.</summary>
    public int? LocationId { get; init; }

    /// <summary>Zimmete verme tarihi.</summary>
    public DateTime AssignDate { get; init; }

    /// <summary>Geri alma (iade) tarihi. NULL → halen zimmette.</summary>
    public DateTime? ReturnDate { get; init; }

    public string? AssignNote { get; init; }
    public string? ReturnNote { get; init; }

    /// <summary>Zimmet belge no (basılan zimmet formu referansı).</summary>
    public string? DocumentNo { get; init; }

    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
    public int? CreatedById { get; init; }
    public int? UpdatedById { get; init; }
}
