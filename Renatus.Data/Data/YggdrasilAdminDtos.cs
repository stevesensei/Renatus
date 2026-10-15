namespace Renatus.Host.Data;

public sealed record YggdrasilAdminConfigDto(
    bool AuthServerEnabled,
    string UuidAlgorithm,
    int AccessTokenExpireHours,
    int RefreshTokenExpireHours,
    int JoinSessionTtlSeconds,
    int MaxProfileLookupCount,
    int MaxActiveSessions,
    int AuthenticationRateLimitMilliseconds,
    bool NonEmailLogin,
    bool UsernameCheck);

public sealed record UpdateYggdrasilAdminConfigRequest(
    bool AuthServerEnabled,
    string UuidAlgorithm,
    int AccessTokenExpireHours,
    int RefreshTokenExpireHours,
    int JoinSessionTtlSeconds,
    int MaxProfileLookupCount,
    int MaxActiveSessions,
    int AuthenticationRateLimitMilliseconds,
    bool NonEmailLogin,
    bool UsernameCheck);
