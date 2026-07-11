using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Core.Interceptors;

using Finam.gRPC.Wrapper.ServicesWrappers;

namespace Finam.gRPC.Wrapper; 

public class ServicesClientsWrappers : IDisposable
{
    #region Поля
    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;
    private readonly string _targetUrl;
    private readonly string _secretKey;
    private readonly string _accountId;
    private string? _currentJwtToken;
    private CancellationTokenSource? _streamCts;
    private Task? _jwtRenewalTask;
    #endregion

    public Auth_ServiceClient_Wrapper AuthService;

    public ServicesClientsWrappers(string targetUrl, string secretKey, string accountId)
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
        var interceptor = new FinamAuthInterceptor(() => _currentJwtToken);
        _invoker = _channel.Intercept(interceptor);

        AuthService = new Auth_ServiceClient_Wrapper(secretKey, accountId, _invoker);
        #endregion
    }
    public void Dispose()
    {
        //Console.WriteLine("[SDK] Зашли в Dispose");

        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _channel.ShutdownAsync();
        _channel?.Dispose();
    }

}
