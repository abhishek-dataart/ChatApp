using ChatApp.Domain.Abstractions;
using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Attachments;

// Unit-level test of the scanner contract via NSubstitute. Does not require Postgres but
// lives under Integration since it cooperates with the ChatAppFactory scanner-override hook.
public class ClamAvScannerTests
{
    [Fact(Skip = "Use NSubstitute to mock IAttachmentScanner: clean -> accepts; infected -> rejects with 422/400.")]
    public Task Infected_upload_is_rejected()
    {
        _ = NSubstitute.Substitute.For<IAttachmentScanner>();
        return Task.CompletedTask;
    }
}
