using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/patients")]
[Authorize]
public sealed class PatientsController : ControllerBase
{
    private readonly AppDbContext _db;
    public PatientsController(AppDbContext db) => _db = db;

    // ✅ OJO: este endpoint lo está usando el doctor para buscar por cédula.
    // Antes devolvía TODO aunque mandaras documentId en query.
    // Ahora: si viene documentId, filtra; si no viene, trae todos (igual que antes).
    [Authorize(Roles = "admin,secretary,doctor")]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? documentId,
        CancellationToken ct)
    {
        var q = _db.Patients.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(documentId))
        {
            var raw = documentId.Trim();

            // Normaliza para casos tipo "402-341-61533" vs "40234161533"
            var normalized = new string(raw.Where(char.IsLetterOrDigit).ToArray());

            // Si es numérico, permitimos match "con o sin guiones/espacios" sin romper PAT-0001
            var isNumeric = normalized.All(char.IsDigit);

            if (isNumeric)
            {
                q = q.Where(p =>
                    p.DocumentId == raw ||
                    p.DocumentId == normalized ||
                    (p.DocumentId != null &&
                     p.DocumentId.Replace("-", "").Replace(" ", "") == normalized)
                );
            }
            else
            {
                // Para IDs tipo "PAT-0001" hacemos match exacto (ignorando espacios)
                q = q.Where(p => p.DocumentId == raw || p.DocumentId == normalized);
            }
        }

        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id,
                documentId = p.DocumentId,
                firstName = p.FirstName,
                lastName = p.LastName,
                phone = p.Phone,
                address = p.Address,
                dateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"),
                userId = p.UserId
            })
            .ToListAsync(ct);

        return Ok(new ApiResponse<object>(200, "OK", items));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entity = await _db.Patients.AsNoTracking()
            .Include(p => p.MedicalHistory)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrado"));
        return Ok(new ApiResponse<object>(200, "OK", entity));
    }
}
