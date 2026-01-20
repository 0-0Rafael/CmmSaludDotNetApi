namespace CmmSalud.Api.Domain.Entities;

public sealed class Specialty : BaseEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Doctor> Doctors { get; set; } = new();
}
