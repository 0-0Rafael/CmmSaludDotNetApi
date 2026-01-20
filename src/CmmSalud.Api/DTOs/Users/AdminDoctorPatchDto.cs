using System.Text.Json.Serialization;

namespace CmmSalud.Api.DTOs.Users;

public sealed class AdminDoctorPatchDto
{
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; init; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; init; }

    [JsonPropertyName("licenseNumber")]
    public string? LicenseNumber { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("consultationFee")]
    public decimal? ConsultationFee { get; init; }

    [JsonPropertyName("specialty")]
    public string? Specialty { get; init; }

    [JsonPropertyName("specialtyId")]
    public Guid? SpecialtyId { get; init; }
}
