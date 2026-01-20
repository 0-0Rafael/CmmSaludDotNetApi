using System;
using CmmSalud.Api.Domain.Entities;

namespace CmmSalud.Api.Domain.Entities;

public sealed class RefreshToken : BaseEntity
{
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    // ✅ FK REAL en tu tabla RefreshTokens
    public Guid UserId { get; set; }

    // ✅ UNA sola navegación (evita UserId1)
    public User User { get; set; } = null!;

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
