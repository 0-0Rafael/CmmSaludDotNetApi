using System.Text.Json;
using CmmSalud.Api.Common;
using CmmSalud.Api.Contracts.Appointments;
using CmmSalud.Api.Data;
using CmmSalud.Api.Domain.Entities;
using CmmSalud.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/appointments")]
[Authorize]
public sealed class AppointmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AppointmentsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? doctorId,
        [FromQuery] Guid? patientId,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        // ✅ Paciente: forzar PatientId desde el token
        if (User.IsInRole("patient"))
        {
            var claim =
                User.FindFirst("patientId")?.Value ??
                User.FindFirst("PatientId")?.Value;

            if (Guid.TryParse(claim, out var pid) && pid != Guid.Empty)
                patientId = pid;
        }

        // ✅ Doctor: si no mandan doctorId, sacarlo del token
        if (User.IsInRole("doctor") && (doctorId is null || doctorId == Guid.Empty))
        {
            var claim =
                User.FindFirst("doctorId")?.Value ??
                User.FindFirst("DoctorId")?.Value;

            if (Guid.TryParse(claim, out var did) && did != Guid.Empty)
                doctorId = did;
        }

        var q = _db.Appointments.AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .AsQueryable();

        if (doctorId is not null && doctorId != Guid.Empty)
            q = q.Where(a => a.DoctorId == doctorId.Value);

        if (patientId is not null && patientId != Guid.Empty)
            q = q.Where(a => a.PatientId == patientId.Value);

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<AppointmentStatus>(status, true, out var st))
        {
            q = q.Where(a => a.Status == st);
        }

        if (from is not null) q = q.Where(a => a.AppointmentDate >= from);
        if (to is not null) q = q.Where(a => a.AppointmentDate <= to);

        var items = await q
            .OrderByDescending(a => a.AppointmentDate)
            .Take(500)
            .Select(a => new
            {
                id = a.Id,
                doctorId = a.DoctorId,           // ✅ útil para el front
                patientId = a.PatientId,         // ✅ útil para el front
                appointmentDate = a.AppointmentDate,
                status = a.Status.ToString(),
                reason = a.Reason,
                notes = a.Notes,
                fee = a.Fee,
                isPaid = a.IsPaid,
                requiresPayment = a.RequiresPayment,

                patient = a.Patient == null ? null : new
                {
                    id = a.Patient.Id,
                    firstName = a.Patient.FirstName,
                    lastName = a.Patient.LastName,
                    documentId = a.Patient.DocumentId
                },

                doctor = a.Doctor == null ? null : new
                {
                    id = a.Doctor.Id,
                    firstName = a.Doctor.FirstName,
                    lastName = a.Doctor.LastName,
                    licenseNumber = a.Doctor.LicenseNumber
                },
            })
            .ToListAsync(ct);

        return Ok(new ApiResponse<object>(200, "OK", items));
    }

    [Authorize(Roles = "admin,secretary,patient")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // ✅ Si es paciente, fuerza PatientId desde el token
        if (User.IsInRole("patient"))
        {
            var claim =
                User.FindFirst("patientId")?.Value ??
                User.FindFirst("PatientId")?.Value;

            if (Guid.TryParse(claim, out var pid) && pid != Guid.Empty)
                req.PatientId = pid;
        }

        var doctor = await _db.Doctors
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == req.DoctorId, ct);

        if (doctor is null)
            return NotFound(new ApiResponse<object>(404, "Doctor no encontrado."));

        var patientExists = await _db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == req.PatientId, ct);

        if (!patientExists)
            return NotFound(new ApiResponse<object>(404, "Paciente no encontrado."));

        var appointmentDate = req.ScheduledDate;

        var clash = await _db.Appointments.AnyAsync(a =>
            a.DoctorId == req.DoctorId &&
            a.AppointmentDate == appointmentDate &&
            a.Status != AppointmentStatus.cancelled, ct);

        if (clash)
            return BadRequest(new ApiResponse<object>(400, "El doctor ya tiene una cita en ese horario."));

        var entity = new Appointment
        {
            Id = Guid.NewGuid(),
            DoctorId = req.DoctorId,
            PatientId = req.PatientId,
            AppointmentDate = appointmentDate,
            Reason = req.Reason,
            Status = AppointmentStatus.scheduled,
            Notes = null,
            Fee = doctor.ConsultationFee,
            RequiresPayment = true,
            IsPaid = false,
            IsConfirmed = false,
            ConfirmationDeadline = null
        };

        _db.Appointments.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new ApiResponse<object>(200, "Cita creada", new
        {
            id = entity.Id,
            appointmentDate = entity.AppointmentDate,
            status = entity.Status.ToString(),
            doctorId = entity.DoctorId,
            patientId = entity.PatientId
        }));
    }

    // ✅ DTO seguro para PATCH (acepta status string o number)
    public sealed class UpdateAppointmentRequest
    {
        public DateTime? AppointmentDate { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public string? Reason { get; set; }
        public string? Notes { get; set; }

        public JsonElement? Status { get; set; } // string "scheduled" o number 0/1/...
        public decimal? Fee { get; set; }

        public bool? RequiresPayment { get; set; }
        public bool? IsPaid { get; set; }

        public DateTime? ConfirmationDeadline { get; set; }
        public bool? IsConfirmed { get; set; }
    }

    [Authorize(Roles = "admin,secretary,doctor")]
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAppointmentRequest req, CancellationToken ct)
    {
        var entity = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));

        // ✅ Ownership: si es doctor, SOLO puede editar sus propias citas
        if (User.IsInRole("doctor"))
        {
            var claim =
                User.FindFirst("doctorId")?.Value ??
                User.FindFirst("DoctorId")?.Value;

            if (!Guid.TryParse(claim, out var did) || did == Guid.Empty)
                return Forbid();

            if (entity.DoctorId != did)
                return Forbid();
        }

        // ✅ fecha: soporta appointmentDate o scheduledDate del front
        var newDate = req.AppointmentDate ?? req.ScheduledDate;
        if (newDate is not null)
            entity.AppointmentDate = newDate.Value;

        if (req.Reason is not null)
            entity.Reason = req.Reason;

        if (req.Notes is not null)
            entity.Notes = req.Notes;

        // ✅ status: string o number
        if (req.Status is not null)
        {
            var stEl = req.Status.Value;
            AppointmentStatus? parsed = null;

            if (stEl.ValueKind == JsonValueKind.String)
            {
                var s = stEl.GetString();
                if (!string.IsNullOrWhiteSpace(s) && Enum.TryParse<AppointmentStatus>(s, true, out var st))
                    parsed = st;
            }
            else if (stEl.ValueKind == JsonValueKind.Number && stEl.TryGetInt32(out var n))
            {
                if (Enum.IsDefined(typeof(AppointmentStatus), n))
                    parsed = (AppointmentStatus)n;
            }

            if (parsed is null)
                return BadRequest(new ApiResponse<object>(400, "Status inválido."));

            entity.Status = parsed.Value;
        }

        // 🔒 OJO: decidir qué campos puede cambiar un doctor.
        // Si quieres que el doctor NO pueda marcar IsPaid/RequiresPayment, comenta estas líneas.
        if (req.Fee is not null)
            entity.Fee = req.Fee.Value;

        if (req.RequiresPayment is not null)
            entity.RequiresPayment = req.RequiresPayment.Value;

        if (req.IsPaid is not null)
            entity.IsPaid = req.IsPaid.Value;

        if (req.ConfirmationDeadline is not null)
            entity.ConfirmationDeadline = req.ConfirmationDeadline;

        if (req.IsConfirmed is not null)
            entity.IsConfirmed = req.IsConfirmed.Value;

        await _db.SaveChangesAsync(ct);

        // ✅ respuesta limpia (sin ciclos, sin navegación gigante)
        return Ok(new ApiResponse<object>(200, "Cita actualizada", new
        {
            id = entity.Id,
            doctorId = entity.DoctorId,
            patientId = entity.PatientId,
            appointmentDate = entity.AppointmentDate,
            status = entity.Status.ToString(),
            reason = entity.Reason,
            notes = entity.Notes,
            fee = entity.Fee,
            isPaid = entity.IsPaid,
            requiresPayment = entity.RequiresPayment,
            confirmationDeadline = entity.ConfirmationDeadline,
            isConfirmed = entity.IsConfirmed
        }));
    }

    // ✅ Mejor que "delete": cancelar (soft delete). Así no rompes historial.
    [Authorize(Roles = "admin,secretary,doctor")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));

        if (User.IsInRole("doctor"))
        {
            var claim =
                User.FindFirst("doctorId")?.Value ??
                User.FindFirst("DoctorId")?.Value;

            if (!Guid.TryParse(claim, out var did) || did == Guid.Empty)
                return Forbid();

            if (entity.DoctorId != did)
                return Forbid();
        }

        entity.Status = AppointmentStatus.cancelled;
        await _db.SaveChangesAsync(ct);

        return Ok(new ApiResponse<object>(200, "Cancelada"));
    }
}
