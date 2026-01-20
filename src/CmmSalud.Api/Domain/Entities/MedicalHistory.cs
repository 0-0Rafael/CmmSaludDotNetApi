using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CmmSalud.Api.Domain.Entities;

[Table("MedicalHistory")]
public sealed class MedicalHistory : BaseEntity
{
    [Required]
    public Guid PatientId { get; set; }

    public Patient? Patient { get; set; }

    [Required]
    public string Condition { get; set; } = string.Empty;

    public string? Diagnosis { get; set; }

    public string? Treatment { get; set; }

    public string? Notes { get; set; }
}
