using System.Net;
using System.Threading.RateLimiting;
using ChatApp.Api.Hubs;
using ChatApp.Api.Infrastructure.Attachments;
using ChatApp.Api.Infrastructure.Auth;
using ChatApp.Api.Infrastructure.Configuration;
using ChatApp.Api.Infrastructure.Csrf;
using ChatApp.Api.Infrastructure.Images;
using ChatApp.Api.Infrastructure.Presence;
using ChatApp.Api.Infrastructure.Rooms;
using ChatApp.Api.Infrastructure.Scanning;
using ChatApp.Data;
using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Services.Attachments;
using ChatApp.Data.Services.Identity;
using ChatApp.Data.Services.Messaging;
using ChatApp.Data.Services.Presence;
using ChatApp.Data.Services.Rooms;
using ChatApp.Data.Services.Social;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Attachments;
using ChatApp.Domain.Services.Identity;
using ChatApp.Domain.Services.Presence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;

namespace ChatApp.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((ctx, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .WriteTo.Console());

        var conn = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        // Explicit pool sizing for the 300-concurrent-user single-instance envelope.
        // Only set if the connection string doesn't already specify one.
        var csb = new NpgsqlConnectionStringBuilder(conn);
        if (!csb.ContainsKey("Maximum Pool Size"))
        {
            csb.MaxPoolSize = 100;
        }
        var dataSource = new NpgsqlDataSourceBuilder(csb.ToString()).Build();
        builder.Services.AddSingleton(dataSource);

        builder.Services.AddDbContext<ChatDbContext>(o => o
            .UseNpgsql(dataSource)
            .UseSnakeCaseNamingConvention());

        builder.Services.AddProblemDetails();

        var scannerKind = builder.Configuration["ChatApp:Attachments:Scanner"] ?? "clamav";
        var healthChecks = builder.Services.AddHealthChecks()
            .AddDbContextCheck<ChatDbContext>();
        if (!scannerKind.Equals("noop", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<ClamAvHealthCheck>("clamav");
        }

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMemoryCache();

        builder.Services.Configure<CookieOptionsConfig>(builder.Configuration.GetSection("ChatApp:Cookie"));
        builder.Services.Configure<FilesOptions>(builder.Configuration.GetSection("ChatApp:Files"));

        // Bind attachment options; FilesRoot inherits from ChatApp:Files:Root when not overridden
        builder.Services.Configure<AttachmentsOptions>(builder.Configuration.GetSection("ChatApp:Attachments"));
        builder.Services.PostConfigure<AttachmentsOptions>(opts =>
        {
            if (string.IsNullOrEmpty(opts.FilesRoot))
            {
                opts.FilesRoot = builder.Configuration["ChatApp:Files:Root"] ?? "/var/chatapp/files";
            }
        });

        // Raise Kestrel's multipart limit so the per-endpoint [RequestFormLimits] can take effect
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
        {
            o.MultipartBodyLengthLimit = 22_000_000;
        });

        // Why: api sits behind nginx; without this, every session row records the nginx-container IP.
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
            o.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Loopback, 8));
        });

        builder.Services
            .AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<SessionAuthenticationOptions, SessionAuthenticationHandler>(
                SessionAuthenticationHandler.SchemeName, _ => { });

        builder.Services.AddAuthorization();
        builder.Services.AddSignalR();

        builder.Services.AddRateLimiter(opts =>
        {
            opts.AddPolicy("uploads", context =>
            {
                var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });

            opts.AddPolicy("messages", context =>
            {
                var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit          = 30,
                    TokensPerPeriod     = 3,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    AutoReplenishment   = true,
                    QueueLimit          = 0,
                });
            });

            // General anti-spam limiter for non-message write endpoints (friendships, invitations,
            // room moderation, profile edits, etc.). Per-user token bucket: 60 burst, refilled 1/s.
            opts.AddPolicy("general", context =>
            {
                var partitionKey = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit          = 60,
                    TokensPerPeriod     = 1,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    AutoReplenishment   = true,
                    QueueLimit          = 0,
                });
            });

            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opts.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers["Retry-After"] =
                        ((int)retryAfter.TotalSeconds + 1).ToString();
                }
                await Task.CompletedTask;
            };
        });

        builder.Services.AddSingleton<HubRateLimiter>();
        builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        builder.Services.AddSingleton<CookieWriter>();
        builder.Services.AddSingleton<LoginRateLimiter>();
        builder.Services.AddSingleton<PasswordResetRateLimiter>();
        builder.Services.AddSingleton<IAvatarImageProcessor, AvatarImageProcessor>();
        builder.Services.AddSingleton<IAttachmentImageProcessor, AttachmentImageProcessor>();

        builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection("ChatApp:Attachments:ClamAv"));
        if (scannerKind.Equals("noop", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IAttachmentScanner, NoOpScanner>();
        }
        else
        {
            builder.Services.AddSingleton<IAttachmentScanner, ClamAvScanner>();
        }

        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<PasswordResetService>();
        builder.Services.AddSingleton<IPasswordResetNotifier, LogPasswordResetNotifier>();
        builder.Services.AddScoped<SessionLookupService>();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<ProfileService>();
        builder.Services.AddScoped<SessionQueryService>();
        builder.Services.AddScoped<SessionRevocationService>();
        builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();

        // Social
        builder.Services.AddScoped<PersonalChatService>();
        builder.Services.AddScoped<UserBanService>();
        builder.Services.AddScoped<FriendshipService>();

        // Rooms
        builder.Services.AddScoped<RoomService>();
        builder.Services.AddScoped<RoomPermissionService>();
        builder.Services.AddScoped<InvitationService>();
        builder.Services.AddScoped<ModerationService>();
        builder.Services.AddScoped<RoomPurgeService>();
        builder.Services.AddHostedService<SoftDeletedRoomPurger>();

        // Messaging
        builder.Services.AddSingleton<IChatBroadcaster, ChatBroadcaster>();
        builder.Services.AddScoped<MessageService>();
        builder.Services.AddScoped<UnreadService>();

        // Attachments
        builder.Services.AddScoped<AttachmentService>();
        builder.Services.AddHostedService<AttachmentPurger>();

        // Presence
        builder.Services.AddSingleton<IPresenceStore, InMemoryPresenceStore>();
        builder.Services.AddSingleton<PresenceAggregator>();
        builder.Services.AddScoped<IPresenceFanoutResolver, ContactFanoutResolver>();
        builder.Services.AddHostedService<PresenceTickService>();

        builder.Services.AddControllers();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ChatDbContext>()
                .Database.Migrate();
        }

        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"]        = "DENY";
            context.Response.Headers["Referrer-Policy"]        = "strict-origin-when-cross-origin";
            await next();
        });

        app.UseAuthentication();
        app.UseCsrf();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        app.MapHub<PresenceHub>("/hub/presence");
        app.MapHub<ChatHub>("/hub/chat");
        app.MapHealthChecks("/health");

        app.Run();
    }
}
