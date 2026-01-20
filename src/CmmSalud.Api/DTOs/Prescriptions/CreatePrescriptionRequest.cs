using System.ComponentModel.DataAnnotations;

namespace CmmSalud.Api.DTOs.Prescriptions;

public sealed record CreatePrescriptionRequest
{
    [Required] public Guid PatientId { get; init; }
    [Required] public Guid DoctorId { get; init; }

    [Required] public string MedicationName { get; init; } = "";
    [Required] public string Dosage { get; init; } = "";
    [Required] public string Frequency { get; init; } = "";

    public string? Duration { get; init; }
    public string? Instructions { get; init; }
    public string? Diagnosis { get; init; }

    // ✅ continua
    public bool IsContinuous { get; init; } = false;
    public int? RefillEveryDays { get; init; }
    public DateOnly? TreatmentEndDate { get; init; }
}
