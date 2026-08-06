using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace FinamApiGrpc.Interceptors;

public class ExceptionHandlingInterceptor : Interceptor
{
    private readonly int _maxRetryCount;
    private readonly TimeSpan _retryDelay;

    public ExceptionHandlingInterceptor(int maxRetryCount = 3, TimeSpan? retryDelay = null)
    {
        _maxRetryCount = maxRetryCount;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(200);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine($"[EXCEPT] Начали вызов {context.Method.FullName}");

        var interceptedResponseTask = ExecuteWithRetryAsync(request, context, continuation, stopwatch);

        return new AsyncUnaryCall<TResponse>(
            interceptedResponseTask,
            GetHeadersAsync(interceptedResponseTask, request, context, continuation),
            () => GetStatus(interceptedResponseTask),
            () => GetTrailers(interceptedResponseTask),
            () => { });
    }

    private async Task<TResponse> ExecuteWithRetryAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
        Stopwatch stopwatch)
        where TRequest : class
        where TResponse : class
    {
        int attempt = 0;

        while (true)
        {
            attempt++;

            try
            {
                var call = continuation(request, context);
                var response = await call.ResponseAsync;

                stopwatch.Stop();
                Console.WriteLine($"[EXCEPT] Завершили вызов {context.Method.FullName} | Длительность: {stopwatch.ElapsedMilliseconds} мс");

                return response;
            }
            catch (RpcException rpcEx) when (IsTransientError(rpcEx.StatusCode) && attempt < _maxRetryCount)
            {
                Console.WriteLine(
                    $"[EXCEPT] Повтор вызова {context.Method.FullName} | Попытка {attempt}/{_maxRetryCount} | Статус: {rpcEx.StatusCode}");

                await Task.Delay(_retryDelay);
            }
            catch (RpcException rpcEx)
            {
                stopwatch.Stop();

                Console.WriteLine(
                    $"[EXCEPT] Ошибка вызова {context.Method.FullName} | Статус: {rpcEx.StatusCode} | Длительность: {stopwatch.ElapsedMilliseconds} мс | {rpcEx.Status.Detail}");

                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                Console.WriteLine(
                    $"[EXCEPT] Ошибка вызова {context.Method.FullName} | Длительность: {stopwatch.ElapsedMilliseconds} мс | {ex.Message}");
                
                throw;
            }
        }
    }

    private bool IsTransientError(StatusCode statusCode)
    {
        return statusCode switch
        {
            StatusCode.Unavailable => true,
            StatusCode.DeadlineExceeded => true,
            StatusCode.ResourceExhausted => true,
            StatusCode.Internal => true,
            _ => false
        };
    }

    private async Task<Metadata> GetHeadersAsync<TRequest, TResponse>(
        Task<TResponse> mainTask,
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        try
        {
            await mainTask;
            var call = continuation(request, context);
            return await call.ResponseHeadersAsync;
        }
        catch
        {
            return new Metadata();
        }
    }

    private Status GetStatus(Task mainTask) => mainTask.IsFaulted ? new Status(StatusCode.Internal, "Retry failed") : Status.DefaultSuccess;
    private Metadata GetTrailers(Task mainTask) => new Metadata();
}

