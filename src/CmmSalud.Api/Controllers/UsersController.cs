using System.Globalization;
using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using CmmSalud.Api.DTOs.Users;
using CmmSalud.Api.Domain.Enums;
using CmmSalud.Api.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Roles = "admin")]
public sealed class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<UsersController> _log;

    public UsersController(AppDbContext db, ILogger<UsersController> log)
    {
        _db = db;
        _log = log;
    }

    // ✅ POST api/v1/users  (CREAR USUARIO DESDE ADMIN)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        // email
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new ApiResponse<object>(400, "email es requerido"));

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();

        // password
        if (string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new ApiResponse<object>(400, "password es requerido"));

        // role
        var roleText = string.IsNullOrWhiteSpace(req.Role) ? "patient" : req.Role.Trim();
        if (!TryParseUserRole(roleText, out var parsedRole))
            return BadRequest(new ApiResponse<object>(400, $"Rol inválido: {req.Role}"));

        // unique email
        var emailTaken = await _db.Users.AsNoTracking().AnyAsync(u => u.Email == normalizedEmail, ct);
        if (emailTaken)
            return BadRequest(new ApiResponse<object>(400, "Ya existe un usuario con ese email."));

        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 11),
            Role = parsedRole,
            IsActive = req.IsActive ?? true,
            LastLogin = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        // -------------------------
        // Crear data relacionada segun rol
        // -------------------------
        if (parsedRole == UserRole.doctor)
        {
            if (req.DoctorData is null)
                return BadRequest(new ApiResponse<object>(400, "Para rol doctor se requiere doctorData"));

            var docId = req.DoctorData.DocumentId?.Trim();
            var lic = req.DoctorData.LicenseNumber?.Trim();

            if (string.IsNullOrWhiteSpace(docId))
                return BadRequest(new ApiResponse<object>(400, "doctorData.documentId es requerido"));
            if (string.IsNullOrWhiteSpace(lic))
                return BadRequest(new ApiResponse<object>(400, "doctorData.licenseNumber es requerido"));

            // unique documentId / licenseNumber
            var docIdTaken = await _db.Doctors.AsNoTracking().AnyAsync(d => d.DocumentId == docId, ct);
            if (docIdTaken)
                return BadRequest(new ApiResponse<object>(400, $"Ya existe un doctor con ese documentId: {docId}"));

            var licTaken = await _db.Doctors.AsNoTracking().AnyAsync(d => d.LicenseNumber == lic, ct);
            if (licTaken)
                return BadRequest(new ApiResponse<object>(400, $"Ya existe un doctor con esa licencia: {lic}"));

            // specialty: por ID primero, si no por nombre
            Guid specialtyId;
            if (req.DoctorData.SpecialtyId.HasValue && req.DoctorData.SpecialtyId.Value != Guid.Empty)
            {
                var exists = await _db.Specialties.AsNoTracking()
                    .AnyAsync(s => s.Id == req.DoctorData.SpecialtyId.Value, ct);

                if (!exists)
                    return BadRequest(new ApiResponse<object>(400, "specialtyId inválido: no existe esa especialidad."));

                specialtyId = req.DoctorData.SpecialtyId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(req.DoctorData.Specialty))
            {
                var specialtyName = req.DoctorData.Specialty.Trim();

                specialtyId = await _db.Specialties.AsNoTracking()
                    .Where(s => s.Name.ToLower() == specialtyName.ToLower())
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync(ct);

                if (specialtyId == Guid.Empty)
                    return BadRequest(new ApiResponse<object>(400, $"Especialidad no encontrada: {specialtyName}"));
            }
            else
            {
                return BadRequest(new ApiResponse<object>(400, "Para rol doctor se requiere doctorData.specialty o doctorData.specialtyId"));
            }

            user.Doctor = new Doctor
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DocumentId = docId!,
                LicenseNumber = lic!,
                FirstName = string.IsNullOrWhiteSpace(req.DoctorData.FirstName) ? "N/A" : req.DoctorData.FirstName.Trim(),
                LastName = string.IsNullOrWhiteSpace(req.DoctorData.LastName) ? "N/A" : req.DoctorData.LastName.Trim(),
                Phone = string.IsNullOrWhiteSpace(req.DoctorData.Phone) ? "N/A" : req.DoctorData.Phone.Trim(),
                ConsultationFee = req.DoctorData.ConsultationFee ?? 0m,
                AcceptsInsurance = false,
                SpecialtyId = specialtyId,
                CreatedAt = now,
                UpdatedAt = now
            };
        }
        else if (parsedRole == UserRole.patient)
        {
            if (req.PatientData is null)
                return BadRequest(new ApiResponse<object>(400, "Para rol patient se requiere patientData"));

            var documentId = req.PatientData.DocumentId?.Trim();
            if (string.IsNullOrWhiteSpace(documentId))
                return BadRequest(new ApiResponse<object>(400, "patientData.documentId es requerido"));

            var docExists = await _db.Patients.AsNoTracking().AnyAsync(p => p.DocumentId == documentId, ct);
            if (docExists)
                return BadRequest(new ApiResponse<object>(400, "Ya existe un paciente con ese documento."));

            if (string.IsNullOrWhiteSpace(req.PatientData.DateOfBirth) ||
                !TryParseDateOnly(req.PatientData.DateOfBirth.Trim(), out var dob))
            {
                return BadRequest(new ApiResponse<object>(400, "patientData.dateOfBirth inválida. Usa formato yyyy-MM-dd"));
            }

            // ✅ Validación: mayor o igual a 18
            if (!IsAtLeastAge(dob, 18))
                return BadRequest(new ApiResponse<object>(400, "El paciente debe ser mayor de 18 años."));

            user.Patient = new Patient
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DocumentId = documentId!,
                FirstName = string.IsNullOrWhiteSpace(req.PatientData.FirstName) ? "N/A" : req.PatientData.FirstName.Trim(),
                LastName = string.IsNullOrWhiteSpace(req.PatientData.LastName) ? "N/A" : req.PatientData.LastName.Trim(),
                Phone = string.IsNullOrWhiteSpace(req.PatientData.Phone) ? "N/A" : req.PatientData.Phone.Trim(),
                Address = string.IsNullOrWhiteSpace(req.PatientData.Address) ? "N/A" : req.PatientData.Address.Trim(),
                DateOfBirth = dob,
                CreatedAt = now,
                UpdatedAt = now
            };
        }
        else if (parsedRole == UserRole.pharmacy)
        {
            if (req.PharmacyData is null)
                return BadRequest(new ApiResponse<object>(400, "Para rol pharmacy se requiere pharmacyData"));

            var name = req.PharmacyData.Name?.Trim();
            var lic = req.PharmacyData.LicenseNumber?.Trim();

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new ApiResponse<object>(400, "pharmacyData.name es requerido"));
            if (string.IsNullOrWhiteSpace(lic))
                return BadRequest(new ApiResponse<object>(400, "pharmacyData.licenseNumber es requerido"));

            var licLower = lic.ToLowerInvariant();
            var licExists = await _db.Pharmacies.AsNoTracking()
                .AnyAsync(p => p.LicenseNumber.ToLower() == licLower, ct);

            if (licExists)
                return BadRequest(new ApiResponse<object>(400, "Ya existe una farmacia con ese número de licencia."));

            // ⚠️ OJO: tu modelBuilder marca City/State/Address/Phone/Email como required
            // (NOT NULL) -> aquí metemos defaults si no vienen, para que no explote.
            var pharmEmail = (req.PharmacyData.Email ?? user.Email)?.Trim();
            if (string.IsNullOrWhiteSpace(pharmEmail)) pharmEmail = user.Email;

            user.Pharmacy = new Pharmacy
            {
                Id = user.Id, // ✅ PK compartida Users.Id == Pharmacies.Id
                Name = name!,
                LicenseNumber = lic!,
                Phone = string.IsNullOrWhiteSpace(req.PharmacyData.Phone) ? "N/A" : req.PharmacyData.Phone.Trim(),
                Address = string.IsNullOrWhiteSpace(req.PharmacyData.Address) ? "N/A" : req.PharmacyData.Address.Trim(),
                City = string.IsNullOrWhiteSpace(req.PharmacyData.City) ? "N/A" : req.PharmacyData.City.Trim(),
                State = string.IsNullOrWhiteSpace(req.PharmacyData.State) ? "N/A" : req.PharmacyData.State.Trim(),
                ZipCode = string.IsNullOrWhiteSpace(req.PharmacyData.ZipCode) ? "00000" : req.PharmacyData.ZipCode.Trim(),
                Email = pharmEmail!,
                PharmacistName = string.IsNullOrWhiteSpace(req.PharmacyData.PharmacistName) ? "N/A" : req.PharmacyData.PharmacistName.Trim(),
                PharmacistLicense = string.IsNullOrWhiteSpace(req.PharmacyData.PharmacistLicense) ? "N/A" : req.PharmacyData.PharmacistLicense.Trim(),
                OperatingHours = string.IsNullOrWhiteSpace(req.PharmacyData.OperatingHours) ? "N/A" : req.PharmacyData.OperatingHours.Trim(),
                Notes = req.PharmacyData.Notes ?? "",
                IsActive = true,
                IsVerified = false,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx)
        {
            _log.LogError(ex, "🔥 DbUpdateException creating user. SQL={SqlNumber} Msg={Msg}", sqlEx.Number, sqlEx.Message);

            if (sqlEx.Number == 515)
                return BadRequest(new ApiResponse<object>(400, "Faltan datos obligatorios para guardar."));

            if (sqlEx.Number == 547)
                return BadRequest(new ApiResponse<object>(400, "Datos inválidos: referencia no existe (ej: specialtyId)."));

            if (sqlEx.Number is 2627 or 2601)
                return BadRequest(new ApiResponse<object>(400, "Ya existe un registro con un valor único repetido (ej: email/documentId/licenseNumber)."));

            if (sqlEx.Number == 2628)
                return BadRequest(new ApiResponse<object>(400, "Algún campo tiene demasiados caracteres (se está truncando en la DB)."));

            return StatusCode(500, new ApiResponse<object>(500, "Error interno del servidor.", null));
        }
        catch (DbUpdateException ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            _log.LogError(ex, "🔥 DbUpdateException creating user. Msg={Msg}", msg);

            if (msg.Contains("Cannot insert the value NULL", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Faltan datos obligatorios para guardar."));

            if (msg.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("conflicted with the FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Datos inválidos: referencia no existe (ej: specialtyId)."));

            if (msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Ya existe un registro con un valor único repetido (ej: email/documentId/licenseNumber)."));

            if (msg.Contains("String or binary data would be truncated", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Algún campo tiene demasiados caracteres (se está truncando en la DB)."));

            return StatusCode(500, new ApiResponse<object>(500, "Error interno del servidor.", null));
        }

        // devolver creado completito
        var created = await _db.Users.AsNoTracking()
            .Include(u => u.Patient)
            .Include(u => u.Doctor)!.ThenInclude(d => d.Specialty)
            .Include(u => u.Pharmacy)
            .Where(u => u.Id == user.Id)
            .Select(u => new
            {
                id = u.Id,
                email = u.Email,
                role = u.Role.ToString(),
                isActive = u.IsActive,
                createdAt = u.CreatedAt,
                updatedAt = u.UpdatedAt,
                lastLogin = u.LastLogin,

                patient = u.Patient == null ? null : new
                {
                    id = u.Patient.Id,
                    firstName = u.Patient.FirstName,
                    lastName = u.Patient.LastName,
                    documentId = u.Patient.DocumentId,
                    phone = u.Patient.Phone,
                    address = u.Patient.Address,
                    dateOfBirth = u.Patient.DateOfBirth.ToString("yyyy-MM-dd"),
                },

                doctor = u.Doctor == null ? null : new
                {
                    id = u.Doctor.Id,
                    firstName = u.Doctor.FirstName,
                    lastName = u.Doctor.LastName,
                    documentId = u.Doctor.DocumentId,
                    licenseNumber = u.Doctor.LicenseNumber,
                    phone = u.Doctor.Phone,
                    consultationFee = u.Doctor.ConsultationFee,
                    specialtyId = u.Doctor.SpecialtyId,
                    specialty = u.Doctor.Specialty == null ? null : new
                    {
                        id = u.Doctor.Specialty.Id,
                        name = u.Doctor.Specialty.Name
                    }
                },

                pharmacy = u.Pharmacy == null ? null : new
                {
                    id = u.Pharmacy.Id,
                    name = u.Pharmacy.Name,
                    licenseNumber = u.Pharmacy.LicenseNumber,
                    pharmacistName = u.Pharmacy.PharmacistName,
                    pharmacistLicense = u.Pharmacy.PharmacistLicense,
                    phone = u.Pharmacy.Phone,
                    email = u.Pharmacy.Email,
                    address = u.Pharmacy.Address,
                    city = u.Pharmacy.City,
                    state = u.Pharmacy.State,
                    zipCode = u.Pharmacy.ZipCode,
                    notes = u.Pharmacy.Notes,
                    operatingHours = u.Pharmacy.OperatingHours,
                    isActive = u.Pharmacy.IsActive,
                    isVerified = u.Pharmacy.IsVerified,
                    verifiedAt = u.Pharmacy.VerifiedAt
                }
            })
            .FirstAsync(ct);

        return StatusCode(201, new ApiResponse<object>(201, "Creado", created));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery(Name = "pageSize")] int? pageSize = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);

        limit = pageSize ?? limit;
        limit = Math.Clamp(limit, 1, 200);

        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.Patient)
            .Include(u => u.Doctor)!.ThenInclude(d => d.Specialty)
            .Include(u => u.Pharmacy)
            .OrderByDescending(u => u.CreatedAt)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(role))
        {
            if (!TryParseUserRole(role, out var parsedRole))
                return BadRequest(new ApiResponse<object>(400, $"Rol inválido: {role}"));

            query = query.Where(u => u.Role == parsedRole);
        }

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)limit);

        var items = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(u => new
            {
                id = u.Id,
                email = u.Email,
                role = u.Role.ToString(),
                isActive = u.IsActive,
                createdAt = u.CreatedAt,
                updatedAt = u.UpdatedAt,
                lastLogin = u.LastLogin,

                patient = u.Patient == null ? null : new
                {
                    id = u.Patient.Id,
                    firstName = u.Patient.FirstName,
                    lastName = u.Patient.LastName,
                    documentId = u.Patient.DocumentId,
                    phone = u.Patient.Phone,
                    address = u.Patient.Address,
                    dateOfBirth = u.Patient.DateOfBirth.ToString("yyyy-MM-dd"),
                },

                doctor = u.Doctor == null ? null : new
                {
                    id = u.Doctor.Id,
                    firstName = u.Doctor.FirstName,
                    lastName = u.Doctor.LastName,
                    documentId = u.Doctor.DocumentId,
                    licenseNumber = u.Doctor.LicenseNumber,
                    phone = u.Doctor.Phone,
                    consultationFee = u.Doctor.ConsultationFee,
                    specialtyId = u.Doctor.SpecialtyId,
                    specialty = u.Doctor.Specialty == null ? null : new
                    {
                        id = u.Doctor.Specialty.Id,
                        name = u.Doctor.Specialty.Name
                    }
                },

                pharmacy = u.Pharmacy == null ? null : new
                {
                    id = u.Pharmacy.Id,
                    name = u.Pharmacy.Name,
                    licenseNumber = u.Pharmacy.LicenseNumber,
                    pharmacistName = u.Pharmacy.PharmacistName,
                    pharmacistLicense = u.Pharmacy.PharmacistLicense,
                    phone = u.Pharmacy.Phone,
                    email = u.Pharmacy.Email,
                    address = u.Pharmacy.Address,
                    city = u.Pharmacy.City,
                    state = u.Pharmacy.State,
                    zipCode = u.Pharmacy.ZipCode,
                    notes = u.Pharmacy.Notes,
                    operatingHours = u.Pharmacy.OperatingHours,
                    isActive = u.Pharmacy.IsActive,
                    isVerified = u.Pharmacy.IsVerified,
                    verifiedAt = u.Pharmacy.VerifiedAt
                }
            })
            .ToListAsync(ct);

        var data = new
        {
            page,
            pageSize = limit,
            totalPages,
            totalCount,
            items
        };

        return Ok(new ApiResponse<object>(200, "OK", data));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Patient)
            .Include(u => u.Doctor)!.ThenInclude(d => d.Specialty)
            .Include(u => u.Pharmacy)
            .Where(u => u.Id == id)
            .Select(u => new
            {
                id = u.Id,
                email = u.Email,
                role = u.Role.ToString(),
                isActive = u.IsActive,
                createdAt = u.CreatedAt,
                updatedAt = u.UpdatedAt,
                lastLogin = u.LastLogin,

                patient = u.Patient == null ? null : new
                {
                    id = u.Patient.Id,
                    firstName = u.Patient.FirstName,
                    lastName = u.Patient.LastName,
                    documentId = u.Patient.DocumentId,
                    phone = u.Patient.Phone,
                    address = u.Patient.Address,
                    dateOfBirth = u.Patient.DateOfBirth.ToString("yyyy-MM-dd"),
                },

                doctor = u.Doctor == null ? null : new
                {
                    id = u.Doctor.Id,
                    firstName = u.Doctor.FirstName,
                    lastName = u.Doctor.LastName,
                    documentId = u.Doctor.DocumentId,
                    licenseNumber = u.Doctor.LicenseNumber,
                    phone = u.Doctor.Phone,
                    consultationFee = u.Doctor.ConsultationFee,
                    specialtyId = u.Doctor.SpecialtyId,
                    specialty = u.Doctor.Specialty == null ? null : new
                    {
                        id = u.Doctor.Specialty.Id,
                        name = u.Doctor.Specialty.Name
                    }
                },

                pharmacy = u.Pharmacy == null ? null : new
                {
                    id = u.Pharmacy.Id,
                    name = u.Pharmacy.Name,
                    licenseNumber = u.Pharmacy.LicenseNumber,
                    pharmacistName = u.Pharmacy.PharmacistName,
                    pharmacistLicense = u.Pharmacy.PharmacistLicense,
                    phone = u.Pharmacy.Phone,
                    email = u.Pharmacy.Email,
                    address = u.Pharmacy.Address,
                    city = u.Pharmacy.City,
                    state = u.Pharmacy.State,
                    zipCode = u.Pharmacy.ZipCode,
                    notes = u.Pharmacy.Notes,
                    operatingHours = u.Pharmacy.OperatingHours,
                    isActive = u.Pharmacy.IsActive,
                    isVerified = u.Pharmacy.IsVerified,
                    verifiedAt = u.Pharmacy.VerifiedAt
                }
            })
            .FirstOrDefaultAsync(ct);

        if (user is null) return NotFound(new ApiResponse<object>(404, "No encontrado"));
        return Ok(new ApiResponse<object>(200, "OK", user));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AdminUserPatchRequest req, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Patient)
            .Include(u => u.Doctor)
            .Include(u => u.Pharmacy)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
            return NotFound(new ApiResponse<object>(404, "No encontrado"));

        // Email (si lo cambian, valida UNIQUE antes)
        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var newEmail = req.Email.Trim();

            var emailTaken = await _db.Users.AsNoTracking()
                .AnyAsync(u => u.Email == newEmail && u.Id != user.Id, ct);

            if (emailTaken)
                return BadRequest(new ApiResponse<object>(400, "Ya existe un usuario con ese email."));

            user.Email = newEmail;
        }

        if (req.IsActive.HasValue)
            user.IsActive = req.IsActive.Value;

        // Determina rol objetivo (si no mandan role, se queda con el actual)
        var targetRole = user.Role;
        if (!string.IsNullOrWhiteSpace(req.Role))
        {
            if (!TryParseUserRole(req.Role, out var parsedRole))
                return BadRequest(new ApiResponse<object>(400, $"Rol inválido: {req.Role}"));

            targetRole = parsedRole;
            user.Role = parsedRole;
        }

        if (targetRole == UserRole.doctor)
        {
            if (req.DoctorData is null && user.Doctor is null)
                return BadRequest(new ApiResponse<object>(400, "Para rol doctor se requiere doctorData"));

            if (req.DoctorData is not null)
            {
                var isNewDoctor = user.Doctor is null;

                var docId = req.DoctorData.DocumentId?.Trim();
                var lic = req.DoctorData.LicenseNumber?.Trim();

                if (isNewDoctor)
                {
                    if (string.IsNullOrWhiteSpace(docId))
                        return BadRequest(new ApiResponse<object>(400, "Para rol doctor se requiere doctorData.documentId"));

                    if (string.IsNullOrWhiteSpace(lic))
                        return BadRequest(new ApiResponse<object>(400, "Para rol doctor se requiere doctorData.licenseNumber"));
                }

                if (!string.IsNullOrWhiteSpace(docId))
                {
                    var docIdTaken = await _db.Doctors.AsNoTracking()
                        .AnyAsync(d => d.DocumentId == docId && d.UserId != user.Id, ct);

                    if (docIdTaken)
                        return BadRequest(new ApiResponse<object>(400, $"Ya existe un doctor con ese documentId: {docId}"));
                }

                if (!string.IsNullOrWhiteSpace(lic))
                {
                    var licTaken = await _db.Doctors.AsNoTracking()
                        .AnyAsync(d => d.LicenseNumber == lic && d.UserId != user.Id, ct);

                    if (licTaken)
                        return BadRequest(new ApiResponse<object>(400, $"Ya existe un doctor con esa licencia: {lic}"));
                }

                if (user.Doctor is null)
                {
                    user.Doctor = new Doctor
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        AcceptsInsurance = false
                    };

                    _db.Entry(user.Doctor).State = EntityState.Added;
                }

                if (!string.IsNullOrWhiteSpace(req.DoctorData.FirstName)) user.Doctor.FirstName = req.DoctorData.FirstName.Trim();
                if (!string.IsNullOrWhiteSpace(req.DoctorData.LastName)) user.Doctor.LastName = req.DoctorData.LastName.Trim();
                if (!string.IsNullOrWhiteSpace(docId)) user.Doctor.DocumentId = docId!;
                if (!string.IsNullOrWhiteSpace(req.DoctorData.Phone)) user.Doctor.Phone = req.DoctorData.Phone.Trim();
                if (!string.IsNullOrWhiteSpace(lic)) user.Doctor.LicenseNumber = lic!;
                if (req.DoctorData.ConsultationFee.HasValue) user.Doctor.ConsultationFee = req.DoctorData.ConsultationFee.Value;

                Guid? specialtyIdFromReq = req.DoctorData.SpecialtyId;
                if (specialtyIdFromReq.HasValue && specialtyIdFromReq.Value != Guid.Empty)
                {
                    var exists = await _db.Specialties.AsNoTracking()
                        .AnyAsync(s => s.Id == specialtyIdFromReq.Value, ct);

                    if (!exists)
                        return BadRequest(new ApiResponse<object>(400, "specialtyId inválido: no existe esa especialidad."));

                    user.Doctor.SpecialtyId = specialtyIdFromReq.Value;
                }
                else if (!string.IsNullOrWhiteSpace(req.DoctorData.Specialty))
                {
                    var specialtyName = req.DoctorData.Specialty.Trim();

                    var specialtyId = await _db.Specialties
                        .AsNoTracking()
                        .Where(s => s.Name.ToLower() == specialtyName.ToLower())
                        .Select(s => s.Id)
                        .FirstOrDefaultAsync(ct);

                    if (specialtyId == Guid.Empty)
                        return BadRequest(new ApiResponse<object>(400, $"Especialidad no encontrada: {specialtyName}"));

                    user.Doctor.SpecialtyId = specialtyId;
                }
                else
                {
                    return BadRequest(new ApiResponse<object>(400, "Para rol doctor se requiere doctorData.specialty o doctorData.specialtyId"));
                }

                if (string.IsNullOrWhiteSpace(user.Doctor.DocumentId))
                    return BadRequest(new ApiResponse<object>(400, "doctorData.documentId es requerido."));

                if (string.IsNullOrWhiteSpace(user.Doctor.LicenseNumber))
                    return BadRequest(new ApiResponse<object>(400, "doctorData.licenseNumber es requerido."));

                user.Doctor.FirstName = string.IsNullOrWhiteSpace(user.Doctor.FirstName) ? "N/A" : user.Doctor.FirstName;
                user.Doctor.LastName = string.IsNullOrWhiteSpace(user.Doctor.LastName) ? "N/A" : user.Doctor.LastName;
                user.Doctor.Phone = string.IsNullOrWhiteSpace(user.Doctor.Phone) ? "N/A" : user.Doctor.Phone;

                user.Doctor.UpdatedAt = DateTime.UtcNow;

                if (!isNewDoctor)
                {
                    var doctorRowExists = await _db.Doctors.AsNoTracking().AnyAsync(d => d.Id == user.Doctor.Id, ct);
                    if (!doctorRowExists)
                        _db.Entry(user.Doctor).State = EntityState.Added;
                }
            }
        }
        else if (targetRole == UserRole.patient)
        {
            if (req.PatientData is null && user.Patient is null)
                return BadRequest(new ApiResponse<object>(400, "Para rol patient se requiere patientData"));

            if (req.PatientData is not null)
            {
                var isNewPatient = user.Patient is null;

                user.Patient ??= new Patient
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                if (!string.IsNullOrWhiteSpace(req.PatientData.FirstName)) user.Patient.FirstName = req.PatientData.FirstName.Trim();
                if (!string.IsNullOrWhiteSpace(req.PatientData.LastName)) user.Patient.LastName = req.PatientData.LastName.Trim();
                if (!string.IsNullOrWhiteSpace(req.PatientData.Phone)) user.Patient.Phone = req.PatientData.Phone.Trim();
                if (!string.IsNullOrWhiteSpace(req.PatientData.Address)) user.Patient.Address = req.PatientData.Address.Trim();
                if (!string.IsNullOrWhiteSpace(req.PatientData.DocumentId)) user.Patient.DocumentId = req.PatientData.DocumentId.Trim();

                // ✅ Para patient nuevo, DOB es obligatorio (porque no es nullable)
                if (isNewPatient && string.IsNullOrWhiteSpace(req.PatientData.DateOfBirth))
                    return BadRequest(new ApiResponse<object>(400, "Para rol patient se requiere patientData.dateOfBirth (yyyy-MM-dd)."));

                if (!string.IsNullOrWhiteSpace(req.PatientData.DateOfBirth))
                {
                    if (!TryParseDateOnly(req.PatientData.DateOfBirth.Trim(), out var dob))
                        return BadRequest(new ApiResponse<object>(400, "dateOfBirth inválida. Usa formato yyyy-MM-dd"));

                    // ✅ Validación: mayor o igual a 18
                    if (!IsAtLeastAge(dob, 18))
                        return BadRequest(new ApiResponse<object>(400, "El paciente debe ser mayor de 18 años."));

                    user.Patient.DateOfBirth = dob;
                }

                if (isNewPatient && string.IsNullOrWhiteSpace(user.Patient.DocumentId))
                    return BadRequest(new ApiResponse<object>(400, "Para rol patient se requiere patientData.documentId"));

                user.Patient.FirstName = string.IsNullOrWhiteSpace(user.Patient.FirstName) ? "N/A" : user.Patient.FirstName;
                user.Patient.LastName = string.IsNullOrWhiteSpace(user.Patient.LastName) ? "N/A" : user.Patient.LastName;
                user.Patient.Phone = string.IsNullOrWhiteSpace(user.Patient.Phone) ? "N/A" : user.Patient.Phone;
                user.Patient.Address = string.IsNullOrWhiteSpace(user.Patient.Address) ? "N/A" : user.Patient.Address;

                user.Patient.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (targetRole == UserRole.pharmacy)
        {
            if (req.PharmacyData is null && user.Pharmacy is null)
                return BadRequest(new ApiResponse<object>(400, "Para rol farmacia se requiere pharmacyData"));

            if (req.PharmacyData is not null)
            {
                var isNewPharmacy = user.Pharmacy is null;

                var name = req.PharmacyData.Name?.Trim();
                var lic = req.PharmacyData.LicenseNumber?.Trim();

                if (isNewPharmacy)
                {
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(lic))
                        return BadRequest(new ApiResponse<object>(400, "Para rol farmacia se requiere pharmacyData.name y pharmacyData.licenseNumber"));
                }

                if (!string.IsNullOrWhiteSpace(lic))
                {
                    var licLower = lic.ToLowerInvariant();

                    var exists = await _db.Pharmacies
                        .AsNoTracking()
                        .AnyAsync(p => p.LicenseNumber.ToLower() == licLower
                                      && (user.Pharmacy == null ? true : p.Id != user.Pharmacy.Id), ct);

                    if (exists)
                        return BadRequest(new ApiResponse<object>(400, "Ya existe una farmacia con ese número de licencia."));
                }

                user.Pharmacy ??= new Pharmacy
                {
                    Id = user.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsVerified = false,
                    IsActive = true
                };

                if (!string.IsNullOrWhiteSpace(name)) user.Pharmacy.Name = name!;
                if (!string.IsNullOrWhiteSpace(lic)) user.Pharmacy.LicenseNumber = lic!;

                if (!string.IsNullOrWhiteSpace(req.PharmacyData.Phone)) user.Pharmacy.Phone = req.PharmacyData.Phone.Trim();
                if (!string.IsNullOrWhiteSpace(req.PharmacyData.Address)) user.Pharmacy.Address = req.PharmacyData.Address.Trim();

                user.Pharmacy.Email = (req.PharmacyData.Email ?? user.Email)?.Trim();

                if (!string.IsNullOrWhiteSpace(req.PharmacyData.City)) user.Pharmacy.City = req.PharmacyData.City.Trim();
                if (!string.IsNullOrWhiteSpace(req.PharmacyData.State)) user.Pharmacy.State = req.PharmacyData.State.Trim();
                if (!string.IsNullOrWhiteSpace(req.PharmacyData.ZipCode)) user.Pharmacy.ZipCode = req.PharmacyData.ZipCode.Trim();

                if (!string.IsNullOrWhiteSpace(req.PharmacyData.Notes)) user.Pharmacy.Notes = req.PharmacyData.Notes.Trim();
                if (!string.IsNullOrWhiteSpace(req.PharmacyData.OperatingHours)) user.Pharmacy.OperatingHours = req.PharmacyData.OperatingHours.Trim();
                if (!string.IsNullOrWhiteSpace(req.PharmacyData.PharmacistName)) user.Pharmacy.PharmacistName = req.PharmacyData.PharmacistName.Trim();
                if (!string.IsNullOrWhiteSpace(req.PharmacyData.PharmacistLicense)) user.Pharmacy.PharmacistLicense = req.PharmacyData.PharmacistLicense.Trim();

                user.Pharmacy.Name = string.IsNullOrWhiteSpace(user.Pharmacy.Name) ? "N/A" : user.Pharmacy.Name;

                if (isNewPharmacy && string.IsNullOrWhiteSpace(user.Pharmacy.LicenseNumber))
                    return BadRequest(new ApiResponse<object>(400, "pharmacyData.licenseNumber es requerido."));

                user.Pharmacy.Phone = string.IsNullOrWhiteSpace(user.Pharmacy.Phone) ? "N/A" : user.Pharmacy.Phone;
                user.Pharmacy.Address = string.IsNullOrWhiteSpace(user.Pharmacy.Address) ? "N/A" : user.Pharmacy.Address;
                user.Pharmacy.City = string.IsNullOrWhiteSpace(user.Pharmacy.City) ? "N/A" : user.Pharmacy.City;
                user.Pharmacy.State = string.IsNullOrWhiteSpace(user.Pharmacy.State) ? "N/A" : user.Pharmacy.State;
                user.Pharmacy.ZipCode = string.IsNullOrWhiteSpace(user.Pharmacy.ZipCode) ? "00000" : user.Pharmacy.ZipCode;
                user.Pharmacy.OperatingHours = string.IsNullOrWhiteSpace(user.Pharmacy.OperatingHours) ? "N/A" : user.Pharmacy.OperatingHours;
                user.Pharmacy.PharmacistName = string.IsNullOrWhiteSpace(user.Pharmacy.PharmacistName) ? "N/A" : user.Pharmacy.PharmacistName;
                user.Pharmacy.PharmacistLicense = string.IsNullOrWhiteSpace(user.Pharmacy.PharmacistLicense) ? "N/A" : user.Pharmacy.PharmacistLicense;
                user.Pharmacy.Notes = user.Pharmacy.Notes ?? "";

                user.Pharmacy.UpdatedAt = DateTime.UtcNow;
            }
        }

        user.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _log.LogError(ex, "⚠️ Concurrency error updating user {UserId}", user.Id);
            return StatusCode(409, new ApiResponse<object>(409, "El registro fue modificado por otro proceso. Reintenta."));
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx)
        {
            _log.LogError(ex, "🔥 DbUpdateException updating user {UserId}. SQL={SqlNumber} Msg={Msg}",
                user.Id, sqlEx.Number, sqlEx.Message);

            if (sqlEx.Number == 515)
                return BadRequest(new ApiResponse<object>(400, "Faltan datos obligatorios para guardar."));

            if (sqlEx.Number == 547)
                return BadRequest(new ApiResponse<object>(400, "Datos inválidos: referencia no existe (ej: specialtyId)."));

            if (sqlEx.Number is 2627 or 2601)
                return BadRequest(new ApiResponse<object>(400, "Ya existe un registro con un valor único repetido (ej: email/documentId/licenseNumber)."));

            if (sqlEx.Number == 2628)
                return BadRequest(new ApiResponse<object>(400, "Algún campo tiene demasiados caracteres (se está truncando en la DB)."));

            return StatusCode(500, new ApiResponse<object>(500, "Error interno del servidor.", null));
        }
        catch (DbUpdateException ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            _log.LogError(ex, "🔥 DbUpdateException updating user {UserId}. Msg={Msg}", user.Id, msg);

            if (msg.Contains("Cannot insert the value NULL", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Faltan datos obligatorios para guardar."));

            if (msg.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("conflicted with the FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Datos inválidos: referencia no existe (ej: specialtyId)."));

            if (msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Ya existe un registro con un valor único repetido (ej: email/documentId/licenseNumber)."));

            if (msg.Contains("String or binary data would be truncated", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object>(400, "Algún campo tiene demasiados caracteres (se está truncando en la DB)."));

            return StatusCode(500, new ApiResponse<object>(500, "Error interno del servidor.", null));
        }

        return Ok(new ApiResponse<object>(200, "Actualizado"));
    }

    private static bool TryParseDateOnly(string value, out DateOnly date)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseUserRole(string role, out UserRole parsed)
        => Enum.TryParse(role, ignoreCase: true, out parsed);

    // ✅ Helper edad mínima (>= 18)
    private static bool IsAtLeastAge(DateOnly dob, int minAge, DateOnly? today = null)
    {
        var t = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var age = t.Year - dob.Year;
        if (t < dob.AddYears(age)) age--;

        return age >= minAge;
    }
}
