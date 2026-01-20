namespace CmmSalud.Api.Domain.Enums;

public enum PrescriptionStatus
{
    active = 0,
    used = 1,
    expired = 2,
    cancelled = 3,
    regenerated = 4,
    hidden = 5,
    paused = 6,
    completed = 7
}
