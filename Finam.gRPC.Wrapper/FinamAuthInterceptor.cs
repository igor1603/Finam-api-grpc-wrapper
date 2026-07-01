using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Finam.gRPC.Wrapper;

/// <summary>
/// Универсальный перехватчик gRPC-запросов для Finam Trade API.
/// </summary>
public class FinamAuthInterceptor : Interceptor
{
    private readonly Func<string?> _getJwtTokenFunc;

    public FinamAuthInterceptor(Func<string?> getJwtTokenFunc)
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
        if (methodName.Contains("AuthService"))
        {
            return context;
        }

        string? currentToken = _getJwtTokenFunc();

        if (string.IsNullOrEmpty(currentToken))
        {
            throw new InvalidOperationException(
                $"Критическая ошибка SDK: Попытка вызова метода '{methodName}' без предварительного получения JWT-токена.");
        }

        var metadata = context.Options.Headers ?? new Metadata();
        metadata.Add("Authorization", $"Bearer {currentToken}");

        var newOptions = context.Options.WithHeaders(metadata);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
    }
}
