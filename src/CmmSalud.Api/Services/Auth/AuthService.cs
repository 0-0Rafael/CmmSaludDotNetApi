using BCrypt.Net;
using CmmSalud.Api.Data;
using CmmSalud.Api.Domain.Entities;
using CmmSalud.Api.Domain.Enums;
using CmmSalud.Api.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Services.Auth;

public sealed class AuthService
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly JwtOptions _jwt;

    public AuthService(AppDbContext db, TokenService tokens, Microsoft.Extensions.Options.IOptions<JwtOptions> jwt)
    {
        _db = db;
        _tokens = tokens;
        _jwt = jwt.Value;
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> RegisterPatientAsync(
        string email,
        string password,
        string documentId,
        string firstName,
        string lastName,
        string phone,
        DateOnly dob,
        string address,
        CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var exists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
        if (exists) throw new InvalidOperationException("Ya existe un usuario con ese email.");

        var docExists = await _db.Patients.AnyAsync(p => p.DocumentId == documentId, ct);
        if (docExists) throw new InvalidOperationException("Ya existe un paciente con ese documento.");

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11),
            Role = UserRole.patient,
            IsActive = true,
            LastLogin = null
        };

        user.Patient = new Patient
        {
            DocumentId = documentId.Trim(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Phone = phone.Trim(),
            DateOfBirth = dob,
            Address = address.Trim(),
            User = user
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // Para que el response salga completito (y por consistencia con Login/Refresh)
        var createdUser = await _db.Users.AsNoTracking()
            .Include(u => u.Patient)
            .Include(u => u.Doctor).ThenInclude(d => d.Specialty)
            .Include(u => u.Pharmacy)
            .FirstAsync(u => u.Id == user.Id, ct);

        var (accessToken, _) = _tokens.CreateAccessToken(createdUser);
        var refreshToken = _tokens.CreateRefreshToken();
        await AddRefreshTokenAsync(createdUser.Id, refreshToken, ct);

        return (createdUser, accessToken, refreshToken);
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(
        string email,
        string password,
        CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.Patient)
            .Include(u => u.Doctor).ThenInclude(d => d.Specialty)
            .Include(u => u.Pharmacy)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null) throw new UnauthorizedAccessException("Credenciales inválidas.");
        if (!user.IsActive) throw new UnauthorizedAccessException("Usuario inactivo.");
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) throw new UnauthorizedAccessException("Credenciales inválidas.");

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Re-leer para que el objeto quede con la data consistente (especialmente specialty)
        var freshUser = await _db.Users.AsNoTracking()
            .Include(u => u.Patient)
            .Include(u => u.Doctor).ThenInclude(d => d.Specialty)
            .Include(u => u.Pharmacy)
            .FirstAsync(u => u.Id == user.Id, ct);

        var (accessToken, _) = _tokens.CreateAccessToken(freshUser);
        var refreshToken = _tokens.CreateRefreshToken();
        await AddRefreshTokenAsync(freshUser.Id, refreshToken, ct);

        return (freshUser, accessToken, refreshToken);
    }

    public async Task<(string AccessToken, string RefreshToken)> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = TokenService.Sha256(refreshToken);

        var stored = await _db.RefreshTokens
            .Include(rt => rt.User).ThenInclude(u => u.Patient)
            .Include(rt => rt.User).ThenInclude(u => u.Doctor).ThenInclude(d => d.Specialty)
            .Include(rt => rt.User).ThenInclude(u => u.Pharmacy)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (stored is null || !stored.IsActive)
            throw new UnauthorizedAccessException("Refresh token inválido o expirado.");

        // rotate
        stored.RevokedAt = DateTime.UtcNow;

        var newRefresh = _tokens.CreateRefreshToken();
        stored.ReplacedByTokenHash = TokenService.Sha256(newRefresh);

        await AddRefreshTokenAsync(stored.UserId, newRefresh, ct);

        var (accessToken, _) = _tokens.CreateAccessToken(stored.User);
        await _db.SaveChangesAsync(ct);

        return (accessToken, newRefresh);
    }

    private async Task AddRefreshTokenAsync(Guid userId, string refreshToken, CancellationToken ct)
    {
        var tokenHash = TokenService.Sha256(refreshToken);

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays)
        };

        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct);
    }
}
