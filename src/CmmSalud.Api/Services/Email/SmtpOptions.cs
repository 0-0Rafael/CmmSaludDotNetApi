namespace CmmSalud.Api.Services.Email;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 465;
    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "CMM Salud";

    public string ToEmail { get; set; } = "";
    public string ToName { get; set; } = "Contacto";
}
