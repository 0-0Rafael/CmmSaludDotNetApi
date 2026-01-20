using System.Text;
using CmmSalud.Api.Data;
using CmmSalud.Api.Middleware;
using CmmSalud.Api.Services.Auth;
using CmmSalud.Api.Services.Security;
using CmmSalud.Api.Services.Email;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Controllers + Swagger
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new CmmSalud.Api.Common.Json.DateOnlyJsonConverter());
        o.JsonSerializerOptions.Converters.Add(new CmmSalud.Api.Common.Json.TimeOnlyJsonConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

// ✅ SMTP (Email)
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// DbContext (SqlServer default)
var provider = builder.Configuration["DatabaseProvider"]?.Trim() ?? "SqlServer";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        var cs = builder.Configuration.GetConnectionString("SqlServer");
        options.UseSqlServer(cs);
        return;
    }

    throw new InvalidOperationException("DatabaseProvider no soportado. Usa SqlServer o configura Oracle.");
});

// Services
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddTransient<ExceptionHandlingMiddleware>();

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

static bool IsNgrokOrigin(string? origin)
{
    if (string.IsNullOrWhiteSpace(origin)) return false;
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

    var host = uri.Host.ToLowerInvariant();
    return host.EndsWith(".ngrok-free.app") || host.EndsWith(".ngrok.app") || host.EndsWith(".ngrok.io");
}

builder.Services.AddCors(opt =>
{
    opt.AddPolicy("Default", policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            return;
        }

        policy
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin)) return false;

                if (allowedOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                    return true;

                if (IsNgrokOrigin(origin)) return true;

                return false;
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// JWT Auth
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
jwt.Issuer = string.IsNullOrWhiteSpace(jwt.Issuer) ? "CmmSalud" : jwt.Issuer;
jwt.Audience = string.IsNullOrWhiteSpace(jwt.Audience) ? "CmmSaludClient" : jwt.Audience;

if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres.");

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(15),

            RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Middleware
app.UseSwagger();
app.UseSwaggerUI();

// ✅ (1) FORZAR HEADERS CORS PARA /uploads (aunque lo sirva otro middleware)
// Esto mata el error sí o sí.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/uploads"))
    {
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET,OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "*";
            ctx.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            ctx.Response.Headers["Vary"] = "Origin";
            return Task.CompletedTask;
        });
    }

    await next();
});

// ✅ (2) Servir uploads desde carpeta ./uploads (ContentRoot/uploads)
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

// IMPORTANTE: este va ANTES del UseStaticFiles() default
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        ctx.Context.Response.Headers["Access-Control-Allow-Methods"] = "GET,OPTIONS";
        ctx.Context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        ctx.Context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        ctx.Context.Response.Headers["Vary"] = "Origin";
    }
});

// ✅ (3) Static files normal (wwwroot)
app.UseStaticFiles();

app.UseCors("Default");
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Auto-migrate + seed (DEV)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DbSeeder.SeedAsync(db);
}

app.Run();
