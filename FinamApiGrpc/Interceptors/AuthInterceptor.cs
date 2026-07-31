using Grpc.Core;
using Grpc.Core.Interceptors;

namespace FinamApiGrpc.Interceptors;

/// <summary>
/// Универсальный перехватчик gRPC-запросов для Finam Trade API.
/// </summary>
/// <param name="_getJwtTokenFunc"> Делегат, получающий jwt токен </param>
public class AuthInterceptor : Interceptor
{
    private readonly Func<string?> _getJwtTokenFunc;

    public AuthInterceptor(Func<string?> getJwtTokenFunc)
    {
        _getJwtTokenFunc = getJwtTokenFunc ?? throw new ArgumentNullException(nameof(getJwtTokenFunc));
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var newContext = ApplyAuthHeader(context);
        return continuation(request, newContext);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var newContext = ApplyAuthHeader(context);
        return continuation(request, newContext);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var newContext = ApplyAuthHeader(context);
        return continuation(newContext);
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var newContext = ApplyAuthHeader(context);
        return continuation(newContext);
    }

    /// <summary>
    /// Проверка метода и наложение авторизационных метаданных
    /// </summary>
    private ClientInterceptorContext<TRequest, TResponse> ApplyAuthHeader<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        string methodName = context.Method.FullName;

        // Исключаем сервис авторизации. В актуальном API путь выглядит как "/grpc.tradeapi.v1.auth.AuthService/..."
        if (methodName.Contains("AuthService", StringComparison.OrdinalIgnoreCase))
        {
            return context;
        }

        string? currentToken = _getJwtTokenFunc();

        if (string.IsNullOrEmpty(currentToken))
        {
            Console.WriteLine($"[AUTH] JWT-токен отсутствует для вызова {methodName}");
            throw new InvalidOperationException(
                $"Критическая ошибка SDK: попытка вызова метода '{methodName}' без предварительного получения JWT-токена.");
        }

        var metadata = context.Options.Headers ?? new Metadata();
        metadata.Add("Authorization", $"Bearer {currentToken}");

        var newOptions = context.Options.WithHeaders(metadata);

        Console.WriteLine($"[AUTH] Добавлен заголовок Authorization для {methodName}");

        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
    }
}
