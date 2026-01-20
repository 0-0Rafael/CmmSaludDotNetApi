using CmmSalud.Api.Common;
using CmmSalud.Api.Data;
using CmmSalud.Api.Domain.Entities;
using CmmSalud.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/payments")]
[Authorize]
public sealed class PaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public PaymentsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? patientId, CancellationToken ct)
    {
        var q = _db.Payments.AsNoTracking().Include(p => p.Appointment).AsQueryable();
        if (patientId is not null) q = q.Where(p => p.PatientId == patientId);

        var items = await q.OrderByDescending(p => p.CreatedAt).Take(500).ToListAsync(ct);
        return Ok(new ApiResponse<object>(200, "OK", items));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Payment req, CancellationToken ct)
    {
        req.Id = Guid.NewGuid();
        req.Status = PaymentStatus.pending;
        req.TransactionId = $"TX-{Guid.NewGuid():N}".ToUpperInvariant();

        _db.Payments.Add(req);
        await _db.SaveChangesAsync(ct);

        return Ok(new ApiResponse<object>(200, "Pago creado", req));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Payment req, CancellationToken ct)
    {
        var entity = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound(new ApiResponse<object>(404, "No encontrado"));

        entity.Status = req.Status;
        entity.PaymentMethod = req.PaymentMethod;
        entity.PaymentType = req.PaymentType;
        entity.Amount = req.Amount;
        entity.Currency = req.Currency;
        entity.Notes = req.Notes;

        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Actualizado", entity));
    }

    [HttpPost("simulate")]
    public IActionResult Simulate([FromBody] object payload)
    {
        // Simulación para testing (como el doc)
        var data = new
        {
            ok = true,
            simulated = true,
            transactionId = $"SIM-{Guid.NewGuid():N}".ToUpperInvariant(),
            payload
        };
        return Ok(new ApiResponse<object>(200, "Simulación OK", data));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] Guid? patientId, CancellationToken ct)
    {
        var q = _db.Payments.AsNoTracking().AsQueryable();
        if (patientId is not null) q = q.Where(p => p.PatientId == patientId);

        var payments = await q.OrderByDescending(p => p.CreatedAt).Take(200).ToListAsync(ct);

        var summary = new
        {
            totalPayments = payments.Count,
            totalAmount = payments.Sum(p => p.Amount),
            totalRefunded = payments.Where(p => p.Status == PaymentStatus.refunded).Sum(p => p.Amount),
            successfulPayments = payments.Count(p => p.Status == PaymentStatus.completed),
            failedPayments = payments.Count(p => p.Status == PaymentStatus.failed),
            pendingPayments = payments.Count(p => p.Status == PaymentStatus.pending)
        };

        var monthlyStats = payments
            .GroupBy(p => p.CreatedAt.ToString("yyyy-MM"))
            .Select(g => new { month = g.Key, totalAmount = g.Sum(x => x.Amount), paymentCount = g.Count() })
            .OrderByDescending(x => x.month)
            .ToList();

        return Ok(new ApiResponse<object>(200, "OK", new { payments, summary, monthlyStats }));
    }

    public sealed record RefundRequest(decimal Amount, string Reason);

    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id, [FromBody] RefundRequest req, CancellationToken ct)
    {
        var payment = await _db.Payments.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return NotFound(new ApiResponse<object>(404, "No encontrado"));

        payment.Status = PaymentStatus.refunded;
        payment.Refunds.Add(new PaymentRefund { Amount = req.Amount, Reason = req.Reason, PaymentId = id });

        await _db.SaveChangesAsync(ct);
        return Ok(new ApiResponse<object>(200, "Reembolso OK", payment));
    }

    [HttpPost("process/{paymentId:guid}")]
    public async Task<IActionResult> Process(Guid paymentId, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return NotFound(new ApiResponse<object>(404, "No encontrado"));

        payment.Status = PaymentStatus.completed;
        payment.PaymentDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(new ApiResponse<object>(200, "Procesado", payment));
    }
}
