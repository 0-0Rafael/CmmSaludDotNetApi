namespace CmmSalud.Api.DTOs.Users;

public sealed record UpdateUserRequest
{
    public string? Email { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }

    public DoctorDataDto? DoctorData { get; init; }
    public PatientDataDto? PatientData { get; init; }
    public PharmacyDataDto? PharmacyData { get; init; }
}

public sealed record DoctorDataDto
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Phone { get; init; }
    public string? LicenseNumber { get; init; }
    public decimal? ConsultationFee { get; init; }

    // viene como nombre desde el front
    public string? Specialty { get; init; }
}

public sealed record PatientDataDto
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? DocumentId { get; init; }

    // "yyyy-MM-dd"
    public string? DateOfBirth { get; init; }
}

public sealed record PharmacyDataDto
{
    public string? Name { get; init; }
    public string? LicenseNumber { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? Email { get; init; }

    // ✅ extra opcionales (si la DB los requiere o luego los quieres pedir en el front)
    public string? City { get; init; }
    public string? State { get; init; }
    public string? ZipCode { get; init; }
}
