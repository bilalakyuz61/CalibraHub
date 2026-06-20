using System.Data;
using CalibraHub.Application.Abstractions.DesignProvider;

namespace CalibraHub.Application.Services.DesignProvider;

// ──────────────────────────────────────────────────────────────────────────────
// 2^n ağırlık tablosu (spesifiklikten genele doğru):
//   Customer     = 32  (tekil cari — en spesifik)
//   ContactGroup = 16  (cari grubu — gruba ait tüm carileri kapsar)
//   User         = 8
//   Branch       = 4
//   Warehouse    = 2
//   AccountType  = 1   (cari tipi — müşteri/satıcı/her ikisi; en genel)
// Toplam ağırlık eşsiz olduğu için her kombinasyon farklı sıralama değeri
// üretir. Sorgu ORDER BY weight DESC yapar.
// Yeni kriter eklerken sonraki kuvveti (64, 128, ...) kullan ve DI'a kaydet.
// ──────────────────────────────────────────────────────────────────────────────

public sealed class CustomerCriterion : IDesignCriterion
{
    public string    ColumnName    => "CustomerId";
    public string    ParameterName => "@CustomerId";
    public int       Weight        => 32;
    public SqlDbType SqlType       => SqlDbType.Int;
    public object?   ExtractValue(DesignSelectionContext c) => c.CustomerId;
}

public sealed class ContactGroupCriterion : IDesignCriterion
{
    public string    ColumnName    => "ContactGroupId";
    public string    ParameterName => "@ContactGroupId";
    public int       Weight        => 16;
    public SqlDbType SqlType       => SqlDbType.Int;
    public object?   ExtractValue(DesignSelectionContext c) => c.ContactGroupId;
}

public sealed class UserCriterion : IDesignCriterion
{
    public string    ColumnName    => "UserId";
    public string    ParameterName => "@UserId";
    public int       Weight        => 8;
    public SqlDbType SqlType       => SqlDbType.UniqueIdentifier;
    public object?   ExtractValue(DesignSelectionContext c) => c.UserId;
}

public sealed class BranchCriterion : IDesignCriterion
{
    public string    ColumnName    => "BranchId";
    public string    ParameterName => "@BranchId";
    public int       Weight        => 4;
    public SqlDbType SqlType       => SqlDbType.Int;
    public object?   ExtractValue(DesignSelectionContext c) => c.BranchId;
}

public sealed class WarehouseCriterion : IDesignCriterion
{
    public string    ColumnName    => "WarehouseId";
    public string    ParameterName => "@WarehouseId";
    public int       Weight        => 2;
    public SqlDbType SqlType       => SqlDbType.Int;
    public object?   ExtractValue(DesignSelectionContext c) => c.WarehouseId;
}

public sealed class AccountTypeCriterion : IDesignCriterion
{
    public string    ColumnName    => "AccountType";
    public string    ParameterName => "@AccountType";
    public int       Weight        => 1;
    public SqlDbType SqlType       => SqlDbType.TinyInt;
    public object?   ExtractValue(DesignSelectionContext c) => (object?)c.AccountType;
}
