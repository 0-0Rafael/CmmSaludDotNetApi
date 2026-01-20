using System.Text.Json.Serialization;

namespace CmmSalud.Api.DTOs.Users;

public sealed record AdminUserPatchRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; init; }

    [JsonPropertyName("doctorData")]
    public AdminDoctorPatchDto? DoctorData { get; init; }

    [JsonPropertyName("patientData")]
    public AdminPatientPatchDto? PatientData { get; init; }

    [JsonPropertyName("pharmacyData")]
    public AdminPharmacyPatchDto? PharmacyData { get; init; }
}

// ✅ OJO: AdminDoctorPatchDto YA EXISTE en AdminDoctorPatchDto.cs
// ❌ NO lo declares aquí otra vez, porque rompe con CS0101

public sealed record AdminPatientPatchDto
{
    [JsonPropertyName("firstName")] public string? FirstName { get; init; }
    [JsonPropertyName("lastName")] public string? LastName { get; init; }
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    [JsonPropertyName("address")] public string? Address { get; init; }
    [JsonPropertyName("documentId")] public string? DocumentId { get; init; }
    [JsonPropertyName("dateOfBirth")] public string? DateOfBirth { get; init; }
}

public sealed record AdminPharmacyPatchDto
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("licenseNumber")] public string? LicenseNumber { get; init; }
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    [JsonPropertyName("address")] public string? Address { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("city")] public string? City { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("zipCode")] public string? ZipCode { get; init; }
    [JsonPropertyName("pharmacistName")] public string? PharmacistName { get; init; }
    [JsonPropertyName("pharmacistLicense")] public string? PharmacistLicense { get; init; }
    [JsonPropertyName("operatingHours")] public string? OperatingHours { get; init; }
    [JsonPropertyName("notes")] public string? Notes { get; init; }
}
