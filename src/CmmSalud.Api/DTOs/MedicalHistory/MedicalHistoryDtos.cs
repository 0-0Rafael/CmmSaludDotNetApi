using System.Text.Json.Serialization;

namespace CmmSalud.Api.DTOs.MedicalHistory;

public sealed record MedicalHistoryCreateDto
{
    [JsonPropertyName("patientId")]
    public Guid PatientId { get; init; }

    [JsonPropertyName("condition")]
    public string Condition { get; init; } = string.Empty;

    [JsonPropertyName("diagnosis")]
    public string? Diagnosis { get; init; }

    [JsonPropertyName("treatment")]
    public string? Treatment { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

public sealed record MedicalHistoryUpdateDto
{
    // ✅ todos opcionales porque es PATCH
    [JsonPropertyName("patientId")]
    public Guid? PatientId { get; init; }

    [JsonPropertyName("condition")]
    public string? Condition { get; init; }

    [JsonPropertyName("diagnosis")]
    public string? Diagnosis { get; init; }

    [JsonPropertyName("treatment")]
    public string? Treatment { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
