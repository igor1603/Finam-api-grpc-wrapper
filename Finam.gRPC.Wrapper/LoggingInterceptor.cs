using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

public class LoggingInterceptor : Interceptor
{
    // Современный перехват унарного вызова
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var methodName = context.Method.FullName;
#if DEBUG
        Console.WriteLine($"[gRPC LOG] >>> [Унарный] Вызов: {methodName}");
#endif
        var stopwatch = Stopwatch.StartNew();

        // 1. Запускаем запрос дальше по конвейеру
        var call = continuation(request, context);

        // 2. Используем современный асинхронный перехват ответа
        var interceptedResponseTask = LogResponseAsync(call.ResponseAsync, methodName, stopwatch);

        // 3. Возвращаем оригинальный call, но подменяем в нем только Task ответа
        return new AsyncUnaryCall<TResponse>(
            interceptedResponseTask,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private async Task<TResponse> LogResponseAsync<TResponse>(Task<TResponse> responseTask, string methodName, Stopwatch stopwatch)
    {
        try
        {
            // Здесь асинхронно ждем реальный ответ от Finam API через HttpClient
            var response = await responseTask;
            stopwatch.Stop();
#if DEBUG
            Console.WriteLine($"[gRPC LOG] <<< [Унарный] Успешно: {methodName} | Время: {stopwatch.ElapsedMilliseconds} мс");
#endif
            return response;
        }
        catch (RpcException rpcEx)
        {
            stopwatch.Stop();
#if DEBUG
            Console.WriteLine($"[gRPC LOG] !!! [Унарный] gRPC Ошибка: {methodName} | Статус: {rpcEx.StatusCode} | Время: {stopwatch.ElapsedMilliseconds} мс | {rpcEx.Status.Detail}");
#endif
            throw; // Обязательно пробрасываем ошибку дальше
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
#if DEBUG
            Console.WriteLine($"[gRPC LOG] !!! [Унарный] Системная Ошибка: {methodName} | Время: {stopwatch.ElapsedMilliseconds} мс | {ex.Message}");
#endif
            throw; // Обязательно пробрасываем ошибку дальше
        }
    }
}

