namespace CalibraHub.Domain.Enums;

public enum ApprovalRuleType
{
    Always = 0,
    MinAmount = 1,
    MaxAmount = 2,
    AmountRange = 3,
    SenderTaxNo = 4,
    /// <summary>
    /// Belge belirli bir/bir kac departmanin sorumlulugundaysa akis uygulanir.
    /// RuleValue = virgulle ayrilmis Department.Id listesi (orn. "3,5,7"). Tek kural
    /// satirinda birden cok departman tutar — belge bunlardan herhangi birine
    /// aitse esleme saglanir (OR mantigi).
    /// </summary>
    Department = 5,
}
