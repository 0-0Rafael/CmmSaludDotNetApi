using BCrypt.Net;
using CmmSalud.Api.Domain.Entities;
using CmmSalud.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // 1) Specialties
        if (!await db.Specialties.AnyAsync(ct))
        {
            db.Specialties.AddRange(new[]
            {
                new Specialty { Name = "Medicina General", Description = "Atención primaria", IsActive = true },
                new Specialty { Name = "Cardiología", Description = "Corazón y sistema circulatorio", IsActive = true },
                new Specialty { Name = "Pediatría", Description = "Salud infantil", IsActive = true },
                new Specialty { Name = "Dermatología", Description = "Piel", IsActive = true },
            });
            await db.SaveChangesAsync(ct);
        }

        // 2) Admin user
        var adminEmail = "admin@cmm.local";
        if (!await db.Users.AnyAsync(u => u.Email == adminEmail, ct))
        {
            db.Users.Add(new User
            {
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!", 11),
                Role = UserRole.admin,
                IsActive = true
            });
            await db.SaveChangesAsync(ct);
        }

        // 3) Secretary
        var secEmail = "secretary@cmm.local";
        if (!await db.Users.AnyAsync(u => u.Email == secEmail, ct))
        {
            db.Users.Add(new User
            {
                Email = secEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Secretary123!", 11),
                Role = UserRole.secretary,
                IsActive = true
            });
            await db.SaveChangesAsync(ct);
        }

        // 4) Doctor
        var docEmail = "doctor@cmm.local";
        if (!await db.Users.AnyAsync(u => u.Email == docEmail, ct))
        {
            var specialtyId = await db.Specialties.Select(s => s.Id).FirstAsync(ct);

            var user = new User
            {
                Email = docEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Doctor123!", 11),
                Role = UserRole.doctor,
                IsActive = true
            };

            user.Doctor = new Doctor
            {
                User = user,
                DocumentId = "DOC-0001",
                FirstName = "Juan",
                LastName = "Pérez",
                LicenseNumber = "MED-12345",
                Phone = "809-000-0000",
                ConsultationFee = 50,
                SpecialtyId = specialtyId
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // 5) Patient
        var patEmail = "patient@cmm.local";
        if (!await db.Users.AnyAsync(u => u.Email == patEmail, ct))
        {
            var user = new User
            {
                Email = patEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Patient123!", 11),
                Role = UserRole.patient,
                IsActive = true
            };

            user.Patient = new Patient
            {
                User = user,
                DocumentId = "PAT-0001",
                FirstName = "María",
                LastName = "Gómez",
                Phone = "809-111-2222",
                Address = "Santo Domingo",
                DateOfBirth = new DateOnly(2000, 1, 1)
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
    }
}
