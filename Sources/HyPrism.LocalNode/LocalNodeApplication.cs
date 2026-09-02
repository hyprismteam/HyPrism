// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using HyPrism.Core.Game.Authentication;
using HyPrism.Mesh;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;

namespace HyPrism.LocalNode;

/// <summary>
/// Builds the loopback HTTP compatibility surface used by the Hytale client
/// </summary>
public static class LocalNodeApplication
{
    private static readonly Dictionary<string, object> DefaultPresenceSettings = new()
    {
        ["allowFriendRequests"] = true,
        ["allowInvites"] = 1,
        ["allowJoin"] = true,
        ["showActivity"] = 1,
        ["showLocation"] = 1,
        ["showOnline"] = 1
    };

    /// <summary>
    /// Creates a configured web application without starting it
    /// </summary>
    public static WebApplication Build(
        LocalNodeOptions options,
        X509Certificate2 certificate,
        LocalSessionRegistry? sessions = null,
        LocalAccountStore? accounts = null,
        LocalCosmeticsCatalog? cosmetics = null,
        LocalNodeLog? log = null,
        LocalNodeProcessLifetime? processLifetime = null,
        MeshFriendService? meshFriends = null,
        MeshNetworkHost? meshNetwork = null)
    {
        sessions ??= new LocalSessionRegistry(options.Issuer);
        accounts ??= new LocalAccountStore(options.AccountDataDirectory ?? options.DataDirectory);
        cosmetics ??= new LocalCosmeticsCatalog(options.AssetsPath, log);
        meshFriends ??= new MeshFriendService(options.AccountDataDirectory ?? options.DataDirectory);
        meshNetwork ??= new MeshNetworkHost(
            options.AccountDataDirectory ?? options.DataDirectory,
            options.MeshTransport,
            log,
            friends: meshFriends);
        var socketGateway = new SocketGatewayService(meshNetwork, meshFriends, log);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(LocalNodeApplication).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(meshNetwork);
        builder.Services.AddHostedService(provider => provider.GetRequiredService<MeshNetworkHost>());
        builder.Services.AddSingleton(socketGateway);
        builder.Services.AddHostedService(provider => provider.GetRequiredService<SocketGatewayService>());
        builder.Services.Configure<JsonOptions>(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        });
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
            server.ListenLocalhost(options.Port, listener => listener.UseHttps(certificate));
        });

        var app = builder.Build();
        var journal = new RequestJournal(options.DataDirectory, options.RequestJournalPath);

        app.Use(async (context, next) =>
        {
            var remoteAddress = context.Connection.RemoteIpAddress;
            if (remoteAddress is not null && !IPAddress.IsLoopback(remoteAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var host = context.Request.Host.Host;
            if (!string.Equals(host, options.Hostname, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                && host != "127.0.0.1"
                && host != "::1")
            {
                context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            await next(context);
        });

        app.Use(async (context, next) =>
        {
            var stopwatch = Stopwatch.StartNew();
            Exception? failure = null;
            try
            {
                await next(context);
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (log is not null && ShouldLogRequest(context.Request.Path, context.Response.StatusCode))
                {
                    var message = $"{context.Request.Method} {context.Request.Path} -> "
                                  + $"{context.Response.StatusCode} ({stopwatch.ElapsedMilliseconds}ms)";
                    if (failure is not null)
                        log.Error($"{message}: {failure.GetType().Name}: {failure.Message}");
                    else if (context.Response.StatusCode >= 400)
                        log.Warning(message);
                    else
                        log.Info(message);
                }
            }
        });

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20),
            KeepAliveTimeout = TimeSpan.FromSeconds(20)
        });

        MapHealthAndSessionRoutes(app, options, sessions, accounts, processLifetime, meshNetwork, log);
        MapAccountRoutes(app, sessions, accounts, cosmetics);
        MapCompatibilityRoutes(app, options, sessions, accounts, meshFriends, meshNetwork, socketGateway, log);
        MapSocketGatewayRoute(app, sessions, socketGateway);
        MapWorldInviteRoutes(app, sessions, socketGateway);
        MapMeshControlRoutes(app, options, meshFriends, meshNetwork);

        app.MapFallback(async context =>
        {
            journal.Append(context.Request.Method, context.Request.Path, context.Request.Query.Keys);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "endpoint_not_implemented",
                message = "The Local Node recorded this request path for compatibility analysis"
            });
        });

        return app;
    }

    private static void MapMeshControlRoutes(
        WebApplication app,
        LocalNodeOptions options,
        MeshFriendService meshFriends,
        MeshNetworkHost meshNetwork)
    {
        static IResult Unauthorized() => Results.Unauthorized();

        app.MapGet("/_hyprism/v1/mesh/profiles/{profileId}/identity", async (
            HttpContext context,
            string profileId,
            CancellationToken cancellationToken) =>
        {
            if (!IsMeshControlAuthorized(context.Request, options.ControlSecret))
                return Unauthorized();

            try
            {
                return Results.Ok(await meshFriends.GetIdentityAsync(profileId, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = "invalid_profile", message = exception.Message });
            }
        });

        app.MapGet("/_hyprism/v1/mesh/profiles/{profileId}/friends", async (
            HttpContext context,
            string profileId,
            CancellationToken cancellationToken) =>
        {
            if (!IsMeshControlAuthorized(context.Request, options.ControlSecret))
                return Unauthorized();

            try
            {
                var friends = await meshFriends.GetFriendsAsync(profileId, cancellationToken);
                return Results.Ok(new { friends });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = "invalid_profile", message = exception.Message });
            }
        });

        app.MapPost("/_hyprism/v1/mesh/profiles/{profileId}/invites", async (
            HttpContext context,
            string profileId,
            MeshCreateInviteRequest body,
            CancellationToken cancellationToken) =>
        {
            if (!IsMeshControlAuthorized(context.Request, options.ControlSecret))
                return Unauthorized();

            var lifetimeMinutes = body.LifetimeMinutes ?? 10;
            if (!double.IsFinite(lifetimeMinutes) || lifetimeMinutes <= 0 || lifetimeMinutes > 24 * 60)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_lifetime",
                    message = "Invite lifetime must be between 0 and 1440 minutes"
                });
            }

            var lifetime = TimeSpan.FromMinutes(lifetimeMinutes);
            var result = await meshFriends.CreateInviteAsync(
                profileId,
                body.DisplayName,
                lifetime,
                cancellationToken);
            return MapMeshResult(result);
        });

        app.MapPost("/_hyprism/v1/mesh/profiles/{profileId}/accept", async (
            HttpContext context,
            string profileId,
            MeshAcceptInviteRequest body,
            CancellationToken cancellationToken) =>
        {
            if (!IsMeshControlAuthorized(context.Request, options.ControlSecret))
                return Unauthorized();

            var result = await meshFriends.AcceptInviteAsync(
                profileId,
                body.DisplayName,
                body.InviteToken,
                cancellationToken);
            if (result.IsSuccess)
                await meshNetwork.RefreshProfileAsync(profileId, cancellationToken);
            return MapMeshResult(result);
        });

        app.MapPost("/_hyprism/v1/mesh/profiles/{profileId}/complete", async (
            HttpContext context,
            string profileId,
            MeshCompleteInviteRequest body,
            CancellationToken cancellationToken) =>
        {
            if (!IsMeshControlAuthorized(context.Request, options.ControlSecret))
                return Unauthorized();

            var result = await meshFriends.CompleteInviteAsync(
                profileId,
                body.AcceptanceToken,
                cancellationToken);
            if (result.IsSuccess)
                await meshNetwork.RefreshProfileAsync(profileId, cancellationToken);
            return MapMeshResult(result);
        });

        app.MapGet("/_hyprism/v1/mesh/profiles/{profileId}/presence", (
            HttpContext context,
            string profileId) =>
        {
            if (!IsMeshControlAuthorized(context.Request, options.ControlSecret))
                return Unauthorized();
            return Results.Ok(new { friends = meshNetwork.GetPresence(profileId) });
        });
    }

    private static void MapHealthAndSessionRoutes(
        WebApplication app,
        LocalNodeOptions options,
        LocalSessionRegistry sessions,
        LocalAccountStore accounts,
        LocalNodeProcessLifetime? processLifetime,
        MeshNetworkHost meshNetwork,
        LocalNodeLog? log)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ready",
            server = "hyprism-local-node",
            issuer = options.Issuer,
            protocol = "https",
            hostname = options.Hostname,
            port = options.Port,
            processId = Environment.ProcessId,
            gameProcessId = processLifetime?.GameProcessId,
            meshTransportPort = meshNetwork.TransportPort
        }));

        if (processLifetime is not null && !string.IsNullOrWhiteSpace(options.ControlSecret))
        {
            app.MapPost("/_hyprism/v1/lifecycle/attach", (HttpContext context, JsonElement body) =>
            {
                if (!IsControlAuthorized(context.Request, options.ControlSecret))
                    return Results.Unauthorized();
                if (!body.TryGetProperty("gameProcessId", out var processIdElement)
                    || !processIdElement.TryGetInt32(out var processId))
                {
                    return Results.BadRequest(new { error = "gameProcessId is required" });
                }

                return processLifetime.TryAttachGameProcess(processId, app.Lifetime, out var error)
                    ? Results.Ok(new { attached = true, gameProcessId = processId })
                    : Results.Conflict(new { error });
            });

            app.MapPost("/_hyprism/v1/lifecycle/stop", (HttpContext context) =>
            {
                if (!IsControlAuthorized(context.Request, options.ControlSecret))
                    return Results.Unauthorized();
                processLifetime.Stop("Shutdown requested by launcher", app.Lifetime);
                return Results.Accepted();
            });
        }

        app.MapGet("/.well-known/jwks.json", () => Results.Ok(new { keys = sessions.GetPublicKeys() }));
        app.MapGet("/jwks.json", () => Results.Ok(new { keys = sessions.GetPublicKeys() }));

        app.MapPost("/v1/sessions", async (JsonElement body, CancellationToken cancellationToken) =>
        {
            var uuid = GetString(body, "playerUuid", "uuid");
            var name = GetString(body, "playerName", "username", "name");
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                return MissingIdentity();

            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            await TryActivateMeshAsync(meshNetwork, uuid, name, log, cancellationToken);
            return SessionResult(sessions.Renew(uuid, name, skin: profile.SkinJson));
        });

        async Task<(JsonElement Body, string? Uuid, string? Name)> ReadSessionRequest(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            var body = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
                ? await context.Request.ReadFromJsonAsync<JsonElement>(cancellationToken)
                : default;
            var uuid = GetString(body, "uuid", "playerUuid", "profileId");
            var name = GetString(body, "username", "name", "playerName");
            if ((string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                && TryGetBearerIdentity(context.Request, sessions, out var bearerUuid, out var bearerName))
            {
                if (string.IsNullOrWhiteSpace(uuid))
                    uuid = bearerUuid;
                if (string.IsNullOrWhiteSpace(name))
                    name = bearerName;
            }
            return (body, uuid, name);
        }

        async Task<IResult> CreateGameSession(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            var (body, uuid, name) = await ReadSessionRequest(context, cancellationToken);
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                return MissingIdentity();

            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            await TryActivateMeshAsync(meshNetwork, uuid, name, log, cancellationToken);
            return SessionResult(sessions.Renew(uuid, name, skin: profile.SkinJson));
        }

        async Task<IResult> CreateChildSession(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            var (body, uuid, name) = await ReadSessionRequest(context, cancellationToken);
            var profile = !string.IsNullOrWhiteSpace(uuid) && !string.IsNullOrWhiteSpace(name)
                ? await accounts.GetOrCreateAsync(uuid, name, cancellationToken)
                : null;
            var proofToken = GetBearerToken(context.Request)
                             ?? GetString(body, "sessionToken", "session_token", "identityToken", "identity_token");
            var session = !string.IsNullOrWhiteSpace(proofToken)
                ? sessions.RenewByToken(
                    proofToken,
                    GetScope(body) ?? "hytale:server",
                    skin: profile?.SkinJson)
                : null;

            if (session is null)
            {
                if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                    return Results.Unauthorized();
                session = sessions.Renew(
                    uuid,
                    name,
                    GetScope(body) ?? "hytale:server",
                    skin: profile?.SkinJson);
            }

            return SessionResult(session);
        }

        async Task<IResult> RefreshGameSession(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            var (body, uuid, name) = await ReadSessionRequest(context, cancellationToken);
            var profile = !string.IsNullOrWhiteSpace(uuid) && !string.IsNullOrWhiteSpace(name)
                ? await accounts.GetOrCreateAsync(uuid, name, cancellationToken)
                : null;
            var proofToken = GetBearerToken(context.Request)
                             ?? GetString(body, "sessionToken", "session_token", "identityToken", "identity_token");
            if (string.IsNullOrWhiteSpace(proofToken))
                return Results.Unauthorized();

            var session = sessions.RenewByToken(
                proofToken,
                GetScope(body) ?? "hytale:server hytale:client",
                skin: profile?.SkinJson);
            return session is null ? Results.Unauthorized() : SessionResult(session);
        }

        app.MapPost("/game-session", CreateGameSession);
        app.MapPost("/game-session/new", CreateGameSession);
        app.MapPost("/game-session/child", CreateChildSession);
        app.MapPost("/game-session/refresh", RefreshGameSession);

        app.MapDelete("/game-session", (HttpContext context) =>
        {
            var bearer = GetBearerToken(context.Request);
            if (!string.IsNullOrWhiteSpace(bearer))
                sessions.RemoveByToken(bearer);
            return Results.NoContent();
        });

        async Task<IResult> CreateAuthorizationGrant(
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken)
        {
            var identityToken = GetString(body, "identityToken", "identity_token", "token");
            var audience = GetString(body, "aud", "audience", "serverAudience", "server_id");
            var scope = GetScope(body);
            if (string.IsNullOrWhiteSpace(audience))
                return Results.BadRequest(new { error = "audience is required" });

            var bearer = GetBearerToken(context.Request);
            LocalProfileData? profile = null;
            if (TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            var grant = !string.IsNullOrWhiteSpace(bearer)
                ? sessions.CreateAuthorizationGrant(bearer, audience, scope, profile?.SkinJson)
                : null;
            if (grant is null && !string.IsNullOrWhiteSpace(identityToken))
                grant = sessions.CreateAuthorizationGrant(identityToken, audience, scope);
            return grant is null
                ? Results.Unauthorized()
                : Results.Ok(new
                {
                    authorizationGrant = grant,
                    expiresAt = DateTimeOffset.UtcNow.AddHours(10)
                });
        }

        app.MapPost("/game-session/authorize", CreateAuthorizationGrant);
        app.MapPost("/server-join/auth-grant", CreateAuthorizationGrant);

        IResult ExchangeGrant(JsonElement body)
        {
            var grant = GetString(body, "authorizationGrant", "authorization_grant", "grant");
            if (string.IsNullOrWhiteSpace(grant))
                return Results.BadRequest(new { error = "authorizationGrant is required" });

            var fingerprint = GetString(body, "x509Fingerprint", "certFingerprint", "fingerprint");
            var token = sessions.ExchangeAuthorizationGrant(
                grant,
                fingerprint,
                GetScope(body),
                out var scope,
                out var refreshToken);
            if (token is null)
                return Results.Unauthorized();

            return Results.Ok(new
            {
                accessToken = token,
                tokenType = "Bearer",
                expiresIn = 36000,
                expiresAt = DateTimeOffset.UtcNow.AddHours(10),
                scope,
                refreshToken
            });
        }

        app.MapPost("/server-join/auth-token", ExchangeGrant);
        app.MapPost("/game-session/exchange", ExchangeGrant);

        IResult Validate(HttpContext context)
        {
            var bearer = GetBearerToken(context.Request);
            return bearer is not null && sessions.TryValidate(bearer, out _)
                ? Results.Ok(new { valid = true, success = true })
                : Results.Unauthorized();
        }

        app.MapMethods("/validate", ["GET", "POST"], Validate);
        app.MapMethods("/verify", ["GET", "POST"], Validate);

        app.MapPost("/server/auto-auth", (JsonElement body) =>
        {
            var serverId = GetString(body, "serverUuid", "serverId", "server_id") ?? Guid.NewGuid().ToString();
            var serverName = GetString(body, "serverName", "server_name") ?? $"Server-{serverId[..Math.Min(8, serverId.Length)]}";
            var session = sessions.Renew(serverId, serverName, "hytale:server", ["game.base", "server.host"]);
            return Results.Ok(new
            {
                identityToken = session.IdentityToken,
                sessionToken = session.SessionToken,
                expiresIn = 36000,
                expiresAt = session.ExpiresAt,
                tokenType = "Bearer",
                serverId,
                serverUuid = serverId,
                serverName
            });
        });

        app.MapGet("/server/game-profiles", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            return Results.Ok(new[] { new { uuid, username = name, isDefault = true } });
        });
        app.MapGet("/game-profiles", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            return Results.Ok(new[] { new { uuid, username = name, isDefault = true } });
        });

        app.MapGet("/api/check-identity", (HttpContext context) =>
        {
            var uuid = context.Request.Query["uuid"].ToString();
            var username = context.Request.Query["username"].ToString();
            return string.IsNullOrWhiteSpace(uuid) && string.IsNullOrWhiteSpace(username)
                ? Results.BadRequest(new { error = "uuid or username required" })
                : Results.Ok(new { allowed = true });
        });
    }

    private static void MapAccountRoutes(
        WebApplication app,
        LocalSessionRegistry sessions,
        LocalAccountStore accounts,
        LocalCosmeticsCatalog cosmetics)
    {
        async Task<IResult> GameProfile(HttpContext context, CancellationToken cancellationToken)
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            return Results.Ok(new
            {
                uuid = profile.Uuid,
                username = profile.Username,
                entitlements = new[] { "game.base" },
                createdAt = "2026-01-01T00:00:00Z",
                nextNameChangeAt = DateTimeOffset.UtcNow.AddDays(30),
                skin = profile.SkinJson,
                password_protected = false
            });
        }

        app.MapMethods("/my-account/game-profile", ["GET", "POST"], GameProfile);

        app.MapGet("/my-account/get-profiles", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            return Results.Ok(new { owner = uuid, profiles = new[] { new { uuid, username = name } } });
        });

        app.MapGet("/my-account/get-launcher-data", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            return Results.Ok(new
            {
                eulaAcceptedAt = "2026-01-01T00:00:00Z",
                owner = uuid,
                patchlines = new
                {
                    preRelease = new { buildVersion = "local", newest = 1 },
                    release = new { buildVersion = "local", newest = 1 }
                },
                profiles = new[] { new { uuid, username = name, entitlements = new[] { "game.base" } } }
            });
        });

        app.MapGet("/profile/uuid/{uuid}", async (string uuid, CancellationToken cancellationToken) =>
        {
            var profile = await accounts.FindByUuidAsync(uuid, cancellationToken);
            return profile is null
                ? Results.NotFound(new { error = "Profile not found" })
                : Results.Ok(new { uuid = profile.Uuid, username = profile.Username, skin = profile.SkinJson });
        });

        app.MapGet("/profile/username/{username}", async (string username, CancellationToken cancellationToken) =>
        {
            var profile = await accounts.FindByUsernameAsync(username, cancellationToken);
            return profile is null
                ? Results.NotFound(new { error = "Profile not found" })
                : Results.Ok(new { uuid = profile.Uuid, username = profile.Username });
        });

        app.MapPost("/my-account/skin", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            await accounts.SaveSkinAsync(uuid, name, body, cancellationToken);
            return Results.NoContent();
        });

        app.MapPost("/account-data/skin/{uuid}", async (
            HttpContext context,
            string uuid,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var tokenUuid, out var name)
                || !string.Equals(uuid, tokenUuid, StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();
            await accounts.SaveSkinAsync(uuid, name, body, cancellationToken);
            return Results.NoContent();
        });

        app.MapGet("/my-account/cosmetics", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out _, out _))
                return Results.Unauthorized();
            return Results.Ok(cosmetics.GetUnlockedCosmetics());
        });

        app.MapGet("/player-skins", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            return Results.Ok(new
            {
                activeSkin = profile.ActiveSkinId,
                maxSkins = 10,
                skins = profile.PlayerSkins.Select(skin => new
                {
                    id = skin.Id,
                    name = skin.Name,
                    skinData = skin.SkinData,
                    createdAt = skin.CreatedAt
                })
            });
        });

        app.MapPost("/player-skins", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var username))
                return Results.Unauthorized();
            var skinData = GetString(body, "skinData");
            if (skinData is null)
                return Results.BadRequest(new { error = "skinData is required" });
            var skinId = await accounts.CreatePlayerSkinAsync(
                uuid,
                username,
                GetString(body, "name") ?? "Avatar",
                skinData,
                cancellationToken);
            return Results.Created($"/player-skins/{skinId}", new { skinId });
        });

        app.MapPut("/player-skins/active", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            var skinId = GetString(body, "skinId");
            if (skinId is null)
                return Results.BadRequest(new { error = "skinId is required" });
            return await accounts.SetActivePlayerSkinAsync(uuid, skinId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "Skin not found" });
        });

        app.MapPut("/player-skins/{skinId}", async (
            HttpContext context,
            string skinId,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            return await accounts.UpdatePlayerSkinAsync(
                uuid,
                skinId,
                GetString(body, "name"),
                GetString(body, "skinData"),
                cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "Skin not found" });
        });

        app.MapDelete("/player-skins/{skinId}", async (
            HttpContext context,
            string skinId,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            return await accounts.DeletePlayerSkinAsync(uuid, skinId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "Skin not found" });
        });
    }

    private static void MapSocketGatewayRoute(
        WebApplication app,
        LocalSessionRegistry sessions,
        SocketGatewayService socketGateway)
    {
        app.MapGet("/ws", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!TryGetSocketGatewayIdentity(context.Request, sessions, out var uuid, out var name))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            await socketGateway.RunConnectionAsync(uuid, name, socket, cancellationToken).ConfigureAwait(false);
        });
    }

    private static void MapWorldInviteRoutes(
        WebApplication app,
        LocalSessionRegistry sessions,
        SocketGatewayService socketGateway)
    {
        app.MapGet("/world-invites", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            return Results.Ok(new
            {
                invites = socketGateway.GetIncomingWorldInvites(uuid).Select(MapWorldInvite)
            });
        });
        app.MapGet("/world-invites/sent", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            return Results.Ok(new
            {
                invites = socketGateway.GetOutgoingWorldInvites(uuid).Select(MapWorldInvite)
            });
        });
        app.MapPost("/world-invite", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            var playerUuid = GetGuid(body, "player_uuid", "invited_player_uuid", "playerUuid");
            var inviteCode = GetString(body, "invite_code", "inviteCode");
            if (playerUuid is null || string.IsNullOrWhiteSpace(inviteCode))
                return InvalidWorldInviteRequest();

            var invite = await socketGateway.SendWorldInviteAsync(
                uuid,
                playerUuid.Value,
                inviteCode,
                cancellationToken);
            return invite is null
                ? Results.Json(
                    new { error = "friend_offline", message = "The Mesh friend is unavailable" },
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(MapWorldInvite(invite));
        });
        app.MapPost("/world-invites/accept", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            var inviteUuid = GetGuid(body, "invite_uuid", "inviteUuid");
            if (inviteUuid is null)
                return InvalidWorldInviteRequest();

            var join = await socketGateway.AcceptWorldInviteAsync(uuid, inviteUuid.Value, cancellationToken);
            return join is null
                ? Results.NotFound(new { error = "invite_not_found" })
                : Results.Ok(MapWorldJoin(join));
        });
        app.MapPost("/world-invites/reject", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            var inviteUuid = GetGuid(body, "invite_uuid", "inviteUuid");
            if (inviteUuid is null)
                return InvalidWorldInviteRequest();
            return await socketGateway.RejectWorldInviteAsync(uuid, inviteUuid.Value, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "invite_not_found" });
        });
        app.MapPost("/world-invites/cancel", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            var inviteUuid = GetGuid(body, "invite_uuid", "inviteUuid");
            if (inviteUuid is null)
                return InvalidWorldInviteRequest();
            return await socketGateway.CancelWorldInviteAsync(uuid, inviteUuid.Value, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "invite_not_found" });
        });
        app.MapPost("/presence/join-world", (
            HttpContext context,
            JsonElement body) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            var playerUuid = GetGuid(body, "player_uuid", "playerUuid");
            if (playerUuid is null)
                return InvalidWorldInviteRequest();
            var join = socketGateway.JoinFriendWorld(uuid, playerUuid.Value);
            return join is null
                ? Results.NotFound(new { error = "join_unavailable" })
                : Results.Ok(MapWorldJoin(join));
        });
    }

    private static object MapWorldInvite(WorldInviteSnapshot invite)
        => new Dictionary<string, object?>
        {
            ["invite_uuid"] = invite.InviteUuid,
            ["inviter_uuid"] = invite.InviterUuid,
            ["invitedPlayerUuid"] = invite.InvitedPlayerUuid,
            ["created_at"] = invite.CreatedAt,
            ["expires_at"] = invite.ExpiresAt,
            ["server_host"] = null,
            ["server_port"] = null,
            ["invite_code"] = invite.InviteCode,
            ["is_p2p"] = invite.IsP2P
        };

    private static object MapWorldJoin(WorldJoinSnapshot join)
        => new Dictionary<string, object?>
        {
            ["server_host"] = join.ServerHost,
            ["server_port"] = join.ServerPort,
            ["invite_code"] = join.InviteCode,
            ["server_uuid"] = join.ServerUuid,
            ["server_name"] = join.ServerName,
            ["is_p2p"] = join.IsP2P
        };

    private static IResult InvalidWorldInviteRequest()
        => Results.BadRequest(new
        {
            error = "invalid_world_invite",
            message = "A valid player or invite UUID and P2P invite code are required"
        });

    private static void MapCompatibilityRoutes(
        WebApplication app,
        LocalNodeOptions options,
        LocalSessionRegistry sessions,
        LocalAccountStore accounts,
        MeshFriendService meshFriends,
        MeshNetworkHost meshNetwork,
        SocketGatewayService socketGateway,
        LocalNodeLog? log)
    {
        const string liveConfigVersion = "hyprism-local-v1";
        const string liveConfigHash = "7832ee33ba4e83cd8f15933b3c474ec7fc7a3a51b46c6f8afd304cca6420291c";
        const string liveConfigPath = "/v1/release/any/any/feature-flags/"
                                      + liveConfigHash + ".json";
        var liveConfigFlags = new Dictionary<string, object>
        {
            ["enable_discord_integration"] = new { type = "boolean", value = false },
            ["enable_in_game_discord_link"] = new { type = "boolean", value = false },
            ["enable_new_server_discovery"] = new { type = "boolean", value = false },
            ["enable_news_tiles"] = new { type = "boolean", value = false },
            ["enable_social_layer"] = new { type = "boolean", value = true }
        };

        IResult LiveConfigDescriptor(HttpContext context)
        {
            log?.Info($"Live config descriptor requested at {context.Request.Path}");
            return Results.Ok(new
            {
                flags = liveConfigFlags,
                manifest_url = $"{options.Issuer}/liveconfig/manifest.json",
                version = liveConfigVersion
            });
        }

        app.MapGet("/configs", LiveConfigDescriptor);
        app.MapGet("/configs/{**path}", LiveConfigDescriptor);
        app.MapGet(liveConfigPath, () => Results.Ok(liveConfigFlags));
        app.MapGet("/liveconfig/manifest.json", () => Results.Ok(new
        {
            version = liveConfigVersion,
            patchline = "release",
            platform = new { os = "any", arch = "any" },
            configs = new Dictionary<string, object>
            {
                ["feature-flags"] = new
                {
                    url = liveConfigPath,
                    hash = liveConfigHash,
                    updated = DateTimeOffset.UnixEpoch
                }
            }
        }));
        app.MapGet("/news-tiles", () => Results.Ok(new { tiles = Array.Empty<object>() }));

        app.MapGet("/friends", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();

            var friends = await GetSocialFriendsAsync(
                uuid,
                meshFriends,
                meshNetwork,
                socketGateway,
                log,
                cancellationToken);
            return Results.Ok(new { friends, truncated = false });
        });
        app.MapGet("/friends/favorites", () => Results.Ok(new { favorites = Array.Empty<object>() }));
        app.MapGet("/presence/friends", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();

            var friends = await GetSocialFriendsAsync(
                uuid,
                meshFriends,
                meshNetwork,
                socketGateway,
                log,
                cancellationToken);
            return Results.Ok(new
            {
                friends = friends.Select(friend => new HytaleFriendPresence(
                    friend.Uuid,
                    friend.Username,
                    friend.IsOnline ? "online" : "offline",
                    Activity: null,
                    friend.GameMode,
                    ServerUuid: null,
                    ServerName: null,
                    friend.WorldName,
                    ServerHost: null,
                    ServerPort: null,
                    InviteCode: null,
                    friend.CanJoin,
                    friend.IsWorldOwner))
            });
        });
        app.MapGet("/friend-requests/outgoing", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            return Results.Ok(new
            {
                requests = meshNetwork.GetOutgoingFriendRequests(uuid).Select(ToHytaleFriendRequest),
                truncated = false
            });
        });
        app.MapGet("/friend-requests/incoming", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            return Results.Ok(new
            {
                requests = meshNetwork.GetIncomingFriendRequests(uuid).Select(ToHytaleFriendRequest),
                truncated = false
            });
        });
        app.MapGet("/party/invites", () => Results.Ok(new { invites = Array.Empty<object>() }));
        app.MapGet("/party/invites/sent", () => Results.Ok(new { invites = Array.Empty<object>() }));
        app.MapGet("/party", () => Results.Text("not in a party", statusCode: StatusCodes.Status404NotFound));
        app.MapGet("/blocks", () => Results.Ok(new { blocks = Array.Empty<object>(), truncated = false }));
        app.MapPost("/friend-requests/by-username", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var friendId = GetString(body, "username");
            if (string.IsNullOrWhiteSpace(friendId))
                return Results.BadRequest(new { error = "unknown_username" });

            await TryActivateMeshAsync(meshNetwork, uuid, name, log, cancellationToken);
            var result = await meshNetwork.SendFriendRequestAsync(uuid, friendId, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ToHytaleFriendRequest(result.Value))
                : MapSocialFailure(result.Failure);
        });
        app.MapPost("/friend-requests/accept", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var requesterUuid = GetGuid(body, "requester_uuid", "requesterUuid", "player_uuid", "playerUuid");
            if (requesterUuid is null)
                return Results.BadRequest(new { error = "invalid_request" });

            await TryActivateMeshAsync(meshNetwork, uuid, name, log, cancellationToken);
            var result = await meshNetwork.AcceptFriendRequestAsync(uuid, requesterUuid.Value, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ToHytaleFriendRequest(result.Value))
                : MapSocialFailure(result.Failure);
        });
        app.MapPost("/friend-requests/reject", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var requesterUuid = GetGuid(body, "requester_uuid", "requesterUuid", "player_uuid", "playerUuid");
            if (requesterUuid is null)
                return Results.BadRequest(new { error = "invalid_request" });

            await TryActivateMeshAsync(meshNetwork, uuid, name, log, cancellationToken);
            return await meshNetwork.RejectFriendRequestAsync(uuid, requesterUuid.Value, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "request_not_found" });
        });
        app.MapMethods("/me/interactions/{**path}",
            ["GET", "POST", "PUT", "PATCH", "DELETE"],
            () => Results.StatusCode(StatusCodes.Status501NotImplemented));

        app.MapGet("/presence/settings", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            return Results.Ok(profile.PresenceSettings.Count == 0
                ? DefaultPresenceSettings
                : profile.PresenceSettings.ToDictionary(pair => pair.Key, pair => (object)pair.Value));
        });
        app.MapPut("/presence/settings", async (
            HttpContext context,
            Dictionary<string, JsonElement> body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            await accounts.SavePresenceSettingsAsync(uuid, body, cancellationToken);
            return Results.NoContent();
        });
        app.MapPost("/presence/heartbeat", () => Results.NoContent());
        app.MapPost("/telemetry", () => Results.Ok(new { success = true, received = true }));
        app.MapPost("/telemetry/{**path}", () => Results.Ok(new { success = true, received = true }));
        app.MapPost("/analytics", () => Results.Ok(new { success = true, received = true }));
        app.MapPost("/analytics/{**path}", () => Results.Ok(new { success = true, received = true }));
        app.MapPost("/bugs/create", () => Results.NoContent());
        app.MapPost("/feedback/create", () => Results.NoContent());
        app.MapGet("/servers/listings", () => Results.Ok(Array.Empty<object>()));
    }

    private static IResult SessionResult(OmniAuthSession session)
        => Results.Ok(new
        {
            identityToken = session.IdentityToken,
            sessionToken = session.SessionToken,
            expiresIn = 36000,
            expiresAt = session.ExpiresAt,
            tokenType = "Bearer"
        });

    private static async Task TryActivateMeshAsync(
        MeshNetworkHost meshNetwork,
        string profileId,
        string displayName,
        LocalNodeLog? log,
        CancellationToken cancellationToken)
    {
        try
        {
            await meshNetwork.ActivateProfileAsync(profileId, displayName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log?.Warning($"Mesh profile activation failed: {exception.Message}");
        }
    }

    private static async Task<IReadOnlyList<HytaleFriend>> GetSocialFriendsAsync(
        string profileId,
        MeshFriendService meshFriends,
        MeshNetworkHost meshNetwork,
        SocketGatewayService socketGateway,
        LocalNodeLog? log,
        CancellationToken cancellationToken)
    {
        try
        {
            var presence = meshNetwork.GetPresence(profileId)
                .ToDictionary(item => item.PeerId, StringComparer.Ordinal);
            var friends = await meshFriends.GetFriendsAsync(profileId, cancellationToken);
            return friends
                .Select(friend =>
                {
                    if (!Guid.TryParse(friend.PlayerUuid, out var playerUuid))
                        return null;

                    var isOnline = presence.TryGetValue(friend.PeerId, out var current)
                                   && current.IsOnline;
                    var canJoin = socketGateway.JoinFriendWorld(profileId, playerUuid) is not null;
                    return new HytaleFriend(
                        playerUuid,
                        friend.DisplayName,
                        friend.AddedAt,
                        isOnline,
                        DiscordUserId: null,
                        IsFavorite: false,
                        WorldName: null,
                        GameMode: null,
                        CanJoin: canJoin,
                        IsWorldOwner: canJoin);
                })
                .OfType<HytaleFriend>()
                .OrderBy(friend => friend.Username, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log?.Warning($"Mesh social state could not be read: {exception.Message}");
            return [];
        }
    }

    private static IResult MissingIdentity()
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["uuid"] = ["A player UUID is required"],
            ["username"] = ["A player name is required"]
        });

    private static bool ShouldLogRequest(PathString path, int statusCode)
        => statusCode >= 400
           || path.StartsWithSegments("/game-session")
           || path.StartsWithSegments("/server-join")
           || path.StartsWithSegments("/v1/sessions")
           || path.StartsWithSegments("/_hyprism/v1");

    private static string? GetString(JsonElement body, params string[] names)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
        {
            if (body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    private static object ToHytaleFriendRequest(MeshFriendRequestSnapshot request)
        => new
        {
            uuid = request.RequestUuid,
            requester_uuid = request.RequesterUuid,
            player_uuid = request.PlayerUuid,
            username = request.Username,
            created_at = request.CreatedAt,
            accepted_at = request.AcceptedAt
        };

    private static IResult MapSocialFailure(MeshFailure failure)
    {
        var statusCode = failure.Code switch
        {
            "unknown_username" or "invalid_peer_record" => StatusCodes.Status404NotFound,
            "request_not_found" => StatusCodes.Status404NotFound,
            "already_friends" or "duplicate_pending_invite" => StatusCodes.Status409Conflict,
            "self_target" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return Results.Json(new { error = failure.Code, message = failure.Message }, statusCode: statusCode);
    }

    private static Guid? GetGuid(JsonElement body, params string[] names)
    {
        var value = GetString(body, names);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? GetScope(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return null;
        if (!body.TryGetProperty("scope", out var value) && !body.TryGetProperty("scopes", out value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => string.Join(' ', value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())),
            _ => null
        };
    }

    private static string? GetBearerToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : null;
    }

    private static bool IsControlAuthorized(HttpRequest request, string expectedSecret)
    {
        var suppliedSecret = request.Headers["X-HyPrism-Control"].ToString();
        if (suppliedSecret.Length != expectedSecret.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(suppliedSecret),
            Encoding.UTF8.GetBytes(expectedSecret));
    }

    private static bool IsMeshControlAuthorized(HttpRequest request, string? expectedSecret)
        => !string.IsNullOrWhiteSpace(expectedSecret)
           && IsControlAuthorized(request, expectedSecret);

    private static IResult MapMeshResult<T>(MeshResult<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        var statusCode = result.Failure.Code switch
        {
            "expired" => StatusCodes.Status410Gone,
            "replayed_invite" or "replayed_acceptance" or "unknown_invite" => StatusCodes.Status409Conflict,
            "invite_limit_reached" or "friend_limit_reached" or "replay_cache_full" =>
                StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(
            new { error = result.Failure.Code, message = result.Failure.Message },
            statusCode: statusCode);
    }

    private static bool TryGetBearerIdentity(
        HttpRequest request,
        LocalSessionRegistry sessions,
        out string uuid,
        out string name)
    {
        uuid = string.Empty;
        name = string.Empty;
        var bearer = GetBearerToken(request);
        if (bearer is null || !sessions.TryValidate(bearer, out var claims))
            return false;

        uuid = claims.GetProperty("sub").GetString() ?? string.Empty;
        if (claims.TryGetProperty("username", out var username))
            name = username.GetString() ?? string.Empty;
        if (name.Length == 0 && claims.TryGetProperty("name", out var displayName))
            name = displayName.GetString() ?? string.Empty;
        return uuid.Length > 0 && name.Length > 0;
    }

    private static bool TryGetSocketGatewayIdentity(
        HttpRequest request,
        LocalSessionRegistry sessions,
        out string uuid,
        out string name)
    {
        if (TryGetBearerIdentity(request, sessions, out uuid, out name))
            return true;

        uuid = string.Empty;
        name = string.Empty;
        var token = request.Query["access_token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
            token = request.Query["token"].ToString();
        if (string.IsNullOrWhiteSpace(token) || !sessions.TryValidate(token, out var claims))
            return false;

        uuid = claims.GetProperty("sub").GetString() ?? string.Empty;
        if (claims.TryGetProperty("username", out var username))
            name = username.GetString() ?? string.Empty;
        if (name.Length == 0 && claims.TryGetProperty("name", out var displayName))
            name = displayName.GetString() ?? string.Empty;
        return uuid.Length > 0 && name.Length > 0;
    }

    private sealed record MeshCreateInviteRequest(string DisplayName, double? LifetimeMinutes);
    private sealed record MeshAcceptInviteRequest(string DisplayName, string InviteToken);
    private sealed record MeshCompleteInviteRequest(string AcceptanceToken);
    private sealed record HytaleFriend(
        Guid Uuid,
        string Username,
        DateTimeOffset Since,
        bool IsOnline,
        string? DiscordUserId,
        bool IsFavorite,
        string? WorldName,
        string? GameMode,
        bool CanJoin,
        bool IsWorldOwner);
    private sealed record HytaleFriendPresence(
        Guid PlayerUuid,
        string Username,
        string Status,
        string? Activity,
        string? GameMode,
        Guid? ServerUuid,
        string? ServerName,
        string? WorldName,
        string? ServerHost,
        int? ServerPort,
        string? InviteCode,
        bool CanJoin,
        bool IsWorldOwner);
}
