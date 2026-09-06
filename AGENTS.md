# AGENTS.md

## Project

Renatus is a single-project ASP.NET Core 8 API (`Renatus.Host`). It serves **two separate API surfaces** with **two separate token systems** — do not mix them:

1. **Yggdrasil protocol** (`Controller/Yggdrasil/*`) — a Minecraft authlib-injector-compatible auth server (authserver, sessionserver, profiles, textures).
2. **Frontend API** (`Controller/{Auth,Player,Texture}Controller.cs`) — REST for the web client (account, players, textures), protected by the app's own Bearer JWT.

## Build & run

- SDK pinned to 8.0.424 (`global.json`, rollForward latestMinor).
- **Do not build `Renatus.slnx`** — this SDK's CLI cannot parse the new XML solution format (`dotnet build Renatus.slnx` → `MSB4068`). Build/run the csproj directly:
  - `dotnet build Renatus.Host/Renatus.Host.csproj`
  - `dotnet run --project Renatus.Host` → http://localhost:5294 (launch profile `http`; browser opens `/scalar`)
- EF migrations: `dotnet ef migrations add <Name> --project Renatus.Host`. Installed `dotnet ef` tool is 10.0.7; project packages are EF Core 8.0.30.
- Docker: `docker compose up --build` (image `renatus.host`, EXPOSE 8080/8081). `compose.yaml` builds only the app — it does **not** run Postgres/Redis.
- No tests, no CI, no linter/`.editorconfig`.

## Runtime prerequisites (startup hard requirements)

- **PostgreSQL + Redis are required.** Connection strings live in `Renatus.Host/appsettings.json` and point at a remote host with real (committed) credentials. For new secrets prefer env vars / user-secrets over more hardcoded config.
- On startup `Program.cs` runs `DbSeed.InitializeAsync`: applies pending migrations automatically, then seeds a test account **only if the DB had zero applied migrations** (`test` / `password`, player `TestPlayer`).
- Missing `Auth:JwtSigningKey` → app throws at startup.
- `Yggdrasil:SigningPrivateKeyPem` defaults to empty → an ephemeral RSA-2048 key is generated per startup, so **all previously issued Yggdrasil access tokens and skin signatures break on every restart**. Set it for any persistent deployment.

## Yggdrasil surface conventions

- Routes: `api/yggdrasil` (root metadata), `api/yggdrasil/authserver/{authenticate,refresh,validate,invalidate,signout}`, `api/yggdrasil/sessionserver/session/minecraft/{join,hasJoined,profile/{id}}`, `api/yggdrasil/api/...`, `api/yggdrasil/textures/{id:N}.png`.
- Protocol errors must match Mojang's shape: throw `YggdrasilException(statusCode, "ForbiddenOperationException"|"IllegalArgumentException", message, cause?)`. Controllers extending `YggdrasilControllerBase` catch it via `YggdrasilError(...)`. Success uses plain 200/204 JSON.
- Yggdrasil access tokens are **custom RS256 JWTs** hand-built in `YggdrasilService` (`{sub: clientId, version, iat, exp}`), not the app's Bearer JWT. Validated against the `YggdrasilClients` row including its `Version` counter; `Signout`/`Invalidate` bump `Version` to revoke.
- All responses carry header `X-Authlib-Injector-API-Location: /api/yggdrasil/` (set in `Program.cs` middleware).

## Frontend surface conventions

- Every response is wrapped in `GlobalResponse<T>` `{code, message, data}`: 10000 ok / 10001 business error / 10010 internal. Domain errors are thrown as `AuthException` / `PlayerException` / `TextureException` and caught in-controller → `ResultError`; **HTTP status stays 200, errors live in the envelope**, not HTTP codes.
- Bearer JWT: HMAC-SHA256, `Auth:*` config section. Roles `Admin`/`Player` via `ClaimTypes.Role`; policies registered as `nameof(UserRole.Admin/Player)`; authorize with `[Authorize(Roles = "Admin")]`.
- Password flow: POST `/api/auth/session-key` stores an RSA private key in Redis (TTL `SessionKeyTtlSeconds`, consumed once via `StringGetDelete`), returns the public key; login/register then send `"{unixTs}.{password}"` encrypted RSA-OAEP-SHA256 (timestamp skew tolerance `TimestampSkewSeconds`).
- Passwords hashed with BCrypt (`BCrypt.Net-Next`); `BCrypt.Verify` throws `SaltParseException` on legacy/invalid hashes — catch and treat as invalid (see `AuthService.VerifyPassword`).

## Data layer

- EF Core 8 + Npgsql. Migrations in `Renatus.Host/Data/Migrations`.
- `CreatedAt`/`UpdatedAt` are set by the `AppDbContext.SaveChangesAsync` override (manual per-entity loops), not EF conventions. Bulk `ExecuteUpdateAsync` bypasses it.
- All username/email/player-name lookups must use normalized columns: `YggdrasilFormat.Normalize()` = `Trim() + ToUpperInvariant()`. Unique indexes are on `Normalized*`.
- Enum properties persist as strings via `.HasConversion<string>()`.
- Textures are stored as base64 PNG in `Texture.DataBase64` and served from DB; session-profile texture URLs are built from `Yggdrasil:PublicBaseUrl`.
- Redis keys: `auth:session-key:{guid}`, `yggdrasil:join:{normalizedName}:{sha256(serverId)}`.

## Style

- `sealed` classes, primary constructors, records for DTOs; constructor DI; services registered in `Program.cs` (scoped, except `IConnectionMultiplexer`/`ICacheService`/`YggdrasilSigningKey` singletons).
- Layout: `Controller/` (Yggdrasil subfolder), `Service/` (+`Cache/`), `Entity/`, `Data/` (DbContext, `*Dtos.cs`, `Config/*Options.cs`), `Utils/` (per-domain exceptions, formats). DTOs are positional records in `Data/*Dtos.cs`.
- Comments may be Chinese; identifiers English. Commit messages are terse Chinese one-liners (e.g. `换用scalar`).
- Rider is the IDE (`.idea/`, `obj/rider.project.restore.info`); `bin/`, `obj/` gitignored.

## Stale bits

- `Renatus.Host/Renatus.Host.http` is a template leftover referencing the nonexistent `/weatherforecast/` endpoint.
