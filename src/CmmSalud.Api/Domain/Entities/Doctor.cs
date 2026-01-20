namespace CmmSalud.Api.Domain.Entities;

public sealed class Doctor : BaseEntity
{
    public string DocumentId { get; set; } = default!;
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string LicenseNumber { get; set; } = default!;
    public string Phone { get; set; } = default!;
    public decimal ConsultationFee { get; set; }
    public bool AcceptsInsurance { get; set; } = false;

    public Guid SpecialtyId { get; set; }
    public Specialty Specialty { get; set; } = default!;

    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public List<Appointment> Appointments { get; set; } = new();
    public List<Prescription> Prescriptions { get; set; } = new();

    // ✅ 1 a 1 con DoctorAssets (tabla), entidad singular DoctorAsset
    public DoctorAsset? Assets { get; set; }
}
