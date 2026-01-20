# CMM Salud API - .NET 8 (Web API + EF Core + JWT)

Este backend está hecho para **conectarse al frontend** que me pasaste (usa `NEXT_PUBLIC_API_URL` con `/api/v1`).

## 1) Requisitos
- .NET SDK 8
- SQL Server Express (tu MSI\SQLEXPRESS) **o** Oracle (si quieres migrar después)
- (Recomendado) EF Core tools:
```bash
dotnet tool install --global dotnet-ef
```

## 2) Configuración rápida (SQL Server Express - Windows Auth)
Edita `src/CmmSalud.Api/appsettings.json`:

- `DatabaseProvider`: `SqlServer`
- `ConnectionStrings:SqlServer`:
  - `Server=MSI\SQLEXPRESS;Database=CMMSalud;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True`

## 3) Correr el backend
```bash
cd src/CmmSalud.Api
dotnet restore
dotnet run
```

Swagger:
- http://localhost:5000/swagger  (o el puerto que te salga)

## 4) Cuentas seed (DEV)
- Admin: `admin@cmm.local` / `Admin123!`
- Secretary: `secretary@cmm.local` / `Secretary123!`
- Doctor: `doctor@cmm.local` / `Doctor123!`
- Patient: `patient@cmm.local` / `Patient123!`

## 5) Conectar el frontend
En tu frontend:
- `.env.development`:
  - `NEXT_PUBLIC_API_URL=http://localhost:5000/api/v1` (ajusta el puerto si cambia)

Luego:
```bash
npm i
npm run dev
```

## 6) Oracle (cuando quieras)
Para que compile con Oracle necesitas agregar el provider y habilitarlo:

```bash
cd src/CmmSalud.Api
dotnet add package Oracle.EntityFrameworkCore
```

Luego:
1) En `appsettings.json` pon `DatabaseProvider` en `"Oracle"` y ajusta `ConnectionStrings:Oracle`.
2) En `Program.cs` descomenta `UseOracle(...)` (está marcado ahí mismo).

> Tip: si ya corriste migraciones en SQL Server, lo normal es crear una DB nueva para Oracle y correr migraciones fresh.

## Extra: Migraciones (cuando ya esté corriendo)
Si prefieres migraciones (recomendado en proyectos serios):
```bash
cd src/CmmSalud.Api
dotnet ef migrations add InitialCreate
dotnet ef database update
```
*Ahora mismo el proyecto usa `EnsureCreated()` para que arranque sin líos.*
