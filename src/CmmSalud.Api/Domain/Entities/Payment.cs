using CmmSalud.Api.Domain.Enums;

namespace CmmSalud.Api.Domain.Entities;

public sealed class Payment : BaseEntity
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentStatus Status { get; set; } = PaymentStatus.pending;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.cash;
    public PaymentType PaymentType { get; set; } = PaymentType.prepaid;

    public string? TransactionId { get; set; }
    public string? PaymentGateway { get; set; }
    public string? Notes { get; set; }
    public DateTime? PaymentDate { get; set; }
    public DateTime? DueDate { get; set; }

    public Guid AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = default!;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;

    public List<PaymentRefund> Refunds { get; set; } = new();
}
