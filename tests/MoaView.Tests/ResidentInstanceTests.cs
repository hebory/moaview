using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Reflection;
using MoaView.Services;
using Xunit;

namespace MoaView.Tests;

public sealed class ResidentInstanceTests
{
    [Fact]
    public void SecondaryForwardsLaunchRequestsAndOwnerCanRestartAfterDispose()
    {
        var name = $"ResidentInstanceTests-{Guid.NewGuid():N}";
        using var owner = new ResidentInstance(name);
        Assert.True(owner.IsPrimary);

        var received = new ConcurrentQueue<ResidentRequest>();
        using var firstRequestReceived = new ManualResetEventSlim();
        owner.StartListening(request =>
        {
            received.Enqueue(request);
            firstRequestReceived.Set();
        });

        var firstRequest = new ResidentRequest(Background: true, FilePath: null);
        var fileRequest = new ResidentRequest(Background: false, FilePath: @"C:\sample\photo.png");
        var secondaryWork = Task.Run(() =>
        {
            using var secondary = new ResidentInstance(name);
            var isPrimary = secondary.IsPrimary;
            var firstForwarded = secondary.TryForward(firstRequest, out var firstError);
            var fileForwarded = secondary.TryForward(fileRequest, out var fileError);
            return (isPrimary, firstForwarded, firstError, fileForwarded, fileError);
        });

        Assert.True(secondaryWork.Wait(TimeSpan.FromSeconds(5)));
        var secondaryResult = secondaryWork.Result;
        Assert.False(secondaryResult.isPrimary);
        Assert.True(secondaryResult.firstForwarded, secondaryResult.firstError);
        Assert.True(secondaryResult.fileForwarded, secondaryResult.fileError);
        Assert.True(firstRequestReceived.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => received.Count == 2, TimeSpan.FromSeconds(2)));
        Assert.True(received.TryDequeue(out var backgroundRequest));
        Assert.True(backgroundRequest.Background);
        Assert.Null(backgroundRequest.FilePath);
        Assert.True(received.TryDequeue(out var forwardedFileRequest));
        Assert.False(forwardedFileRequest.Background);
        Assert.Equal(fileRequest.FilePath, forwardedFileRequest.FilePath);

        owner.Dispose();
        using var restarted = new ResidentInstance(name);
        Assert.True(restarted.IsPrimary);
    }

    [Fact]
    public void ListenerRecoversAfterAnIncompleteClientTimesOut()
    {
        var name = $"ResidentInstanceTimeoutTests-{Guid.NewGuid():N}";
        using var owner = new ResidentInstance(name);
        Assert.True(owner.IsPrimary);
        using var received = new ManualResetEventSlim();
        owner.StartListening(_ => received.Set());

        var pipeField = typeof(ResidentInstance).GetField("_pipeName", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pipeName = (string)pipeField.GetValue(owner)!;
        using (var stalledClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            stalledClient.Connect(2000);
            stalledClient.Write(BitConverter.GetBytes(16));
            stalledClient.Flush();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var reply = new byte[1];
            Assert.Equal(0, stalledClient.ReadAsync(reply, timeout.Token).AsTask().GetAwaiter().GetResult());
        }

        var request = new ResidentRequest(Background: false, FilePath: @"C:\sample\after-timeout.png");
        var forwarded = Task.Run(() =>
        {
            using var secondary = new ResidentInstance(name);
            return secondary.TryForward(request, out var error) ? (true, (string?)null) : (false, error);
        });
        Assert.True(forwarded.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(forwarded.Result.Item1, forwarded.Result.Item2);
        Assert.True(received.Wait(TimeSpan.FromSeconds(2)));
    }
}
