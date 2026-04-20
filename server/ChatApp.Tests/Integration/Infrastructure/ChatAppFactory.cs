using ChatApp.Api;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Attachments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ChatApp.Tests.Integration.Infrastructure;

public sealed class ChatAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _filesRoot;

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public ChatAppFactory(string connectionString)
    {
        _connectionString = connectionString;
        _filesRoot = Path.Combine(Path.GetTempPath(), "chatapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_filesRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting wins over appsettings.json / appsettings.Development.json which would
        // otherwise point us at the docker-compose "db" hostname.
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("ChatApp:Files:Root",         _filesRoot);
        builder.UseSetting("ChatApp:Attachments:Scanner", "noop");
        builder.UseSetting("ChatApp:Cookie:Secure",      "false");
        builder.UseSetting("ChatApp:Cookie:Name",        "chatapp_session");
        builder.UseSetting("ChatApp:Cookie:SameSite",    "Lax");

        builder.ConfigureServices(services =>
        {
            // NoOpScanner is already wired by Scanner=noop above, but allow tests to
            // override with a mock if needed via a public hook.
            services.RemoveAll<IAttachmentScanner>();
            services.AddSingleton<IAttachmentScanner>(_ => ScannerOverride ?? new NoOpScanner());

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public IAttachmentScanner? ScannerOverride { get; set; }

    public string FilesRoot => _filesRoot;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_filesRoot))
        {
            try { Directory.Delete(_filesRoot, recursive: true); } catch { /* best-effort */ }
        }
    }
}
