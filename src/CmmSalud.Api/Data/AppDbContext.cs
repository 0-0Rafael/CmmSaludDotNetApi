using CmmSalud.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CmmSalud.Api.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Specialty> Specialties => Set<Specialty>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<Pharmacy> Pharmacies => Set<Pharmacy>();
    public DbSet<PrescriptionDispensation> PrescriptionDispensations => Set<PrescriptionDispensation>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();
    public DbSet<MedicalHistory> MedicalHistories => Set<MedicalHistory>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // ✅ NUEVO
    public DbSet<DoctorAsset> DoctorAssets => Set<DoctorAsset>();

    public override int SaveChanges()
    {
        TouchUpdatedAt();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchUpdatedAt();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void TouchUpdatedAt()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified && e.Entity is BaseEntity);

        foreach (var entry in entries)
            ((BaseEntity)entry.Entity).UpdatedAt = DateTime.UtcNow;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // -------------------------
        // ✅ MedicalHistory (FORZAR NOMBRE DE TABLA REAL)
        // -------------------------
        modelBuilder.Entity<MedicalHistory>(e =>
        {
            e.ToTable("MedicalHistory");
        });

        // -------------------------
        // ✅ DoctorAsset (tabla DoctorAssets)
        // -------------------------
        modelBuilder.Entity<DoctorAsset>(e =>
        {
            e.ToTable("DoctorAssets"); // tu tabla en SQL

            e.HasIndex(x => x.DoctorId).IsUnique();

            e.Property(x => x.SignaturePath).HasMaxLength(300);
            e.Property(x => x.SealPath).HasMaxLength(300);

            e.HasOne(x => x.Doctor)
                .WithOne(d => d.Assets)
                .HasForeignKey<DoctorAsset>(x => x.DoctorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // -------------------------
        // User
        // -------------------------
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Role).HasConversion<string>();
            e.Property(x => x.Email).HasMaxLength(256);

            e.HasOne(x => x.Patient)
                .WithOne(x => x.User)
                .HasForeignKey<Patient>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Doctor)
                .WithOne(x => x.User)
                .HasForeignKey<Doctor>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Pharmacy)
                .WithOne()
                .HasForeignKey<Pharmacy>(x => x.Id)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.RefreshTokens)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // -------------------------
        // Patient
        // -------------------------
        modelBuilder.Entity<Patient>(e =>
        {
            e.HasIndex(x => x.DocumentId).IsUnique();
            e.Property(x => x.DocumentId).HasMaxLength(30);
            e.Property(x => x.FirstName).HasMaxLength(80);
            e.Property(x => x.LastName).HasMaxLength(80);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Address).HasMaxLength(250);
        });

        // -------------------------
        // Doctor
        // -------------------------
        modelBuilder.Entity<Doctor>(e =>
        {
            e.HasIndex(x => x.DocumentId).IsUnique();
            e.HasIndex(x => x.LicenseNumber).IsUnique();

            e.Property(x => x.DocumentId).HasMaxLength(30);
            e.Property(x => x.LicenseNumber).HasMaxLength(60);
            e.Property(x => x.FirstName).HasMaxLength(80);
            e.Property(x => x.LastName).HasMaxLength(80);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.ConsultationFee).HasPrecision(18, 2);

            e.HasOne(x => x.Specialty)
                .WithMany(s => s.Doctors)
                .HasForeignKey(x => x.SpecialtyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // -------------------------
        // Specialty
        // -------------------------
        modelBuilder.Entity<Specialty>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
        });

        // -------------------------
        // Appointment
        // -------------------------
        modelBuilder.Entity<Appointment>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Fee).HasPrecision(18, 2);

            e.HasOne(x => x.Patient)
                .WithMany(p => p.Appointments)
                .HasForeignKey(x => x.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Doctor)
                .WithMany(d => d.Appointments)
                .HasForeignKey(x => x.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // -------------------------
        // Prescription
        // -------------------------
        modelBuilder.Entity<Prescription>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.DigitalSignature).HasMaxLength(200);

            e.HasOne(x => x.Patient)
                .WithMany(p => p.Prescriptions)
                .HasForeignKey(x => x.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Doctor)
                .WithMany(d => d.Prescriptions)
                .HasForeignKey(x => x.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // -------------------------
        // Pharmacy
        // -------------------------
        modelBuilder.Entity<Pharmacy>(e =>
        {
            e.HasIndex(x => x.LicenseNumber).IsUnique();

            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.LicenseNumber).HasMaxLength(450).IsRequired();

            e.Property(x => x.Address).HasMaxLength(250).IsRequired();
            e.Property(x => x.City).HasMaxLength(120).IsRequired();
            e.Property(x => x.State).HasMaxLength(120).IsRequired();
            e.Property(x => x.ZipCode).HasMaxLength(30);

            e.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();

            e.Property(x => x.OperatingHours).HasMaxLength(200);
        });

        // -------------------------
        // Dispensation
        // -------------------------
        modelBuilder.Entity<PrescriptionDispensation>(e =>
        {
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.QuantityDispensed).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>();
        });

        // -------------------------
        // Payment
        // -------------------------
        modelBuilder.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.PaymentMethod).HasConversion<string>();
            e.Property(x => x.PaymentType).HasConversion<string>();

            e.HasOne(x => x.Patient)
                .WithMany()
                .HasForeignKey(x => x.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // -------------------------
        // RefreshToken
        // -------------------------
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128);

            e.HasOne(x => x.User)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
