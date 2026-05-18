using System.Data;
using CalibraHub.Application.Abstractions.DesignProvider;

namespace CalibraHub.Application.Services.DesignProvider;

// ──────────────────────────────────────────────────────────────────────────────
// 2^n ağırlık tablosu:
//   Customer     = 16  (tekil cari — en spesifik)
//   ContactGroup = 8   (cari grubu — gruba ait tum carileri kapsar)
//   User         = 4
//   Branch       = 2
//   Warehouse    = 1
// Toplam ağırlık eşsiz olduğu için DocType + (Customer & User) → 20 gibi her
// kombinasyon farklı sıralama değeri üretir. Sorgu ORDER BY weight DESC yapar.
// Yeni kriter eklerken sonraki kuvveti (32, 64, ...) kullan ve DI'a kaydet.
// ──────────────────────────────────────────────────────────────────────────────

public sealed class CustomerCriterion : IDesignCriterion
{
    public string    ColumnName    => "CustomerId";
    public string    ParameterName => "@CustomerId";
    public int       Weight        => 16;
    public SqlDbType SqlType       => SqlDbType.Int;
    public object?   ExtractValue(DesignSelectionContext c) => c.CustomerId;
}

public sealed class ContactGroupCriterion : IDesignCriterion
{
    public string    ColumnName    => "ContactGroupId";
    public string    ParameterName => "@ContactGroupId";
    public int       Weight        => 8;
    public SqlDbType SqlType       => SqlDbType.Int;
    public object?   ExtractValue(DesignSelectionContext c) => c.ContactGroupId;
}

public sealed class UserCriterion : IDesignCriterion
{
    public string    ColumnName    => "UserId";
    public string    ParameterName => "@UserId";
    public int       Weight        => 4;
    public SqlDbType SqlType       => SqlDbType.UniqueIdentifier;
    public object?   ExtractValue(DesignSelectionContext c) => c.UserId;
}

public sealed class BranchCriterion : IDesignCriterion
{
    public string    ColumnName    => "BranchId";
    public string    ParameterName => "@BranchId";
    public int       Weight        => 2;
    public SqlDbType SqlType       => SqlDbType.Int;
    public object?   ExtractValue(DesignSelectionContext c) => c.BranchId;
}

public sealed class WarehouseCriterion : IDesignCriterion
{
    public string    ColumnName    => "WarehouseId";
    public string    ParameterName => "@WarehouseId";
    public int       Weight        => 1;
    public SqlDbType SqlType       => SqlDbType.Int;
    public object?   ExtractValue(DesignSelectionContext c) => c.WarehouseId;
}
