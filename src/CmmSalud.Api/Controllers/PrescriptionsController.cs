using System.Globalization;
using System.Text.Json;
using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using CmmSalud.Api.Domain.Entities;
using CmmSalud.Api.Domain.Enums;
using CmmSalud.Api.DTOs.Prescriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/prescriptions")]
[Authorize]
public sealed class PrescriptionsController : ControllerBase
{
    private readonly AppDbContext _db;
    public PrescriptionsController(AppDbContext db) => _db = db;

    // -----------------------------
    // Helpers
    // -----------------------------
    private Guid? GetGuidClaim(string claimType)
    {
        var v = User.FindFirst(claimType)?.Value;
        return Guid.TryParse(v, out var id) ? id : null;
    }

    private Guid? DoctorIdFromToken() => GetGuidClaim("doctorId");
    private Guid? PatientIdFromToken() => GetGuidClaim("patientId");
    private Guid? PharmacyIdFromToken() => GetGuidClaim("pharmacyId");

    private static DateOnly TodayUtc() => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string ToDate(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static string ToIso(DateTime d) => d.ToString("O");

    private string? ToPublicUrl(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl}/{relativePath.Replace("\\", "/").TrimStart('/')}";
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;

        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        return false;
    }

    private static DateOnly? ParseDateOnly(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Null) return null;

        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d))
                return d;

            if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                return d2;
        }

        return null;
    }

    private static int? ParseInt(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n2)) return n2;
        return null;
    }

    private static bool? ParseBool(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
        return null;
    }

    private static PrescriptionStatus? ParseStatus(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
            return (PrescriptionStatus)n;

        if (v.ValueKind == JsonValueKind.String)
        {
            var s = (v.GetString() ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(s)) return null;

            return s switch
            {
                "paused" => PrescriptionStatus.paused,
                "completed" => PrescriptionStatus.completed,
                "active" => PrescriptionStatus.active,
                "used" => PrescriptionStatus.used,
                "expired" => PrescriptionStatus.expired,
                "cancelled" => PrescriptionStatus.cancelled,
                "canceled" => PrescriptionStatus.cancelled,
                "hidden" => PrescriptionStatus.hidden,
                "regenerated" => PrescriptionStatus.regenerated,
                _ => Enum.TryParse<PrescriptionStatus>(s, true, out var st) ? st : null
            };
        }

        return null;
    }

    private static void ApplyContinuousRulesOrThrow(Prescription p)
    {
        if (!p.IsContinuous)
        {
            p.RefillEveryDays = null;
            p.TreatmentEndDate = null;
            p.NextRefillDate = null;
            return;
        }

        if (p.RefillEveryDays is null || p.RefillEveryDays <= 0)
            throw new InvalidOperationException("RefillEveryDays debe ser mayor a 0.");

        if (p.TreatmentEndDate is null)
            throw new InvalidOperationException("TreatmentEndDate es requerido para recetas continuas.");

        if (p.TreatmentEndDate.Value <= p.IssueDate)
            throw new InvalidOperationException("TreatmentEndDate debe ser posterior a IssueDate.");

        // Expira al final del tratamiento
        p.ExpirationDate = p.TreatmentEndDate.Value;

        // NextRefillDate inicial
        p.NextRefillDate ??= p.IssueDate.AddDays(p.RefillEveryDays.Value);

        // MaxDispensations calculado
        var totalDays = p.TreatmentEndDate.Value.DayNumber - p.IssueDate.DayNumber;
        var calc = (int)Math.Ceiling(totalDays / (double)p.RefillEveryDays.Value) + 1;
        p.MaxDispensations = Math.Max(1, calc);

        if (p.CurrentDispensations < 0) p.CurrentDispensations = 0;
        if (p.CurrentDispensations > p.MaxDispensations) p.CurrentDispensations = p.MaxDispensations;
    }

    private string ContinuousState(Prescription p)
    {
        if (!p.IsContinuous) return "one_time";

        var today = TodayUtc();

        if (p.Status == PrescriptionStatus.paused) return "paused";
        if (p.Status == PrescriptionStatus.cancelled || p.Status == PrescriptionStatus.hidden) return "cancelled";
        if (p.Status == PrescriptionStatus.expired || p.ExpirationDate < today) return "expired";
        if (p.Status == PrescriptionStatus.completed) return "ended";

        if (p.TreatmentEndDate is { } end && today > end) return "ended";

        if (p.NextRefillDate is null) return "ok";
        if (today >= p.NextRefillDate.Value) return "due";

        return "ok";
    }

    private string MapStatusForClient(Prescription p)
    {
        // paciente ve "used"; doctor/admin/secretary/pharmacy ven "dispensed"
        var usedLabel = User.IsInRole("patient") ? "used" : "dispensed";
        var today = TodayUtc();

        if (p.Status == PrescriptionStatus.hidden || p.Status == PrescriptionStatus.cancelled)
            return "cancelled";

        if (p.Status == PrescriptionStatus.paused)
            return "paused";

        if (p.Status == PrescriptionStatus.expired || p.ExpirationDate < today)
            return "expired";

        if (p.Status == PrescriptionStatus.completed)
            return usedLabel;

        if (p.Status == PrescriptionStatus.used || p.CurrentDispensations >= p.MaxDispensations)
            return usedLabel;

        return "active";
    }

    // ✅ NEW: lee LicenseNumber sin romper compilación si el nombre cambia en la entidad
    private static string? GetDoctorLicenseNumber(Doctor? d)
    {
        if (d is null) return null;

        try
        {
            var t = d.GetType();
            var candidates = new[] { "LicenseNumber", "licenseNumber", "MedicalLicense", "License", "LicenseNo" };

            foreach (var name in candidates)
            {
                var p = t.GetProperty(name);
                if (p is null) continue;

                var v = p.GetValue(d);
                var s = v?.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        catch { }

        return null;
    }

    private object MapPrescription(Prescription p)
    {
        object specialty = "";
        try
        {
            var spec = p.Doctor?.Specialty;
            if (spec is not null) specialty = new { name = spec.Name };
        }
        catch { }

        var sigUrl = p.Doctor?.Assets?.SignaturePath is null ? null : ToPublicUrl(p.Doctor.Assets.SignaturePath);
        var sealUrl = p.Doctor?.Assets?.SealPath is null ? null : ToPublicUrl(p.Doctor.Assets.SealPath);

        // ✅ NEW: licencia del doctor para que el PDF nunca salga en N/A
        var licenseNumber = GetDoctorLicenseNumber(p.Doctor);

        return new
        {
            id = p.Id,
            patientId = p.PatientId,
            doctorId = p.DoctorId,

            medicationName = p.MedicationName,
            dosage = p.Dosage,
            frequency = p.Frequency,
            duration = p.Duration,
            instructions = p.Instructions,
            diagnosis = (string?)null,

            status = MapStatusForClient(p),

            isContinuous = p.IsContinuous,
            refillEveryDays = p.RefillEveryDays,
            treatmentEndDate = p.TreatmentEndDate.HasValue ? ToDate(p.TreatmentEndDate.Value) : null,
            nextRefillDate = p.NextRefillDate.HasValue ? ToDate(p.NextRefillDate.Value) : null,
            continuousState = ContinuousState(p),

            issuedDate = ToDate(p.IssueDate),
            issueDate = ToDate(p.IssueDate),
            expirationDate = ToDate(p.ExpirationDate),

            currentDispensations = p.CurrentDispensations,
            maxDispensations = p.MaxDispensations,

            digitalSignature = p.DigitalSignature,

            dispensedAt = p.LastDispensedAt,
            lastDispensedAt = p.LastDispensedAt,

            createdAt = ToIso(p.CreatedAt),
            updatedAt = ToIso(p.UpdatedAt),

            patient = p.Patient == null ? null : new
            {
                id = p.Patient.Id,
                firstName = p.Patient.FirstName,
                lastName = p.Patient.LastName,
                documentId = p.Patient.DocumentId,
                dateOfBirth = p.Patient.DateOfBirth.ToString("yyyy-MM-dd"),
                phone = p.Patient.Phone
            },

            doctor = p.Doctor == null ? null : new
            {
                id = p.Doctor.Id,
                firstName = p.Doctor.FirstName,
                lastName = p.Doctor.LastName,
                specialty,

                // ✅ FIX: ahora sí llega al front
                licenseNumber = licenseNumber,

                // ✅ URLs que ya estabas mandando
                signatureUrl = sigUrl,
                sealUrl = sealUrl,

                // ✅ ALIAS para el front (tu TS estaba buscando stampUrl)
                stampUrl = sealUrl
            }
        };
    }

    private static string NormalizeDocument(string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc)) return "";
        return doc.Trim().Replace("-", "").Replace(" ", "");
    }

    // -----------------------------
    // GET (general)
    // -----------------------------
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? patientId,
        [FromQuery] Guid? doctorId,
        [FromQuery] string? patientDocumentId,
        [FromQuery] string? status,
        [FromQuery] string? medicationName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;
        pageSize = pageSize > 100 ? 100 : pageSize;

        // doctor/patient quedan “amarrados” a su token
        if (User.IsInRole("doctor"))
        {
            var tokenDoctorId = DoctorIdFromToken();
            if (tokenDoctorId is not null) doctorId = tokenDoctorId;
        }
        else if (User.IsInRole("patient"))
        {
            var tokenPatientId = PatientIdFromToken();
            if (tokenPatientId is not null) patientId = tokenPatientId;
        }

        // ✅ farmacia: NO permitimos listar todo (obligatorio patientDocumentId)
        if (User.IsInRole("pharmacy"))
        {
            if (string.IsNullOrWhiteSpace(patientDocumentId))
                return BadRequest(new ApiResponse<object>(400, "patientDocumentId es requerido para farmacia."));
        }

        var normalizedDoc = NormalizeDocument(patientDocumentId);

        var q = _db.Prescriptions.AsNoTracking()
            .Include(p => p.Patient)
            .Include(p => p.Doctor).ThenInclude(d => d.Specialty)
            .Include(p => p.Doctor).ThenInclude(d => d.Assets)
            .AsQueryable();

        if (patientId is not null) q = q.Where(p => p.PatientId == patientId);
        if (doctorId is not null) q = q.Where(p => p.DoctorId == doctorId);

        if (!string.IsNullOrWhiteSpace(normalizedDoc))
        {
            var doc = normalizedDoc;
            var raw = (patientDocumentId ?? "").Trim();

            q = q.Where(p =>
                p.Patient.DocumentId == raw ||
                p.Patient.DocumentId.Replace("-", "").Replace(" ", "") == doc
            );
        }

        if (!string.IsNullOrWhiteSpace(medicationName))
        {
            var term = medicationName.Trim();
            q = q.Where(p => p.MedicationName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLowerInvariant();
            var today = TodayUtc();

            if (s is "used" or "dispensed")
                q = q.Where(p => p.Status == PrescriptionStatus.used
                              || p.Status == PrescriptionStatus.completed
                              || p.CurrentDispensations >= p.MaxDispensations);

            else if (s == "expired")
                q = q.Where(p => p.Status == PrescriptionStatus.expired || p.ExpirationDate < today);

            else if (s is "cancelled" or "canceled" or "hidden")
                q = q.Where(p => p.Status == PrescriptionStatus.cancelled || p.Status == PrescriptionStatus.hidden);

            else if (s == "paused")
                q = q.Where(p => p.Status == PrescriptionStatus.paused);

            else if (s == "active")
                q = q.Where(p => p.Status == PrescriptionStatus.active && p.ExpirationDate >= today);
        }

        var totalCount = await q.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var mapped = items.Select(MapPrescription).ToList();

        return Ok(new ApiResponse<object>(200, "OK", new
        {
            page,
            pageSize,
            totalPages,
            totalCount,
            items = mapped
        }));
    }

    // -----------------------------
    // GET by documentId (farmacia)
    // -----------------------------
    [Authorize(Roles = "pharmacy,admin,secretary")]
    [HttpGet("by-document/{documentId}")]
    public async Task<IActionResult> GetByDocument(
        string documentId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;
        pageSize = pageSize > 100 ? 100 : pageSize;

        var doc = NormalizeDocument(documentId);
        var raw = documentId.Trim();

        if (string.IsNullOrWhiteSpace(doc))
            return BadRequest(new ApiResponse<object>(400, "Documento inválido."));

        var q = _db.Prescriptions.AsNoTracking()
            .Include(p => p.Patient)
            .Include(p => p.Doctor).ThenInclude(d => d.Specialty)
            .Include(p => p.Doctor).ThenInclude(d => d.Assets)
            .Where(p =>
                p.Patient.DocumentId == raw ||
                p.Patient.DocumentId.Replace("-", "").Replace(" ", "") == doc
            )
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLowerInvariant();
            var today = TodayUtc();

            if (s is "used" or "dispensed")
                q = q.Where(p => p.Status == PrescriptionStatus.used
                              || p.Status == PrescriptionStatus.completed
                              || p.CurrentDispensations >= p.MaxDispensations);

            else if (s == "expired")
                q = q.Where(p => p.Status == PrescriptionStatus.expired || p.ExpirationDate < today);

            else if (s is "cancelled" or "canceled" or "hidden")
                q = q.Where(p => p.Status == PrescriptionStatus.cancelled || p.Status == PrescriptionStatus.hidden);

            else if (s == "paused")
                q = q.Where(p => p.Status == PrescriptionStatus.paused);

            else if (s == "active")
                q = q.Where(p => p.Status == PrescriptionStatus.active && p.ExpirationDate >= today);
        }

        var totalCount = await q.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new ApiResponse<object>(200, "OK", new
        {
            documentId = raw,
            page,
            pageSize,
            totalPages,
            totalCount,
            items = items.Select(MapPrescription).ToList()
        }));
    }

    // -----------------------------
    // CREATE
    // -----------------------------
    [Authorize(Roles = "doctor,admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePrescriptionRequest req, CancellationToken ct)
    {
        if (User.IsInRole("doctor"))
        {
            var tokenDoctorId = DoctorIdFromToken();
            if (tokenDoctorId is null)
                return Unauthorized(new ApiResponse<object>(401, "Token inválido (doctorId faltante)."));

            req = req with { DoctorId = tokenDoctorId.Value };
        }

        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == req.PatientId, ct);
        if (patient is null) return BadRequest(new ApiResponse<object>(400, "PatientId inválido."));

        var doctor = await _db.Doctors
            .Include(d => d.Specialty)
            .Include(d => d.Assets)
            .FirstOrDefaultAsync(d => d.Id == req.DoctorId, ct);
        if (doctor is null) return BadRequest(new ApiResponse<object>(400, "DoctorId inválido."));

        var today = TodayUtc();
        var now = DateTime.UtcNow;

        var entity = new Prescription
        {
            Id = Guid.NewGuid(),
            PatientId = req.PatientId,
            DoctorId = req.DoctorId,

            MedicationName = req.MedicationName.Trim(),
            Dosage = req.Dosage.Trim(),
            Frequency = req.Frequency.Trim(),
            Duration = (req.Duration ?? "").Trim(),
            Instructions = req.Instructions?.Trim(),

            IssueDate = today,
            ExpirationDate = today.AddDays(30),

            MaxDispensations = 1,
            CurrentDispensations = 0,
            Status = PrescriptionStatus.active,

            DigitalSignature = $"RX-{Guid.NewGuid():N}".ToUpperInvariant(),

            CreatedAt = now,
            UpdatedAt = now,

            IsContinuous = req.IsContinuous,
            RefillEveryDays = req.RefillEveryDays,
            TreatmentEndDate = req.TreatmentEndDate
        };

        try
        {
            ApplyContinuousRulesOrThrow(entity);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>(400, ex.Message));
        }

        _db.Prescriptions.Add(entity);
        await _db.SaveChangesAsync(ct);

        var created = await _db.Prescriptions.AsNoTracking()
            .Include(p => p.Patient)
            .Include(p => p.Doctor).ThenInclude(d => d.Specialty)
            .Include(p => p.Doctor).ThenInclude(d => d.Assets)
            .FirstAsync(p => p.Id == entity.Id, ct);

        return StatusCode(201, new ApiResponse<object>(201, "Receta creada", MapPrescription(created)));
    }

    // -----------------------------
    // UPDATE (JsonElement)
    // -----------------------------
    [Authorize(Roles = "doctor,admin")]
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] JsonElement req, CancellationToken ct)
    {
        var entity = await _db.Prescriptions
            .Include(p => p.Patient)
            .Include(p => p.Doctor).ThenInclude(d => d.Specialty)
            .Include(p => p.Doctor).ThenInclude(d => d.Assets)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));

        if (User.IsInRole("doctor"))
        {
            var tokenDoctorId = DoctorIdFromToken();
            if (tokenDoctorId is not null && entity.DoctorId != tokenDoctorId.Value)
                return Forbid();
        }

        if (TryGet(req, "medicationName", out var vMed) && vMed.ValueKind == JsonValueKind.String)
        {
            var s = (vMed.GetString() ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(s)) entity.MedicationName = s;
        }

        if (TryGet(req, "dosage", out var vDos) && vDos.ValueKind == JsonValueKind.String)
        {
            var s = (vDos.GetString() ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(s)) entity.Dosage = s;
        }

        if (TryGet(req, "frequency", out var vFre) && vFre.ValueKind == JsonValueKind.String)
        {
            var s = (vFre.GetString() ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(s)) entity.Frequency = s;
        }

        if (TryGet(req, "duration", out var vDur))
        {
            if (vDur.ValueKind == JsonValueKind.Null) entity.Duration = "";
            else if (vDur.ValueKind == JsonValueKind.String) entity.Duration = (vDur.GetString() ?? "").Trim();
        }

        if (TryGet(req, "instructions", out var vIns))
        {
            if (vIns.ValueKind == JsonValueKind.Null) entity.Instructions = null;
            else if (vIns.ValueKind == JsonValueKind.String) entity.Instructions = (vIns.GetString() ?? "").Trim();
        }

        if (TryGet(req, "expirationDate", out var vExp))
        {
            var d = ParseDateOnly(vExp);
            if (d is not null) entity.ExpirationDate = d.Value;
        }

        if (TryGet(req, "maxDispensations", out var vMax))
        {
            var n = ParseInt(vMax);
            if (n is not null && n.Value > 0) entity.MaxDispensations = n.Value;
        }

        if (TryGet(req, "status", out var vSt))
        {
            var st = ParseStatus(vSt);
            if (st is not null) entity.Status = st.Value;
        }

        bool? isCont = null;
        if (TryGet(req, "isContinuous", out var vCont))
            isCont = ParseBool(vCont);

        int? refill = null;
        if (TryGet(req, "refillEveryDays", out var vRef))
            refill = ParseInt(vRef);

        DateOnly? end = null;
        if (TryGet(req, "treatmentEndDate", out var vEnd))
            end = ParseDateOnly(vEnd);

        if (isCont is not null) entity.IsContinuous = isCont.Value;
        if (refill is not null) entity.RefillEveryDays = refill;
        if (end is not null) entity.TreatmentEndDate = end;

        if (isCont == false)
        {
            entity.RefillEveryDays = null;
            entity.TreatmentEndDate = null;
            entity.NextRefillDate = null;
        }

        try
        {
            ApplyContinuousRulesOrThrow(entity);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>(400, ex.Message));
        }

        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Actualizada", MapPrescription(entity)));
    }

    // -----------------------------
    // DISPENSE
    // -----------------------------
    public sealed class DispenseRequest
    {
        public Guid? PharmacyId { get; init; }
        public decimal? Quantity { get; init; }
        public string? Notes { get; init; }

        // Compat paciente (legacy)
        public int? CurrentDispensations { get; init; }
    }

    [HttpPost("{id:guid}/dispense")]
    public async Task<IActionResult> Dispense(Guid id, [FromBody] DispenseRequest req, CancellationToken ct)
    {
        var p = await _db.Prescriptions
            .Include(x => x.Dispensations)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (p is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));

        var today = TodayUtc();

        void advanceContinuousIfNeeded()
        {
            if (!p.IsContinuous) return;
            if (p.RefillEveryDays is null || p.RefillEveryDays <= 0) return;

            if (p.TreatmentEndDate is { } end && today > end)
            {
                p.Status = PrescriptionStatus.completed;
                p.NextRefillDate = null;
                return;
            }

            var next = today.AddDays(p.RefillEveryDays.Value);

            if (p.TreatmentEndDate is { } end2 && next > end2)
            {
                p.Status = PrescriptionStatus.completed;
                p.NextRefillDate = null;
            }
            else
            {
                p.NextRefillDate = next;
            }
        }

        // -----------------------------
        // 1) FARMACIA DISPENSA
        // -----------------------------
        if (User.IsInRole("pharmacy"))
        {
            var pharmacyId = PharmacyIdFromToken();
            if (pharmacyId is null)
                return Unauthorized(new ApiResponse<object>(401, "Token inválido (pharmacyId faltante)."));

            if (req.Quantity is null || req.Quantity <= 0)
                return BadRequest(new ApiResponse<object>(400, "Quantity inválida."));

            if (p.Status != PrescriptionStatus.active)
                return BadRequest(new ApiResponse<object>(400, "La receta no está activa."));

            if (p.ExpirationDate < today)
                return BadRequest(new ApiResponse<object>(400, "La receta está expirada."));

            if (p.CurrentDispensations >= p.MaxDispensations)
                return BadRequest(new ApiResponse<object>(400, "No quedan dispensaciones disponibles."));

            if (p.IsContinuous && p.NextRefillDate is { } nr && today < nr)
                return BadRequest(new ApiResponse<object>(400, $"Aún no toca refill. Próximo: {ToDate(nr)}"));

            var disp = new PrescriptionDispensation
            {
                PrescriptionId = id,
                PharmacyId = pharmacyId.Value,
                DispensationNumber = p.CurrentDispensations + 1,
                QuantityDispensed = req.Quantity.Value,
                PharmacistNotes = req.Notes,
                Price = 0,
                DispensedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            p.CurrentDispensations += 1;
            p.LastDispensedAt = DateTime.UtcNow;

            if (!p.IsContinuous && p.CurrentDispensations >= p.MaxDispensations)
                p.Status = PrescriptionStatus.used;

            if (p.IsContinuous)
                advanceContinuousIfNeeded();

            p.UpdatedAt = DateTime.UtcNow;

            _db.PrescriptionDispensations.Add(disp);
            await _db.SaveChangesAsync(ct);

            return Ok(new ApiResponse<object>(200, "Dispensada", disp));
        }

        // -----------------------------
        // 2) ADMIN/SECRETARY DISPENSA
        // -----------------------------
        if (req.PharmacyId is not null)
        {
            if (!(User.IsInRole("admin") || User.IsInRole("secretary")))
                return Forbid();

            if (req.Quantity is null || req.Quantity <= 0)
                return BadRequest(new ApiResponse<object>(400, "Quantity inválida."));

            if (p.Status != PrescriptionStatus.active)
                return BadRequest(new ApiResponse<object>(400, "La receta no está activa."));

            if (p.ExpirationDate < today)
                return BadRequest(new ApiResponse<object>(400, "La receta está expirada."));

            if (p.CurrentDispensations >= p.MaxDispensations)
                return BadRequest(new ApiResponse<object>(400, "No quedan dispensaciones disponibles."));

            if (p.IsContinuous && p.NextRefillDate is { } nr && today < nr)
                return BadRequest(new ApiResponse<object>(400, $"Aún no toca refill. Próximo: {ToDate(nr)}"));

            var disp = new PrescriptionDispensation
            {
                PrescriptionId = id,
                PharmacyId = req.PharmacyId.Value,
                DispensationNumber = p.CurrentDispensations + 1,
                QuantityDispensed = req.Quantity.Value,
                PharmacistNotes = req.Notes,
                Price = 0,
                DispensedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            p.CurrentDispensations += 1;
            p.LastDispensedAt = DateTime.UtcNow;

            if (!p.IsContinuous && p.CurrentDispensations >= p.MaxDispensations)
                p.Status = PrescriptionStatus.used;

            if (p.IsContinuous)
                advanceContinuousIfNeeded();

            p.UpdatedAt = DateTime.UtcNow;

            _db.PrescriptionDispensations.Add(disp);
            await _db.SaveChangesAsync(ct);

            return Ok(new ApiResponse<object>(200, "Dispensada", disp));
        }

        // -----------------------------
        // 3) PACIENTE (legacy)
        // -----------------------------
        if (req.CurrentDispensations is not null)
        {
            if (!User.IsInRole("patient"))
                return Forbid();

            var tokenPatientId = PatientIdFromToken();
            if (tokenPatientId is null || p.PatientId != tokenPatientId.Value)
                return Forbid();

            var newValue = req.CurrentDispensations.Value;
            if (newValue < 0) newValue = 0;
            if (newValue > p.MaxDispensations) newValue = p.MaxDispensations;

            p.CurrentDispensations = newValue;
            p.LastDispensedAt = DateTime.UtcNow;

            if (p.ExpirationDate < today)
                p.Status = PrescriptionStatus.expired;

            if (!p.IsContinuous && p.CurrentDispensations >= p.MaxDispensations)
                p.Status = PrescriptionStatus.used;

            if (p.IsContinuous)
                advanceContinuousIfNeeded();

            p.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            var refreshed = await _db.Prescriptions.AsNoTracking()
                .Include(x => x.Patient)
                .Include(x => x.Doctor).ThenInclude(d => d.Specialty)
                .Include(x => x.Doctor).ThenInclude(d => d.Assets)
                .FirstAsync(x => x.Id == id, ct);

            return Ok(new ApiResponse<object>(200, "Dispensación actualizada", MapPrescription(refreshed)));
        }

        return BadRequest(new ApiResponse<object>(400,
            "Request inválido. Envíe Quantity (farmacia), PharmacyId+Quantity (admin/secretary) o CurrentDispensations (patient)."));
    }
}
