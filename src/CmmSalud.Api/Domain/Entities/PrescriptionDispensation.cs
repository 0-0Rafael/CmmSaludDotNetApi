using CmmSalud.Api.Domain.Enums;

namespace CmmSalud.Api.Domain.Entities;

public sealed class PrescriptionDispensation : BaseEntity
{
    public int DispensationNumber { get; set; }
    public decimal QuantityDispensed { get; set; }
    public string Unit { get; set; } = "unit";
    public decimal Price { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.completed; // simple reuse
    public string? PharmacistNotes { get; set; }
    public DateTime DispensedAt { get; set; } = DateTime.UtcNow;

    public Guid PrescriptionId { get; set; }
    public Prescription Prescription { get; set; } = default!;

    public Guid PharmacyId { get; set; }
    public Pharmacy Pharmacy { get; set; } = default!;
}
