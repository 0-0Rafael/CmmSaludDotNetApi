namespace CmmSalud.Api.Domain.Entities;

public sealed class DoctorAsset : BaseEntity
{
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = default!;

    public string? SignaturePath { get; set; } // "uploads/doctors/{doctorId}/signature.png"
    public string? SealPath { get; set; }      // "uploads/doctors/{doctorId}/seal.png"
}
