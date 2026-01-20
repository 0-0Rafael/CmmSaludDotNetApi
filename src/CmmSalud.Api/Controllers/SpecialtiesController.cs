using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using CmmSalud.Api.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/specialties")]
public sealed class SpecialtiesController : ControllerBase
{
    private readonly AppDbContext _db;

    public SpecialtiesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await _db.Specialties.OrderBy(s => s.Name).ToListAsync(ct);
        return Ok(new ApiResponse<object>(200, "OK", list));
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Specialty req, CancellationToken ct)
    {
        req.Id = Guid.NewGuid();
        _db.Specialties.Add(req);
        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Creada", req));
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Specialty req, CancellationToken ct)
    {
        var entity = await _db.Specialties.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));

        entity.Name = req.Name;
        entity.Description = req.Description;
        entity.IsActive = req.IsActive;

        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Actualizada", entity));
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await _db.Specialties.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrada"));
        _db.Specialties.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Eliminada"));
    }
}
