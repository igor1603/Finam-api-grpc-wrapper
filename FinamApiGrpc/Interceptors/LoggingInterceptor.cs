using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace FinamApiGrpc.Interceptors;

public class LoggingInterceptor : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var methodName = context.Method.FullName;
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine($"[LOG] Начали вызов {methodName}");

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

            Console.WriteLine($"[LOG] Завершили вызов {methodName} | Длительность: {stopwatch.ElapsedMilliseconds} мс");
            return response;
        }
        catch (RpcException rpcEx)
        {
            stopwatch.Stop();

            Console.WriteLine(
                $"[LOG] Ошибка вызова {methodName} | Статус: {rpcEx.StatusCode} | Длительность: {stopwatch.ElapsedMilliseconds} мс | {rpcEx.Status.Detail}");

            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            Console.WriteLine(
                $"[LOG] Ошибка вызова {methodName} | Длительность: {stopwatch.ElapsedMilliseconds} мс | {ex.Message}");

            throw;
        }
    }
}

