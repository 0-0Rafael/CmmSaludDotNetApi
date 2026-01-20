using CmmSalud.Api.Domain.Enums;

namespace CmmSalud.Api.Domain.Entities;

public sealed class Prescription : BaseEntity
{
    public string MedicationName { get; set; } = default!;
    public string Dosage { get; set; } = default!;
    public string Frequency { get; set; } = default!;
    public string Duration { get; set; } = default!;
    public string? Instructions { get; set; }

    public DateOnly IssueDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly ExpirationDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    public int MaxDispensations { get; set; } = 1;
    public int CurrentDispensations { get; set; } = 0;

    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.active;
    public string DigitalSignature { get; set; } = default!;
    public DateTime? LastDispensedAt { get; set; }

    // NUEVO: Recetas continuas
    public bool IsContinuous { get; set; } = false;
    public int? RefillEveryDays { get; set; }
    public DateOnly? TreatmentEndDate { get; set; }
    public DateOnly? NextRefillDate { get; set; }

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;

    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = default!;

    public List<PrescriptionDispensation> Dispensations { get; set; } = new();
}
