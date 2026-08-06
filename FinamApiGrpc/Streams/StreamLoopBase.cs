using Grpc.Core;

namespace FinamApiGrpc.Streams;

public abstract class StreamLoopBase
{
    private readonly StreamReconnectPolicy _reconnectPolicy;
    private readonly Action<string>? _logger;

    protected StreamLoopBase(StreamReconnectPolicy reconnectPolicy, Action<string>? logger = null)
    {
        _reconnectPolicy = reconnectPolicy ?? throw new ArgumentNullException(nameof(reconnectPolicy));
        _logger = logger;
    }

    protected bool RunForever { get; init; } = true;

    protected async Task RunWithReconnectAsync(Func<CancellationToken, Task> operationAsync, CancellationToken cancellationToken)
    {
        var reconnectAttempts = 0;
        var currentDelaySeconds = _reconnectPolicy.BaseDelaySeconds;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await operationAsync(cancellationToken).ConfigureAwait(false);

                if (!RunForever)
                {
                    break;
                }

                currentDelaySeconds = _reconnectPolicy.BaseDelaySeconds;

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await DelayBeforeReconnectAsync(currentDelaySeconds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log("[StreamLoop] Операция отменена пользователем.");
                break;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Log("[StreamLoop] Операция отменена пользователем.");
                break;
            }
            catch (RpcException ex) when (_reconnectPolicy.IsRetryableStatus(ex.StatusCode))
            {
                if (ShouldStopReconnect(reconnectAttempts, cancellationToken))
                {
                    throw;
                }

                Log($"[StreamLoop] Временная ошибка gRPC: {ex.StatusCode} | {ex.Status.Detail}");
                reconnectAttempts++;
                await DelayBeforeReconnectAsync(currentDelaySeconds, cancellationToken).ConfigureAwait(false);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, _reconnectPolicy.MaxDelaySeconds);
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                if (ShouldStopReconnect(reconnectAttempts, cancellationToken))
                {
                    throw;
                }

                Log($"[StreamLoop] Временная ошибка: {ex.Message}");
                reconnectAttempts++;
                await DelayBeforeReconnectAsync(currentDelaySeconds, cancellationToken).ConfigureAwait(false);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, _reconnectPolicy.MaxDelaySeconds);
            }
        }
    }

    protected virtual bool IsTransientException(Exception ex)
    {
        return ex is TimeoutException
            or IOException
            or TaskCanceledException
            or HttpRequestException;
    }

    private bool ShouldStopReconnect(int reconnectAttempts, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (_reconnectPolicy.MaxAttempts is null)
        {
            return false;
        }

        return reconnectAttempts >= _reconnectPolicy.MaxAttempts.Value;
    }

    protected async Task DelayBeforeReconnectAsync(int delaySeconds, CancellationToken cancellationToken)
    {
        if (delaySeconds <= 0)
        {
            return;
        }

        Log($"[StreamLoop] Ожидание перед повторным подключением: {delaySeconds} сек...");
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
    }

    protected void Log(string message)
    {
        _logger?.Invoke(message);
    }
}