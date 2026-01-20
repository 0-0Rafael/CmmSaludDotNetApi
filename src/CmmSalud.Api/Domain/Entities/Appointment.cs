using CmmSalud.Api.Domain.Enums;

namespace CmmSalud.Api.Domain.Entities;

public sealed class Appointment : BaseEntity
{
    public DateTime AppointmentDate { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.scheduled;
    public string Reason { get; set; } = default!;
    public string? Notes { get; set; }
    public decimal Fee { get; set; }
    public bool RequiresPayment { get; set; } = true;
    public bool IsPaid { get; set; } = false;
    public DateTime? ConfirmationDeadline { get; set; }
    public bool IsConfirmed { get; set; } = false;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;

    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = default!;

    public List<Payment> Payments { get; set; } = new();
}
