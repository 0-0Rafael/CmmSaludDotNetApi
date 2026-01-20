namespace CmmSalud.Api.Services.Email;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);

    // Para el formulario de contacto
    Task SendContactEmailAsync(string name, string fromEmail, string message, CancellationToken ct = default);
}
