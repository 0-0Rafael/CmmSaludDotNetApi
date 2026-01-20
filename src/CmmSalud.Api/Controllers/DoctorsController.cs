using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CmmSalud.Api.Data;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public sealed class DoctorsController : ControllerBase
{
    private readonly AppDbContext _db;

    public DoctorsController(AppDbContext db)
    {
        _db = db;
    }

    // GET: /api/v1/doctors?page=1&pageSize=12&isActive=true&specialtyId=...
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? specialtyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        [FromQuery] bool? isActive = true,
        CancellationToken ct = default)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 12 : pageSize;

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var query = _db.Doctors
            .AsNoTracking()
            .Include(d => d.Specialty)
            .Include(d => d.User)
            .Include(d => d.Assets)
            .AsQueryable();

        // ✅ Filtro de activos: se aplica al User (porque Doctor no tiene IsActive)
        if (isActive.HasValue)
            query = query.Where(d => d.User != null && d.User.IsActive == isActive.Value);

        if (specialtyId.HasValue)
            query = query.Where(d => d.SpecialtyId == specialtyId.Value);

        var total = await query.CountAsync(ct);

        // ✅ Proyección SOLO con campos existentes
        var rows = await query
            .OrderBy(d => d.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DoctorRow
            {
                Id = d.Id,
                FirstName = d.FirstName,
                LastName = d.LastName,
                Phone = d.Phone,

                SpecialtyId = d.SpecialtyId,
                SpecialtyName = d.Specialty != null ? d.Specialty.Name : null,

                Email = d.User != null ? d.User.Email : null,

                // Firma/Sello desde Assets
                SignaturePath = d.Assets != null ? d.Assets.SignaturePath : null,
                SealPath = d.Assets != null ? d.Assets.SealPath : null,

                // ✅ Si el front necesita isActive, lo mandamos desde User
                IsActive = d.User != null && d.User.IsActive
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new DoctorListItemDto
        {
            Id = r.Id,
            FirstName = r.FirstName,
            LastName = r.LastName,
            Email = r.Email,
            Phone = r.Phone,

            SpecialtyId = r.SpecialtyId,
            SpecialtyName = r.SpecialtyName,

            IsActive = r.IsActive,

            SignatureUrl = BuildPublicUrl(baseUrl, r.SignaturePath),
            SealUrl = BuildPublicUrl(baseUrl, r.SealPath),
        }).ToList();

        var response = new ApiResponse<PagedResult<DoctorListItemDto>>
        {
            StatusCode = 200,
            Message = "OK",
            Data = new PagedResult<DoctorListItemDto>
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            }
        };

        return Ok(response);
    }

    private static string? BuildPublicUrl(string baseUrl, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Uri.TryCreate(path, UriKind.Absolute, out _))
            return path;

        var clean = path.TrimStart('/');
        return $"{baseUrl}/{clean}";
    }

    private sealed class DoctorRow
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string? Email { get; set; }
        public string Phone { get; set; } = "";
        public Guid SpecialtyId { get; set; }
        public string? SpecialtyName { get; set; }

        public bool IsActive { get; set; }

        public string? SignaturePath { get; set; }
        public string? SealPath { get; set; }
    }

    public sealed class DoctorListItemDto
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string? Email { get; set; }
        public string Phone { get; set; } = "";

        public Guid SpecialtyId { get; set; }
        public string? SpecialtyName { get; set; }

        public bool IsActive { get; set; }

        public string? SignatureUrl { get; set; }
        public string? SealUrl { get; set; }
    }

    public sealed class PagedResult<T>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<T> Items { get; set; } = new();
    }

    public sealed class ApiResponse<T>
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = "";
        public T? Data { get; set; }
    }
}
