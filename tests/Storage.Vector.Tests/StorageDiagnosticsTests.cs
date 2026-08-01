using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Storage.Vector.Tests;

public class StorageDiagnosticsTests
{
    [Fact]
    public async Task StorageDiagnostics_ActivitySource_RecordsActivityAndTags()
    {
        var activities = new System.Collections.Concurrent.ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == StorageDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        using var provider = new InMemoryStorageProvider();
        using var data = new MemoryStream(Encoding.UTF8.GetBytes("test payload for tracing"));

        await provider.PutObjectAsync("trace-bucket", "item.txt", data, "text/plain", CancellationToken.None);

        Assert.NotEmpty(activities);
        Assert.Contains(activities, a => a.OperationName == "PutObject");
    }
}
