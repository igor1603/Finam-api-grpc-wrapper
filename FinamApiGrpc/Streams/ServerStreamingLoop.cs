using Grpc.Core;

namespace FinamApiGrpc.Streams;

public class ServerStreamingLoop<TRequest, TResponse> : StreamLoopBase
{
    public ServerStreamingLoop(StreamReconnectPolicy reconnectPolicy, bool runForever = true, Action<string>? logger = null)
        : base(reconnectPolicy, logger)
    {
        RunForever = runForever;
    }

    public Task RunAsync(
        TRequest request,
        Func<TRequest, CancellationToken, AsyncServerStreamingCall<TResponse>> createCall,
        Func<TResponse, CancellationToken, Task> onMessageAsync,
        CancellationToken cancellationToken = default)
    {
        return RunWithReconnectAsync(
            async ct =>
            {
                using var streamingCall = createCall(request, ct);

                if (streamingCall?.ResponseStream == null)
                {
                    throw new InvalidOperationException("Сервер вернул пустой поток ответов.");
                }

                await foreach (var response in streamingCall.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    if (response is not null)
                    {
                        await onMessageAsync(response, ct).ConfigureAwait(false);
                    }
                }
            },
            cancellationToken);
    }
}