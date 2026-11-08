using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace FinamApiGrpc.Interceptors;

public class LoggingInterceptor : Interceptor
{
    private readonly ILogger _logger;

    public LoggingInterceptor(ILogger logger)
    {
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var methodName = context.Method.FullName;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogTrace($"[LOG] Начали вызов {methodName}");

        var call = continuation(request, context);

        var interceptedResponseTask = LogResponseAsync(call.ResponseAsync, methodName, stopwatch);

        return new AsyncUnaryCall<TResponse>(
            interceptedResponseTask,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private async Task<TResponse> LogResponseAsync<TResponse>(
        Task<TResponse> responseTask,
        string methodName,
        Stopwatch stopwatch)
    {
        try
        {
            var response = await responseTask;
            stopwatch.Stop();

            _logger.LogDebug($"[LOG] Завершили вызов {methodName} | Длительность: {stopwatch.ElapsedMilliseconds} мс");
            return response;
        }
        catch (RpcException rpcEx)
        {
            stopwatch.Stop();

            _logger.LogError($"[LOG] Ошибка вызова {methodName} | Статус: {rpcEx.StatusCode} | Длительность: {stopwatch.ElapsedMilliseconds} мс | {rpcEx.Status.Detail}");

            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError($"[LOG] Ошибка вызова {methodName} | Длительность: {stopwatch.ElapsedMilliseconds} мс | {ex.Message}");

            throw;
        }
    }
}

