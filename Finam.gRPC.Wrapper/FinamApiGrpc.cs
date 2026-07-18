using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;

using Grpc.Tradeapi.V1.Auth;
using Grpc.Tradeapi.V1.Accounts;
using Grpc.Tradeapi.V1.Orders;
using Grpc.Tradeapi.V1.Marketdata;
using Grpc.Tradeapi.V1.Assets;
using Grpc.Tradeapi.V1.Metrics;
using Grpc.Tradeapi.V1.Reports;
using Grpc.Tradeapi.V1.Corporateactions;

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
    public AccountsService.AccountsServiceClient AccountsService; 
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
            Names = { MethodName.Default }, // Применяется ко всем методам по умолчанию
            RetryPolicy = new RetryPolicy
            {
                MaxAttempts = 5,                         // Максимум 5 попыток
                InitialBackoff = TimeSpan.FromSeconds(1), // Первая пауза — 1 секунда
                MaxBackoff = TimeSpan.FromSeconds(5),     // Максимальная пауза между попытками — 5 секунд
                BackoffMultiplier = 1.5,                 // Каждая следующая пауза длиннее в 1.5 раза
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

        #region Связываем канал с универсальным перехватчиком
        var logInterceptor = new LoggingInterceptor();
        var exceptionHandlingInterceptor = new ExceptionHandlingInterceptor();
        var authInterceptor = new AuthInterceptor(() => _currentJwtToken);

        _invoker = _channel.Intercept(exceptionHandlingInterceptor).Intercept(logInterceptor).Intercept(authInterceptor);
        #endregion

        #region Инициализируем сервисы
        AuthService = new AuthClient(secretKey, accountId, _invoker, (token) => _currentJwtToken = token);

        AccountsService = new AccountsService.AccountsServiceClient(_invoker); 
        #endregion
    }

    public void Dispose()
    {
        //Console.WriteLine("[SDK] Зашли в Dispose");

        //_streamCts?.Cancel();
        //_streamCts?.Dispose();
        _channel.ShutdownAsync();
        _channel?.Dispose();
    }

}
