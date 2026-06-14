namespace CalibraHub.Domain.Enums;

public enum ApproverType
{
    /// <summary>Sisteme giris yapan herhangi bir kullanici bu adimi onaylayabilir.</summary>
    AnyUser = 0,
    /// <summary>Belirli bir kullanici (UserProfile.Id) bu adimi onaylayabilir. ApproverId = User.Id.</summary>
    SpecificUser = 1,
    /// <summary>
    /// Belirli bir departmanin (veya birden cok departmanin) uyesi olan herhangi bir
    /// kullanici bu adimi onaylayabilir. ApproverId = virgulle ayrilmis Department.Id
    /// listesi (orn. "3,5,7"). Runtime onay kontrolu: kullanicinin DepartmentId set
    /// icinde mi.
    /// </summary>
    Department = 2,
    /// <summary>
    /// Talebi olusturan kullanicinin direkt amiri (OrgChart uzerinden cozulur).
    /// ApproverId tasarim zamaninda BOS — runtime'da CreatedById -> OrgChart parent
    /// uzerinden cozulur. Hicbir manuel kullanici/departman secimi gerekmez.
    /// </summary>
    ManagerOfRequester = 3,
}
