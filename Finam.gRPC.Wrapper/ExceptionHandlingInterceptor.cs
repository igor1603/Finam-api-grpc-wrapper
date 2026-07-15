using Grpc.Core;
using Grpc.Core.Interceptors;

public class ExceptionHandlingInterceptor : Interceptor
{
    private readonly int _maxRetryCount;
    private readonly TimeSpan _retryDelay;

    public ExceptionHandlingInterceptor(int maxRetryCount = 3, TimeSpan? retryDelay = null)
    {
        _maxRetryCount = maxRetryCount;
        // По умолчанию делаем паузу 200 миллисекунд между попытками
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(200);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        // Перехватываем Task ответа, передавая туда всё необходимое для повторного вызова
        var interceptedResponseTask = ExecuteWithRetryAsync(request, context, continuation);

        // Возвращаем gRPC новый объект вызова с нашей умной асинхронной задачей
        return new AsyncUnaryCall<TResponse>(
            interceptedResponseTask,
            // Следующие свойства мы не можем получить напрямую заранее, 
            // поэтому пробрасываем их лениво через оригинальный вызов (или фабрику)
            // Но для полноценного Retry безопаснее использовать обертку, создаваемую при успешном вызове.
            // Ниже описан корректный способ проброса метаданных gRPC:
            GetHeadersAsync(interceptedResponseTask, request, context, continuation),
            () => GetStatus(interceptedResponseTask),
            () => GetTrailers(interceptedResponseTask),
            () => { });
    }

    private async Task<TResponse> ExecuteWithRetryAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        int attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                // Вызываем продолжение конвейера (идет в LoggingInterceptor -> AuthInterceptor -> Сеть)
                var call = continuation(request, context);

                // Ждем реальный результат выполнения
                return await call.ResponseAsync;
            }
            catch (RpcException rpcEx) when (IsTransientError(rpcEx.StatusCode) && attempt < _maxRetryCount)
            {
#if DEBUG
                Console.WriteLine($"[gRPC RETRY] !!! Сбой при вызове {context.Method.FullName} (Попытка {attempt} из {_maxRetryCount}). " +
                                  $"Код: {rpcEx.StatusCode}. Повтор через {_retryDelay.TotalMilliseconds} мс...");
#endif

                // Плавная пауза перед следующим возобновлением запроса
                await Task.Delay(_retryDelay);
            }
            catch (Exception)
            {
                // Если это критическая ошибка, или не gRPC ошибка, или лимит попыток исчерпан — 
                // просто пробрасываем её дальше в робота.
                throw;
            }
        }
    }

    // Критически важный метод: определяем, является ли ошибка ВРЕМЕННОЙ
    private bool IsTransientError(StatusCode statusCode)
    {
        return statusCode switch
        {
            // Сервер Финама временно недоступен (перезагрузка, проблемы на шлюзе биржи)
            StatusCode.Unavailable => true,

            // Истек таймаут gRPC-запроса (сетевые пакеты шли слишком долго)
            StatusCode.DeadlineExceeded => true,

            // Слишком много запросов в секунду (Rate Limit) — Финам просит подождать
            StatusCode.ResourceExhausted => true,

            // Внутренняя ошибка сервера (часто бывает временным сбоем кэша или БД)
            StatusCode.Internal => true,

            // Все остальные ошибки (например, Unauthenticated - неверный токен, 
            // или InvalidArgument - неверный тикер акции) повторять бессмысленно.
            _ => false
        };
    }

    // Вспомогательные заглушки для правильной сборки AsyncUnaryCall
    private async Task<Metadata> GetHeadersAsync<TRequest, TResponse>(Task<TResponse> mainTask, TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation) where TRequest : class where TResponse : class
    {
        try { 
            await mainTask; 
            var call = continuation(request, context); 
            return await call.ResponseHeadersAsync;
        }
        catch { return new Metadata(); }
    }
    private Status GetStatus(Task mainTask) => mainTask.IsFaulted ? new Status(StatusCode.Internal, "Retry failed") : Status.DefaultSuccess;
    private Metadata GetTrailers(Task mainTask) => new Metadata();
}

