using Grpc.Core;

namespace FinamApiGrpc.Streams;

public class BidirectionalStreamingLoop<TRequest, TResponse> : StreamLoopBase
{
    public BidirectionalStreamingLoop(StreamReconnectPolicy reconnectPolicy, bool runForever = true, Action<string>? logger = null)
        : base(reconnectPolicy, logger)
    {
        RunForever = runForever;
    }

    public Task RunAsync(
        Func<CancellationToken, AsyncDuplexStreamingCall<TRequest, TResponse>> createCall,
        Func<AsyncDuplexStreamingCall<TRequest, TResponse>, CancellationToken, Task> processAsync,
        CancellationToken cancellationToken = default)
    {
        return RunWithReconnectAsync(
            async ct =>
            {
                using var streamingCall = createCall(ct);

                await processAsync(streamingCall, ct).ConfigureAwait(false);
            },
            cancellationToken);
    }
}