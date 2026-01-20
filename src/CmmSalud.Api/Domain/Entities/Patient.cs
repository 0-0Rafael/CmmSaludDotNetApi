namespace CmmSalud.Api.Domain.Entities;

public sealed class Patient : BaseEntity
{
    public string DocumentId { get; set; } = default!;
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public DateOnly DateOfBirth { get; set; }
    public string Phone { get; set; } = default!;
    public string Address { get; set; } = default!;
    public string? EmergencyContact { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public List<Appointment> Appointments { get; set; } = new();
    public List<Prescription> Prescriptions { get; set; } = new();
    public List<MedicalHistory> MedicalHistory { get; set; } = new();
}
