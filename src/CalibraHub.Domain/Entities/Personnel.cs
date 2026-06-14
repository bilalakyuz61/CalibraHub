using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Üretim personneli (operator/teknisyen). Sistem kullanıcısından (User) ayrı tablo —
/// üretim DNA'sı (PIN, kart no, vardiya, departman) burada toplanır. UserId opsiyonel:
/// bazı operatörler sisteme login olmaz, sadece tablette PIN/kart ile giriş yapar.
/// Shop-floor PIN/NFC doğrulaması, WorkOrderOperation StartedBy/CompletedBy referansları
/// ve gelecekteki vardiya/devamsızlık yönetimi bu tablonun üzerine inşa edilir.
/// </summary>
[Description("Üretim personneli — fabrika çalışanı kimlik kartı. Tablet shop-floor girişinde PIN/NFC ile doğrulanır. UserId opsiyonel: operatörün sistem login'i varsa link verilir, yoksa sadece üretim katından kayıttır.")]
public sealed class Personnel
{
    public int Id { get; init; }
    public int CompanyId { get; init; }

    /// <summary>Personnel/sicil kodu (örn. "OP-001"). Şirket içinde benzersiz.</summary>
    public required string Code { get; init; }

    /// <summary>Tam ad — kart üstünde gösterim için.</summary>
    public required string FullName { get; init; }

    /// <summary>Ünvan / pozisyon (free text — örn. "Kaynakçı", "CNC Operatörü").</summary>
    public string? Title { get; init; }

    /// <summary>Departman (free text — Faz 5'te DepartmentId FK olabilir).</summary>
    public string? Department { get; init; }

    /// <summary>Tablet PIN'i (4-6 hane). Shop-floor giriş ekranında girilir.</summary>
    public string? PinCode { get; init; }

    /// <summary>NFC kart numarası (RFID/MIFARE). Şirket içinde benzersiz (filtered UNIQUE).</summary>
    public string? CardNo { get; init; }

    /// <summary>true ise shop-floor kuyruğunda görünür. false ise yalnızca yönetim/destek.</summary>
    public bool IsProductionOperator { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Opsiyonel: Sistem kullanıcısı varsa link. Operatör web/desktop login yapabilir mi.</summary>
    public int? UserId { get; init; }

    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Notes { get; init; }

    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
}
