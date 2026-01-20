using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using CmmSalud.Api.Domain.Entities;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/doctors")]
[Authorize(Roles = "admin")]
public sealed class DoctorAssetsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public DoctorAssetsController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // POST /api/v1/doctors/{doctorId}/stamp
    // Recibe: multipart/form-data con "file"
    [HttpPost("{doctorId:guid}/stamp")]
    [RequestSizeLimit(10_000_000)] // 10MB
    public async Task<IActionResult> UploadStamp(Guid doctorId, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiResponse<object>(400, "Archivo inválido."));

        var doctorExists = await _db.Doctors.AnyAsync(d => d.Id == doctorId, ct);
        if (!doctorExists)
            return NotFound(new ApiResponse<object>(404, "Doctor no encontrado."));

        // Validación simple de extensión
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".png", ".jpg", ".jpeg", ".webp" };
        if (!allowed.Contains(ext))
            return BadRequest(new ApiResponse<object>(400, "Formato inválido. Usa PNG/JPG/WEBP."));

        // Ruta: wwwroot/uploads/doctors/{doctorId}/stamp_xxx.png
        var relDir = Path.Combine("uploads", "doctors", doctorId.ToString());
        var absDir = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), relDir);

        Directory.CreateDirectory(absDir);

        var fileName = $"stamp_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
        var absPath = Path.Combine(absDir, fileName);

        await using (var stream = System.IO.File.Create(absPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Path para servirlo por StaticFiles
        var relPath = "/" + Path.Combine(relDir, fileName).Replace("\\", "/");

        // Guardar en DoctorAsset (si ya lo tienes)
        var asset = await _db.DoctorAssets.FirstOrDefaultAsync(x => x.DoctorId == doctorId, ct);
        if (asset == null)
        {
            asset = new DoctorAsset
            {
                Id = Guid.NewGuid(),
                DoctorId = doctorId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.DoctorAssets.Add(asset);
        }

        asset.SealPath = relPath;      // 👈 sello
        asset.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Puedes devolver URL absoluta o relativa; yo devuelvo relativa
        return Ok(new ApiResponse<object>(200, "Sello actualizado", new { stampUrl = relPath }));
    }
}
