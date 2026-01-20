namespace CmmSalud.Api.Domain.Enums;

public enum AppointmentStatus
{
    scheduled = 0,
    confirmed = 1,
    completed = 2,
    cancelled = 3,
    rescheduled = 4,
    no_show = 5
}
