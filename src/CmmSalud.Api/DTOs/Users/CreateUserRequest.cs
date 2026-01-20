using System.Text.Json.Serialization;

namespace CmmSalud.Api.DTOs.Users;

public sealed record CreateUserRequest
{
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "patient";

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; init; }

    // ✅ IMPORTANTE:
    // Usamos los mismos DTOs del PATCH (los que tu front ya está mandando)
    [JsonPropertyName("doctorData")]
    public AdminDoctorPatchDto? DoctorData { get; init; }

    [JsonPropertyName("patientData")]
    public AdminPatientPatchDto? PatientData { get; init; }

    [JsonPropertyName("pharmacyData")]
    public AdminPharmacyPatchDto? PharmacyData { get; init; }
}
