using System.Security.Claims;
using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using CmmSalud.Api.Domain.Entities;
using CmmSalud.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/pharmacies")]
[Authorize]
public sealed class PharmaciesController : ControllerBase
{
    private readonly AppDbContext _db;
    public PharmaciesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        // (Opcional) Si quieres solo activas:
        // var items = await _db.Pharmacies.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);

        var items = await _db.Pharmacies.AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return Ok(new ApiResponse<object>(200, "OK", items));
    }

    // ✅ Admin crea: User(role=pharmacy) + Pharmacy linked (shared PK: Pharmacy.Id = User.Id)
    public sealed record CreatePharmacyAccountRequest(
        string Email,
        string Password,
        string Name,
        string LicenseNumber,
        string PharmacistName,
        string PharmacistLicense,
        string Address,
        string City,
        string State,
        string Phone,
        string? ZipCode,
        string? OperatingHours,
        string? Notes
    );

    [Authorize(Roles = "admin")]
    [HttpPost("create-account")]
    public async Task<IActionResult> CreateAccount([FromBody] CreatePharmacyAccountRequest req, CancellationToken ct)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new ApiResponse<object>(400, "Email es obligatorio."));

        if (string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new ApiResponse<object>(400, "Password es obligatorio."));

        var name = (req.Name ?? "").Trim();
        var license = (req.LicenseNumber ?? "").Trim();
        var pharmacistName = (req.PharmacistName ?? "").Trim();
        var pharmacistLicense = (req.PharmacistLicense ?? "").Trim();
        var address = (req.Address ?? "").Trim();
        var city = (req.City ?? "").Trim();
        var state = (req.State ?? "").Trim();
        var phone = (req.Phone ?? "").Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(license))
            return BadRequest(new ApiResponse<object>(400, "Name y LicenseNumber son obligatorios."));

        if (string.IsNullOrWhiteSpace(pharmacistName) || string.IsNullOrWhiteSpace(pharmacistLicense))
            return BadRequest(new ApiResponse<object>(400, "PharmacistName y PharmacistLicense son obligatorios."));

        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state))
            return BadRequest(new ApiResponse<object>(400, "Address, City y State son obligatorios."));

        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new ApiResponse<object>(400, "Phone es obligatorio."));

        // email único
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return BadRequest(new ApiResponse<object>(400, "Ya existe un usuario con ese email."));

        // licencia única (case-insensitive)
        var licLower = license.ToLowerInvariant();
        if (await _db.Pharmacies.AnyAsync(p => p.LicenseNumber.ToLower() == licLower, ct))
            return BadRequest(new ApiResponse<object>(400, "Ya existe una farmacia con ese LicenseNumber."));

        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 11),
            Role = UserRole.pharmacy,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // ✅ Shared PK: Pharmacy.Id = User.Id (importante para que UsersController y EF estén felices)
        var pharmacy = new Pharmacy
        {
            Id = userId,

            Name = name,
            LicenseNumber = license,
            PharmacistName = pharmacistName,
            PharmacistLicense = pharmacistLicense,

            Address = address,
            City = city,
            State = state,
            ZipCode = string.IsNullOrWhiteSpace(req.ZipCode) ? null : req.ZipCode.Trim(),

            Phone = phone,
            Email = email,

            OperatingHours = string.IsNullOrWhiteSpace(req.OperatingHours) ? "N/A" : req.OperatingHours.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? "" : req.Notes.Trim(),

            IsActive = true,
            IsVerified = false,
            VerifiedAt = null,
            VerifiedBy = null,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        _db.Pharmacies.Add(pharmacy);

        await _db.SaveChangesAsync(ct);

        return Ok(new ApiResponse<object>(200, "Cuenta de farmacia creada", new
        {
            user = new { id = user.Id, email = user.Email, role = user.Role.ToString(), isActive = user.IsActive },
            pharmacy = new { id = pharmacy.Id, pharmacy.Name, pharmacy.LicenseNumber, pharmacy.IsActive, pharmacy.IsVerified }
        }));
    }

    // ✅ DTO para update (NO usar entidad Pharmacy directo: evita overposting + problemas NOT NULL)
    public sealed record UpdatePharmacyRequest(
        string? Name,
        string? LicenseNumber,
        string? PharmacistName,
        string? PharmacistLicense,
        string? Address,
        string? City,
        string? State,
        string? ZipCode,
        string? Phone,
        string? Email,
        string? OperatingHours,
        string? Notes,
        bool? IsActive
    );

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePharmacyRequest req, CancellationToken ct)
    {
        var entity = await _db.Pharmacies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));

        // licencia única si viene
        if (!string.IsNullOrWhiteSpace(req.LicenseNumber))
        {
            var lic = req.LicenseNumber.Trim();
            var licLower = lic.ToLowerInvariant();

            var exists = await _db.Pharmacies.AsNoTracking()
                .AnyAsync(p => p.LicenseNumber.ToLower() == licLower && p.Id != id, ct);

            if (exists)
                return BadRequest(new ApiResponse<object>(400, "Ya existe una farmacia con ese número de licencia."));

            entity.LicenseNumber = lic;
        }

        if (!string.IsNullOrWhiteSpace(req.Name)) entity.Name = req.Name.Trim();
        if (!string.IsNullOrWhiteSpace(req.Address)) entity.Address = req.Address.Trim();
        if (!string.IsNullOrWhiteSpace(req.Phone)) entity.Phone = req.Phone.Trim();
        if (!string.IsNullOrWhiteSpace(req.Email)) entity.Email = req.Email.Trim();

        if (!string.IsNullOrWhiteSpace(req.City)) entity.City = req.City.Trim();
        if (!string.IsNullOrWhiteSpace(req.State)) entity.State = req.State.Trim();
        if (req.ZipCode is not null) entity.ZipCode = string.IsNullOrWhiteSpace(req.ZipCode) ? null : req.ZipCode.Trim();

        if (!string.IsNullOrWhiteSpace(req.PharmacistName)) entity.PharmacistName = req.PharmacistName.Trim();
        if (!string.IsNullOrWhiteSpace(req.PharmacistLicense)) entity.PharmacistLicense = req.PharmacistLicense.Trim();

        if (!string.IsNullOrWhiteSpace(req.OperatingHours)) entity.OperatingHours = req.OperatingHours.Trim();
        if (req.Notes is not null) entity.Notes = req.Notes; // permite vacío
        if (req.IsActive.HasValue) entity.IsActive = req.IsActive.Value;

        // ✅ defaults para evitar NULL/empty en campos NOT NULL
        entity.Name = string.IsNullOrWhiteSpace(entity.Name) ? "N/A" : entity.Name;
        entity.LicenseNumber = string.IsNullOrWhiteSpace(entity.LicenseNumber) ? "N/A" : entity.LicenseNumber;
        entity.PharmacistName = string.IsNullOrWhiteSpace(entity.PharmacistName) ? "N/A" : entity.PharmacistName;
        entity.PharmacistLicense = string.IsNullOrWhiteSpace(entity.PharmacistLicense) ? "N/A" : entity.PharmacistLicense;
        entity.Address = string.IsNullOrWhiteSpace(entity.Address) ? "N/A" : entity.Address;
        entity.City = string.IsNullOrWhiteSpace(entity.City) ? "N/A" : entity.City;
        entity.State = string.IsNullOrWhiteSpace(entity.State) ? "N/A" : entity.State;
        entity.Phone = string.IsNullOrWhiteSpace(entity.Phone) ? "N/A" : entity.Phone;
        entity.Email = string.IsNullOrWhiteSpace(entity.Email) ? "N/A" : entity.Email;
        entity.OperatingHours = string.IsNullOrWhiteSpace(entity.OperatingHours) ? "N/A" : entity.OperatingHours;
        entity.Notes ??= "";

        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;

            if (msg.Contains("Cannot insert the value NULL", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Faltan datos obligatorios para guardar la farmacia (City/State/etc)."));

            return StatusCode(500, new ApiResponse<object>(500, "Error interno del servidor.", null));
        }

        return Ok(new ApiResponse<object>(200, "Actualizada", entity));
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        // ⚠️ Mejor que hard delete: inactivar (evita usuarios huérfanos)
        var entity = await _db.Pharmacies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));

        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;

        // como usamos shared PK, el user tiene el mismo Id
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is not null)
        {
            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Inactivada"));
    }

    public sealed record ValidatePrescriptionRequest(Guid PrescriptionId);

    [HttpPost("validate-prescription")]
    public async Task<IActionResult> Validate([FromBody] ValidatePrescriptionRequest req, CancellationToken ct)
    {
        var exists = await _db.Prescriptions.AsNoTracking().AnyAsync(p => p.Id == req.PrescriptionId, ct);
        return Ok(new ApiResponse<object>(200, "OK", new { valid = exists }));
    }

    [Authorize(Roles = "admin")]
    [HttpPost("{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, CancellationToken ct)
    {
        var entity = await _db.Pharmacies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));

        entity.IsVerified = true;
        entity.VerifiedAt = DateTime.UtcNow;

        // ✅ si tienes claim de userId, lo guardamos en VerifiedBy (si no, lo deja null)
        var claimUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(claimUserId, out var adminId))
            entity.VerifiedBy = adminId;

        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Verificada", entity));
    }

    [HttpGet("{id:guid}/stats")]
    public async Task<IActionResult> Stats(Guid id, CancellationToken ct)
    {
        var dispCount = await _db.PrescriptionDispensations.CountAsync(d => d.PharmacyId == id, ct);
        return Ok(new ApiResponse<object>(200, "OK", new { pharmacyId = id, dispensations = dispCount }));
    }
}
