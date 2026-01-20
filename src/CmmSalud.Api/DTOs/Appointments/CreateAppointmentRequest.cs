using System.ComponentModel.DataAnnotations;

namespace CmmSalud.Api.Contracts.Appointments;

public sealed class CreateAppointmentRequest
{
    [Required]
    public Guid DoctorId { get; set; }

    [Required]
    public Guid PatientId { get; set; }

    [Required]
    public DateTime ScheduledDate { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
