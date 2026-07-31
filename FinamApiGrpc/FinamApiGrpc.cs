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
    // Адрес сервера Finam API gRPC
    private readonly string _targetUrl = string.Empty;
    // Секретный токен, созданный на странице https://api.finam.ru/tokens/
    private readonly string _secretKey = string.Empty;
    // Идентификатор приложения-источника запросов к серверу. Не значимая для работы сервисов строка
    private readonly string _sourceAppId = string.Empty;

    // Канал связи
    private readonly GrpcChannel _channel;
    // Перехвадчики запросов
    private readonly CallInvoker _invoker;
    #endregion
    #region Свойства
    /// <summary>
    /// gwt токен получаемый в процессе авторизации. Действителен 15 минут, затем - требуется обновление.
    /// </summary>
    public string? CurrentJwtToken { get; set; } = string.Empty;

    /// <summary>
    /// Сервис авторизации
    /// </summary>
    public AuthClient AuthService { get; init; }
    /// <summary>
    /// Сервис счетов 
    /// </summary>
    public AccountsClient AccountsService { get; init; }
    #endregion

    public FinamApiGrpc(string targetUrl, string secretKey, string sourceAppId)
    {
        #region Проверка входных параметров
        _targetUrl = targetUrl ?? throw new ArgumentNullException(nameof(targetUrl));
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _sourceAppId = sourceAppId ?? throw new ArgumentNullException(nameof(sourceAppId));
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
        var authInterceptor = new AuthInterceptor(() => CurrentJwtToken);

        _invoker = _channel.Intercept(exceptionHandlingInterceptor).Intercept(logInterceptor).Intercept(authInterceptor);
        #endregion

        #region Инициализируем сервисы
        AuthService = new AuthClient(secretKey, sourceAppId, _invoker, (token) => CurrentJwtToken = token);
        AccountsService = new AccountsClient(_invoker);
        #endregion
    }

    public void Dispose()
    {
        _channel.ShutdownAsync();
        _channel?.Dispose();

        GC.SuppressFinalize(this);
    }
}
