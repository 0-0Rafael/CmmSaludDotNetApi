namespace CmmSalud.Api.Domain.Entities;

public sealed class Pharmacy : BaseEntity
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    private string _licenseNumber = string.Empty;
    public string LicenseNumber
    {
        get => _licenseNumber;
        set => _licenseNumber = value ?? string.Empty;
    }

    private string _pharmacistName = string.Empty;
    public string PharmacistName
    {
        get => _pharmacistName;
        set => _pharmacistName = value ?? string.Empty;
    }

    private string _pharmacistLicense = string.Empty;
    public string PharmacistLicense
    {
        get => _pharmacistLicense;
        set => _pharmacistLicense = value ?? string.Empty;
    }

    private string _address = string.Empty;
    public string Address
    {
        get => _address;
        set => _address = value ?? string.Empty;
    }

    // ✅ estas 2 son las que te están rompiendo en SQL Server (NOT NULL)
    private string _city = string.Empty;
    public string City
    {
        get => _city;
        set => _city = value ?? string.Empty;
    }

    private string _state = string.Empty;
    public string State
    {
        get => _state;
        set => _state = value ?? string.Empty;
    }

    private string? _zipCode;
    public string? ZipCode
    {
        get => _zipCode;
        set => _zipCode = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string _phone = string.Empty;
    public string Phone
    {
        get => _phone;
        set => _phone = value ?? string.Empty;
    }

    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set => _email = value ?? string.Empty;
    }

    private string _operatingHours = string.Empty;
    public string OperatingHours
    {
        get => _operatingHours;
        set => _operatingHours = value ?? string.Empty;
    }

    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; } = false;

    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedBy { get; set; }   // ✅ está en tu tabla

    public string? Notes { get; set; }
}
