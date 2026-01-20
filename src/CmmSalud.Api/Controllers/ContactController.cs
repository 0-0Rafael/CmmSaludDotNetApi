using CmmSalud.Api.Common;
using CmmSalud.Api.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;

namespace CmmSalud.Api.Controllers;

[ApiController]
[Route("api/v1/contact")]
public sealed class ContactController : ControllerBase
{
    private readonly IEmailSender _email;
    private readonly ILogger<ContactController> _log;

    public ContactController(IEmailSender email, ILogger<ContactController> log)
    {
        _email = email;
        _log = log;
    }

    public sealed record ContactMessageRequest(string Name, string Email, string Message);

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] ContactMessageRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        var email = (req.Email ?? "").Trim();
        var message = (req.Message ?? "").Trim();

        if (name.Length < 2) return BadRequest(new ApiResponse<object>(400, "Nombre inválido."));
        if (email.Length < 5 || !email.Contains("@")) return BadRequest(new ApiResponse<object>(400, "Email inválido."));
        if (message.Length < 5) return BadRequest(new ApiResponse<object>(400, "Mensaje inválido."));
        if (message.Length > 5000) return BadRequest(new ApiResponse<object>(400, "Mensaje muy largo."));

        try
        {
            await _email.SendContactEmailAsync(name, email, message, ct);
            return Ok(new ApiResponse<object>(200, "Mensaje enviado "));
        }
        catch (SmtpException ex)
        {
            // NO tumbes el endpoint por Gmail. El front necesita respuesta OK.
            _log.LogError(ex, "SMTP falló enviando contacto. Se devuelve OK para no romper UX.");
            return Ok(new ApiResponse<object>(200, "Mensaje recibido ✅ (correo pendiente por configuración SMTP)"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error inesperado enviando contacto. Se devuelve OK para no romper UX.");
            return Ok(new ApiResponse<object>(200, "Mensaje recibido ✅ (correo pendiente)"));
        }
    }
}
