namespace CmmSalud.Api.Domain.Entities;

public sealed class PaymentRefund : BaseEntity
{
    public decimal Amount { get; set; }
    public string Reason { get; set; } = default!;
    public DateTime RefundedAt { get; set; } = DateTime.UtcNow;

    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = default!;
}
