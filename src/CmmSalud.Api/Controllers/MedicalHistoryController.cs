using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using CmmSalud.Api.DTOs.MedicalHistory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/medical-history")]
[Authorize(Roles = "doctor")]
public sealed class MedicalHistoryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<MedicalHistoryController> _log;

    public MedicalHistoryController(AppDbContext db, ILogger<MedicalHistoryController> log)
    {
        _db = db;
        _log = log;
    }

    // GET api/v1/medical-history?patientId=&documentId=&search=&page=&pageSize=
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? documentId = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // ✅ Identificar doctor actual (por UserId del JWT)
        var userId = GetUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new ApiResponse<object>(401, "No autenticado"));

        var doctorId = await _db.Doctors
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .Select(d => d.Id)
            .FirstOrDefaultAsync(ct);

        if (doctorId == Guid.Empty)
            return BadRequest(new ApiResponse<object>(400, "No se encontró el doctor asociado al usuario."));

        // ✅ Resolver patientId por documentId si lo mandan
        if (!string.IsNullOrWhiteSpace(documentId))
        {
            var doc = documentId.Trim();

            var pid = await _db.Patients.AsNoTracking()
                .Where(p => p.DocumentId == doc)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(ct);

            if (pid == Guid.Empty)
                return Ok(new ApiResponse<object>(200, "OK", new
                {
                    page,
                    pageSize,
                    totalPages = 0,
                    totalCount = 0,
                    items = Array.Empty<object>()
                }));

            patientId = pid;
        }

        // ✅ Base query con Patient
        var q = _db.MedicalHistories
            .AsNoTracking()
            .Include(m => m.Patient)
            .AsQueryable();

        // ✅ Si filtramos por patientId
        if (patientId.HasValue && patientId.Value != Guid.Empty)
        {
            q = q.Where(m => m.PatientId == patientId.Value);
        }

        // ✅ Search simple
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();

            q = q.Where(m =>
                m.Condition.ToLower().Contains(s) ||
                (m.Diagnosis != null && m.Diagnosis.ToLower().Contains(s)) ||
                (m.Treatment != null && m.Treatment.ToLower().Contains(s)) ||
                (m.Patient != null && (
                    m.Patient.FirstName.ToLower().Contains(s) ||
                    m.Patient.LastName.ToLower().Contains(s) ||
                    m.Patient.DocumentId.ToLower().Contains(s)
                ))
            );
        }

        // ✅ Restricción: solo pacientes con cita con este doctor (Appointments)
        // (Si NO quieres esto, comenta este bloque entero)
        q = q.Where(m => _db.Appointments.AsNoTracking()
            .Any(a => a.DoctorId == doctorId && a.PatientId == m.PatientId));

        var totalCount = await q.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await q
            .OrderByDescending(m => m.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                id = m.Id,
                patientId = m.PatientId,
                patient = m.Patient == null ? null : new
                {
                    id = m.Patient.Id,
                    firstName = m.Patient.FirstName,
                    lastName = m.Patient.LastName,
                    documentId = m.Patient.DocumentId,
                    dateOfBirth = m.Patient.DateOfBirth.ToString("yyyy-MM-dd"),
                    phone = m.Patient.Phone
                },
                condition = m.Condition,
                diagnosis = m.Diagnosis,
                treatment = m.Treatment,
                notes = m.Notes,
                createdAt = m.CreatedAt,
                updatedAt = m.UpdatedAt
            })
            .ToListAsync(ct);

        return Ok(new ApiResponse<object>(200, "OK", new
        {
            page,
            pageSize,
            totalPages,
            totalCount,
            items
        }));
    }

    // GET api/v1/medical-history/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var item = await _db.MedicalHistories
            .AsNoTracking()
            .Include(m => m.Patient)
            .Where(m => m.Id == id)
            .Select(m => new
            {
                id = m.Id,
                patientId = m.PatientId,
                patient = m.Patient == null ? null : new
                {
                    id = m.Patient.Id,
                    firstName = m.Patient.FirstName,
                    lastName = m.Patient.LastName,
                    documentId = m.Patient.DocumentId,
                    dateOfBirth = m.Patient.DateOfBirth.ToString("yyyy-MM-dd"),
                    phone = m.Patient.Phone
                },
                condition = m.Condition,
                diagnosis = m.Diagnosis,
                treatment = m.Treatment,
                notes = m.Notes,
                createdAt = m.CreatedAt,
                updatedAt = m.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (item is null) return NotFound(new ApiResponse<object>(404, "No encontrado"));
        return Ok(new ApiResponse<object>(200, "OK", item));
    }

    // POST api/v1/medical-history
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MedicalHistoryCreateDto dto, CancellationToken ct)
    {
        if (dto.PatientId == Guid.Empty)
            return BadRequest(new ApiResponse<object>(400, "patientId es requerido"));

        if (string.IsNullOrWhiteSpace(dto.Condition))
            return BadRequest(new ApiResponse<object>(400, "condition es requerido"));

        var patientExists = await _db.Patients.AsNoTracking().AnyAsync(p => p.Id == dto.PatientId, ct);
        if (!patientExists)
            return BadRequest(new ApiResponse<object>(400, "patientId inválido: no existe ese paciente"));

        var entity = new CmmSalud.Api.Domain.Entities.MedicalHistory
        {
            Id = Guid.NewGuid(),
            PatientId = dto.PatientId,
            Condition = dto.Condition.Trim(),
            Diagnosis = string.IsNullOrWhiteSpace(dto.Diagnosis) ? null : dto.Diagnosis.Trim(),
            Treatment = string.IsNullOrWhiteSpace(dto.Treatment) ? null : dto.Treatment.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.MedicalHistories.Add(entity);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, new ApiResponse<object>(201, "Creado", new { id = entity.Id }));
    }

    // PATCH api/v1/medical-history/{id}
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] MedicalHistoryUpdateDto dto, CancellationToken ct)
    {
        var entity = await _db.MedicalHistories.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrado"));

        if (dto.PatientId.HasValue && dto.PatientId.Value != Guid.Empty)
        {
            var patientExists = await _db.Patients.AsNoTracking().AnyAsync(p => p.Id == dto.PatientId.Value, ct);
            if (!patientExists)
                return BadRequest(new ApiResponse<object>(400, "patientId inválido: no existe ese paciente"));

            entity.PatientId = dto.PatientId.Value;
        }

        if (dto.Condition is not null)
        {
            var c = dto.Condition.Trim();
            if (string.IsNullOrWhiteSpace(c))
                return BadRequest(new ApiResponse<object>(400, "condition no puede estar vacío"));

            entity.Condition = c;
        }

        if (dto.Diagnosis is not null) entity.Diagnosis = string.IsNullOrWhiteSpace(dto.Diagnosis) ? null : dto.Diagnosis.Trim();
        if (dto.Treatment is not null) entity.Treatment = string.IsNullOrWhiteSpace(dto.Treatment) ? null : dto.Treatment.Trim();
        if (dto.Notes is not null) entity.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Actualizado"));
    }

    private Guid GetUserId()
    {
        // intenta varias claves típicas del JWT en ASP.NET
        var sub =
            User.FindFirst("sub")?.Value ??
            User.FindFirst("nameid")?.Value ??
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

}
