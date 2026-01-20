using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using CmmSalud.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) => _db = db;

    [Authorize(Roles = "admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> Admin(CancellationToken ct)
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var totalUsers = await _db.Users.CountAsync(ct);
        var activeUsers = await _db.Users.CountAsync(u => u.IsActive, ct);
        var totalAppointments = await _db.Appointments.CountAsync(ct);
        var totalPayments = await _db.Payments.CountAsync(ct);

        var recentActivity = new[]
        {
            new
            {
                id = Guid.NewGuid(),
                type = "system",
                user = "Sistema",
                action = "Dashboard generado",
                time = DateTime.UtcNow,
                status = "completed"
            }
        };

        var data = new
        {
            admin = new { role = "admin" },
            users = new
            {
                totalUsers,
                activeUsers,
                newUsersToday = await _db.Users.CountAsync(u => u.CreatedAt >= todayStart && u.CreatedAt < todayEnd, ct),
                usersByRole = new
                {
                    patients = await _db.Users.CountAsync(u => u.Role == UserRole.patient, ct),
                    doctors = await _db.Users.CountAsync(u => u.Role == UserRole.doctor, ct),
                    secretaries = await _db.Users.CountAsync(u => u.Role == UserRole.secretary, ct),
                    admins = await _db.Users.CountAsync(u => u.Role == UserRole.admin, ct)
                }
            },
            appointments = new
            {
                totalAppointments,
                todayAppointments = await _db.Appointments.CountAsync(a => a.AppointmentDate >= todayStart && a.AppointmentDate < todayEnd, ct)
            },
            financial = new
            {
                totalPayments,
                totalRevenue = await _db.Payments
                    .Where(p => p.Status == PaymentStatus.completed)
                    .SumAsync(p => (decimal?)p.Amount, ct) ?? 0
            },
            recentActivity
        };

        return Ok(new ApiResponse<object>(200, "OK", data));
    }

    [Authorize(Roles = "secretary,admin")]
    [HttpGet("secretary")]
    public async Task<IActionResult> Secretary(CancellationToken ct)
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var data = new
        {
            secretary = new { role = "secretary" },
            appointments = new
            {
                today = await _db.Appointments.CountAsync(a => a.AppointmentDate >= todayStart && a.AppointmentDate < todayEnd, ct),
                pendingConfirmation = await _db.Appointments.CountAsync(a => a.Status == AppointmentStatus.scheduled, ct)
            }
        };

        return Ok(new ApiResponse<object>(200, "OK", data));
    }

    [Authorize(Roles = "doctor,admin")]
    [HttpGet("doctor")]
    public async Task<IActionResult> Doctor([FromQuery] Guid? doctorId, CancellationToken ct)
    {
        // 1) Resolver doctorId: query param > claim del token
        if (doctorId is null)
        {
            var claim =
                User.FindFirst("doctorId")?.Value ??
                User.FindFirst("DoctorId")?.Value;

            if (Guid.TryParse(claim, out var parsed))
                doctorId = parsed;
        }

        if (doctorId is null || doctorId == Guid.Empty)
            return BadRequest(new ApiResponse<object>(400, "No se pudo determinar doctorId (token/query)."));

        var did = doctorId.Value;

        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var todayEnd = todayStart.AddDays(1);
        var monthStart = new DateTime(now.Year, now.Month, 1);

        // 2) Doctor info
        var doctor = await _db.Doctors.AsNoTracking()
            .Include(d => d.Specialty)
            .Where(d => d.Id == did)
            .Select(d => new
            {
                role = "doctor",
                id = d.Id,
                firstName = d.FirstName,
                lastName = d.LastName,
                licenseNumber = d.LicenseNumber,
                phone = d.Phone,
                consultationFee = d.ConsultationFee,
                specialty = d.Specialty == null ? null : new
                {
                    id = d.Specialty.Id,
                    name = d.Specialty.Name
                }
            })
            .FirstOrDefaultAsync(ct);

        if (doctor is null)
            return NotFound(new ApiResponse<object>(404, "Doctor no encontrado."));

        // Queries base
        var apptQ = _db.Appointments.AsNoTracking().Where(a => a.DoctorId == did);
        var rxQ = _db.Prescriptions.AsNoTracking().Where(p => p.DoctorId == did);

        // 3) Appointments stats
        var apptTotal = await apptQ.CountAsync(ct);
        var apptToday = await apptQ.CountAsync(a => a.AppointmentDate >= todayStart && a.AppointmentDate < todayEnd, ct);
        var apptUpcoming = await apptQ.CountAsync(a =>
            a.AppointmentDate > now &&
            (a.Status == AppointmentStatus.scheduled || a.Status == AppointmentStatus.confirmed), ct);

        var apptCompleted = await apptQ.CountAsync(a => a.Status == AppointmentStatus.completed, ct);
        var apptCancelled = await apptQ.CountAsync(a => a.Status == AppointmentStatus.cancelled, ct);

        // 4) Schedule (listas)
        var todayAppointments = await apptQ
            .Include(a => a.Patient)
            .Where(a => a.AppointmentDate >= todayStart && a.AppointmentDate < todayEnd)
            .OrderBy(a => a.AppointmentDate)
            .Take(10)
            .Select(a => new
            {
                id = a.Id,
                appointmentDate = a.AppointmentDate,
                status = a.Status.ToString(),
                patient = a.Patient == null ? null : new
                {
                    id = a.Patient.Id,
                    firstName = a.Patient.FirstName,
                    lastName = a.Patient.LastName
                }
            })
            .ToListAsync(ct);

        var upcomingAppointments = await apptQ
            .Include(a => a.Patient)
            .Where(a =>
                a.AppointmentDate > now &&
                (a.Status == AppointmentStatus.scheduled || a.Status == AppointmentStatus.confirmed))
            .OrderBy(a => a.AppointmentDate)
            .Take(10)
            .Select(a => new
            {
                id = a.Id,
                appointmentDate = a.AppointmentDate,
                status = a.Status.ToString(),
                patient = a.Patient == null ? null : new
                {
                    id = a.Patient.Id,
                    firstName = a.Patient.FirstName,
                    lastName = a.Patient.LastName
                }
            })
            .ToListAsync(ct);

        var nextAppointment = upcomingAppointments.FirstOrDefault();

        // 5) Prescriptions stats
        var rxTotal = await rxQ.CountAsync(ct);
        var rxIssuedThisMonth = await rxQ.CountAsync(p => p.CreatedAt >= monthStart, ct);
        var rxActive = await rxQ.CountAsync(p => p.Status == PrescriptionStatus.active, ct);
        var rxExpired = await rxQ.CountAsync(p => p.Status == PrescriptionStatus.expired, ct);

        // 6) Patients stats (pacientes relacionados al doctor)
        var patientIds = await apptQ.Select(a => a.PatientId)
            .Union(rxQ.Select(p => p.PatientId))
            .Distinct()
            .ToListAsync(ct);

        var patientsQ = _db.Patients.AsNoTracking()
            .Include(p => p.User)
            .Where(p => patientIds.Contains(p.Id));

        var patientsTotal = await patientsQ.CountAsync(ct);
        var patientsNewThisMonth = await patientsQ.CountAsync(p => p.CreatedAt >= monthStart, ct);
        var patientsActive = await patientsQ.CountAsync(p => p.User != null && p.User.IsActive, ct);
        var patientsInactive = Math.Max(0, patientsTotal - patientsActive);

        // 7) Activity
        var recentPrescriptions = await rxQ
            .Include(p => p.Patient)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5)
            .Select(p => new
            {
                id = p.Id,
                createdAt = p.CreatedAt,
                medicationName = p.MedicationName,
                status = p.Status.ToString(),
                patient = p.Patient == null
                    ? null
                    : new { id = p.Patient.Id, firstName = p.Patient.FirstName, lastName = p.Patient.LastName }
            })
            .ToListAsync(ct);

        var recentAppointments = await apptQ
            .Include(a => a.Patient)
            .OrderByDescending(a => a.AppointmentDate)
            .Take(5)
            .Select(a => new
            {
                id = a.Id,
                appointmentDate = a.AppointmentDate,
                status = a.Status.ToString(),
                patient = a.Patient == null
                    ? null
                    : new { id = a.Patient.Id, firstName = a.Patient.FirstName, lastName = a.Patient.LastName }
            })
            .ToListAsync(ct);

        var completionRate = apptTotal == 0
            ? 0
            : Math.Round((decimal)apptCompleted / apptTotal * 100m, 2);

        // Objetos base
        var appointments = new
        {
            total = apptTotal,
            today = apptToday,
            upcoming = apptUpcoming,
            completed = apptCompleted,
            cancelled = apptCancelled
        };

        var patients = new
        {
            total = patientsTotal,
            newThisMonth = patientsNewThisMonth,
            active = patientsActive,
            inactive = patientsInactive
        };

        var prescriptions = new
        {
            total = rxTotal,
            issued = rxIssuedThisMonth,
            issuedThisMonth = rxIssuedThisMonth, // alias
            active = rxActive,
            expired = rxExpired
        };

        var activity = new
        {
            recentAppointments,
            recentPrescriptions,
            unreadNotifications = 0,
            lastLogin = (DateTime?)null
        };

        var schedule = new
        {
            nextAppointment,
            todayAppointments,
            upcomingAppointments
        };

        var performance = new
        {
            averageRating = 0,
            totalReviews = 0,
            completionRate
        };

        // 8) Payload “frontend-friendly” + compat con mappers viejos
        var data = new
        {
            doctor,
            appointments,
            patients,
            prescriptions,
            activity,
            schedule,
            performance,

            // Aliases extra por compatibilidad
            appointmentStats = appointments,
            patientStats = patients,
            prescriptionStats = prescriptions,

            // Aliases en root (algunos hooks lo esperan así)
            recentAppointments,
            recentPrescriptions,
            unreadNotifications = 0,
            lastLogin = (DateTime?)null,

            // Stats agrupados (por si el hook usa data.stats.*)
            stats = new
            {
                appointments,
                patients,
                prescriptions,
                performance
            }
        };

        return Ok(new ApiResponse<object>(200, "OK", data));
    }
}
