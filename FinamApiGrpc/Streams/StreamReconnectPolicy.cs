using Grpc.Core;

namespace FinamApiGrpc.Streams;

public sealed class StreamReconnectPolicy
{
    public int BaseDelaySeconds { get; init; } = 2;
    public int MaxDelaySeconds { get; init; } = 65;
    public int? MaxAttempts { get; init; }

    public IReadOnlyList<StatusCode> RetryableStatusCodes { get; init; } =
    [
        StatusCode.Unavailable,
        StatusCode.DeadlineExceeded,
        StatusCode.Internal,
        StatusCode.ResourceExhausted,
        StatusCode.Unknown
    ];

    public bool IsRetryableStatus(StatusCode statusCode)
    {
        return RetryableStatusCodes.Contains(statusCode);
    }
}