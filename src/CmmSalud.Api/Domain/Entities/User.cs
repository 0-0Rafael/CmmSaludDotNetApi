using System;
using System.Collections.Generic;
using CmmSalud.Api.Domain.Enums;

namespace CmmSalud.Api.Domain.Entities;

public sealed class User : BaseEntity
{
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLogin { get; set; }

    public Patient? Patient { get; set; }
    public Doctor? Doctor { get; set; }

    // ✅ Pharmacy por PK compartida (Users.Id == Pharmacies.Id)
    public Pharmacy? Pharmacy { get; set; }

    // ✅ 1:N RefreshTokens (importante para el WithMany)
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
