using CmmSalud.Api.Common;
using CmmSalud.Api.DTOs.Auth;
using CmmSalud.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        // ✅ Validar formato y edad mínima (>= 18)
        if (!DateOnly.TryParse(req.DateOfBirth, out var dob))
            return BadRequest(new ApiResponse<object>(400, "dateOfBirth inválida. Usa formato yyyy-MM-dd"));

        if (!IsAtLeastAge(dob, 18))
            return BadRequest(new ApiResponse<object>(400, "Debes ser mayor de 18 años para registrarte."));

        var (user, access, refresh) = await _auth.RegisterPatientAsync(
            email: req.Email,
            password: req.Password,
            documentId: req.DocumentId,
            firstName: req.FirstName,
            lastName: req.LastName,
            phone: req.Phone,
            dob: dob,
            address: req.Address,
            ct: ct
        );

        var data = new
        {
            user = MapUser(user),
            access_token = access,
            refresh_token = refresh
        };

        return Ok(new ApiResponse<object>(200, "Registro exitoso", data));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var (user, access, refresh) = await _auth.LoginAsync(req.Email, req.Password, ct);

        var data = new
        {
            user = MapUser(user),
            access_token = access,
            refresh_token = refresh
        };

        return Ok(new ApiResponse<object>(200, "Login exitoso", data));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var (access, refresh) = await _auth.RefreshAsync(req.RefreshToken, ct);

        var data = new
        {
            access_token = access,
            refresh_token = refresh
        };

        return Ok(new ApiResponse<object>(200, "Token renovado", data));
    }

    [HttpPost("forgot-password")]
    public IActionResult ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        // Para tesis/dev: simulación. Luego se conecta a SMTP.
        return Ok(new ApiResponse<object>(
            200,
            "Si el correo existe, se enviará un enlace de recuperación (simulado).",
            new { email = req.Email }
        ));
    }

    private static object MapUser(Domain.Entities.User u)
    {
        return new
        {
            id = u.Id,
            email = u.Email,
            role = u.Role.ToString(),
            isActive = u.IsActive,
            createdAt = u.CreatedAt,
            updatedAt = u.UpdatedAt,
            lastLogin = u.LastLogin,

            patient = u.Patient is null ? null : new
            {
                id = u.Patient.Id,
                firstName = u.Patient.FirstName,
                lastName = u.Patient.LastName,
                documentId = u.Patient.DocumentId,
                phone = u.Patient.Phone,
                address = u.Patient.Address,
                dateOfBirth = u.Patient.DateOfBirth.ToString("yyyy-MM-dd")
            },

            doctor = u.Doctor is null ? null : new
            {
                id = u.Doctor.Id,
                firstName = u.Doctor.FirstName,
                lastName = u.Doctor.LastName,
                documentId = u.Doctor.DocumentId,
                licenseNumber = u.Doctor.LicenseNumber,
                phone = u.Doctor.Phone,
                consultationFee = u.Doctor.ConsultationFee,
                specialtyId = u.Doctor.SpecialtyId,
                specialty = u.Doctor.Specialty is null ? null : new
                {
                    id = u.Doctor.Specialty.Id,
                    name = u.Doctor.Specialty.Name
                }
            },

            pharmacy = u.Pharmacy is null ? null : new
            {
                id = u.Pharmacy.Id,
                name = u.Pharmacy.Name,
                licenseNumber = u.Pharmacy.LicenseNumber,
                pharmacistName = u.Pharmacy.PharmacistName,
                pharmacistLicense = u.Pharmacy.PharmacistLicense,
                address = u.Pharmacy.Address,
                city = u.Pharmacy.City,
                state = u.Pharmacy.State,
                zipCode = u.Pharmacy.ZipCode,
                phone = u.Pharmacy.Phone,
                email = u.Pharmacy.Email,
                operatingHours = u.Pharmacy.OperatingHours,
                isActive = u.Pharmacy.IsActive,
                isVerified = u.Pharmacy.IsVerified,
                verifiedAt = u.Pharmacy.VerifiedAt
            }
        };
    }

    // ✅ Helper edad mínima (>= 18)
    private static bool IsAtLeastAge(DateOnly dob, int minAge, DateOnly? today = null)
    {
        var t = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var age = t.Year - dob.Year;
        if (t < dob.AddYears(age)) age--;

        return age >= minAge;
    }
}
