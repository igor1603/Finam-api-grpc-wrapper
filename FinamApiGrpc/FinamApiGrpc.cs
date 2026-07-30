using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;

using FinamApiGrpc.Interceptors;
using FinamApiGrpc.ServicesClients;

namespace FinamApiGrpc;

public class FinamApiGrpc : IDisposable
{
    #region Поля
    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;
    private readonly string _targetUrl = string.Empty;
    private readonly string _secretKey = string.Empty;
    private readonly string _accountId = string.Empty;
    public string? _currentJwtToken = string.Empty;
    #endregion

    #region Публичные поля сервисов Финама
    public AuthClient AuthService;
    public AccountsClient AccountsService;
    #endregion

    public FinamApiGrpc(string targetUrl, string secretKey, string accountId)
    {
        #region Проверка входных параметров
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _targetUrl = targetUrl ?? throw new ArgumentNullException(nameof(targetUrl));
        #endregion

        #region Настраиваем политику автоматических повторов (Retry Policy) для Unary-запросов
        var methodConfig = new MethodConfig
        {
            Names = { MethodName.Default },
            RetryPolicy = new RetryPolicy
            {
                MaxAttempts = 5,
                InitialBackoff = TimeSpan.FromSeconds(1),
                MaxBackoff = TimeSpan.FromSeconds(5),
                BackoffMultiplier = 1.5,
                RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.Internal, StatusCode.ResourceExhausted }
            }
        };
        #endregion

        #region Инициализируем сетевой gRPC-канал с нашей конфигурацией
        _channel = GrpcChannel.ForAddress(_targetUrl, new GrpcChannelOptions
        {
            ServiceConfig = new ServiceConfig { MethodConfigs = { methodConfig } }
        });
        #endregion

        #region Инициализируем перехвадчики и связываем их с каналом
        var exceptionHandlingInterceptor = new ExceptionHandlingInterceptor();
        var logInterceptor = new LoggingInterceptor();
        var authInterceptor = new AuthInterceptor(() => _currentJwtToken);

        _invoker = _channel.Intercept(exceptionHandlingInterceptor).Intercept(logInterceptor).Intercept(authInterceptor);
        #endregion

        #region Инициализируем сервисы
        AuthService = new AuthClient(secretKey, accountId, _invoker, (token) => _currentJwtToken = token);
        AccountsService = new AccountsClient(accountId, _invoker);
        #endregion
    }

    public void Dispose()
    {
        _channel.ShutdownAsync();
        _channel?.Dispose();

        GC.SuppressFinalize(this);
    }
}
