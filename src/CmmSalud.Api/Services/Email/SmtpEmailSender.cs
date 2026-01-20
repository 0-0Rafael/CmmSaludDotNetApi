using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace CmmSalud.Api.Services.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _opt;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IOptions<SmtpOptions> opt, ILogger<SmtpEmailSender> log)
    {
        _opt = opt.Value;
        _log = log;

        // DEBUG SAFE: no imprime password completo
        var passLen = string.IsNullOrWhiteSpace(_opt.Password) ? 0 : _opt.Password.Replace(" ", "").Trim().Length;
        _log.LogInformation("SMTP CFG => Host={Host} Port={Port} User={User} UseStartTls={Tls} PassLen={Len}",
            _opt.Host, _opt.Port, _opt.Username, _opt.UseStartTls, passLen);
    }


    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        ValidateConfig();

        var fromEmail = string.IsNullOrWhiteSpace(_opt.FromEmail) ? _opt.Username : _opt.FromEmail;
        var fromName = string.IsNullOrWhiteSpace(_opt.FromName) ? "CMM Salud" : _opt.FromName;

        using var mail = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };

        mail.To.Add(new MailAddress(toEmail));

        using var client = BuildClient();

        _log.LogInformation("📨 SMTP => Enviando a {To}. Host={Host}:{Port}", toEmail, _opt.Host, _opt.Port);
        await client.SendMailAsync(mail);
    }

    public async Task SendContactEmailAsync(string name, string fromEmail, string message, CancellationToken ct = default)
    {
        ValidateConfig();

        var toEmail = string.IsNullOrWhiteSpace(_opt.ToEmail) ? _opt.Username : _opt.ToEmail;

        var safeName = (name ?? "").Trim();
        var safeFrom = (fromEmail ?? "").Trim();
        var safeMsg = (message ?? "").Trim();

        var subject = $"📩 Contacto CMM Salud - {safeName}";
        var html =
$@"
<div style='font-family: Arial, sans-serif; line-height:1.5'>
  <h2>Nuevo mensaje de contacto</h2>
  <p><b>Nombre:</b> {WebUtility.HtmlEncode(safeName)}</p>
  <p><b>Email:</b> {WebUtility.HtmlEncode(safeFrom)}</p>
  <hr />
  <p style='white-space: pre-wrap'>{WebUtility.HtmlEncode(safeMsg)}</p>
  <hr />
  <p style='color:#666;font-size:12px'>Enviado desde el formulario de contacto de CMM Salud</p>
</div>
";

        var realFromEmail = string.IsNullOrWhiteSpace(_opt.FromEmail) ? _opt.Username : _opt.FromEmail;
        var realFromName = string.IsNullOrWhiteSpace(_opt.FromName) ? "CMM Salud" : _opt.FromName;

        using var mail = new MailMessage
        {
            From = new MailAddress(realFromEmail, realFromName),
            Subject = subject,
            Body = html,
            IsBodyHtml = true
        };

        mail.To.Add(new MailAddress(toEmail));

        // ✅ Reply-To = correo del usuario (para responder fácil)
        if (!string.IsNullOrWhiteSpace(safeFrom) && safeFrom.Contains("@"))
        {
            mail.ReplyToList.Add(new MailAddress(safeFrom));
        }

        using var client = BuildClient();

        _log.LogInformation("📨 Contact => Enviando a {To}. ReplyTo={ReplyTo}", toEmail, safeFrom);
        await client.SendMailAsync(mail);
    }

    private SmtpClient BuildClient()
    {
        var user = (_opt.Username ?? "").Trim();
        var pwd = (_opt.Password ?? "").Replace(" ", "").Trim();

        return new SmtpClient(_opt.Host, _opt.Port)
        {
            EnableSsl = _opt.UseStartTls, // 587 = STARTTLS
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(user, pwd)
        };
    }


    private void ValidateConfig()
    {
        if (string.IsNullOrWhiteSpace(_opt.Host))
            throw new InvalidOperationException("SMTP Host no está configurado (Smtp:Host).");

        if (_opt.Port <= 0)
            throw new InvalidOperationException("SMTP Port inválido (Smtp:Port).");

        if (string.IsNullOrWhiteSpace(_opt.Username))
            throw new InvalidOperationException("SMTP Username no está configurado (Smtp:Username).");

        if (string.IsNullOrWhiteSpace(_opt.Password))
            throw new InvalidOperationException("SMTP Password no está configurado (Smtp:Password).");
    }
}
