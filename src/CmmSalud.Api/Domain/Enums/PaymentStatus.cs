namespace CmmSalud.Api.Domain.Enums;

public enum PaymentStatus
{
    pending = 0,
    processing = 1,
    completed = 2,
    failed = 3,
    refunded = 4,
    cancelled = 5
}
