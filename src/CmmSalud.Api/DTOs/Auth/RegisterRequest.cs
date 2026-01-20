namespace CmmSalud.Api.DTOs.Auth;

public sealed record RegisterRequest(
    string DocumentId,
    string FirstName,
    string LastName,
    string Phone,
    string Email,
    string DateOfBirth,
    string Address,
    string Password
);
