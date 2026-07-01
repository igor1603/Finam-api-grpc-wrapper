using Grpc.Core;
using Grpc.Core.Interceptors; // Критично для работы метода .Intercept()
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Tradeapi.V1.Accounts;
// Подключаем новые актуальные пространства имен Финама
using Grpc.Tradeapi.V1.Auth;
using Grpc.Tradeapi.V1.Orders; 

namespace Finam.gRPC.Wrapper;

/// <summary>
/// Главный клиент-управляющий для работы с Finam Trade API.
/// </summary>
public class FinamClient : IDisposable
{
    private const string TargetUrl = "https://api.finam.ru:443";

    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;
    private readonly string _secretKey;
    private readonly string _appId;

    private string? _currentJwtToken;
    private CancellationTokenSource? _streamCts;

    // Сервисы из актуального репозитория Финама
    public AccountsService.AccountsServiceClient Accounts { get; }
    public OrdersService.OrdersServiceClient Orders { get; }

    // Конструктор
    public FinamClient(string secretKey, string appId)
    {
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _appId = appId ?? throw new ArgumentNullException(nameof(appId));

        // Настраиваем политику автоматических повторов (Retry Policy) для Unary-запросов
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

        // Инициализируем сетевой gRPC-канал с нашей конфигурацией
        _channel = GrpcChannel.ForAddress(TargetUrl, new GrpcChannelOptions
        {
            ServiceConfig = new ServiceConfig { MethodConfigs = { methodConfig } }
        });

        // Связываем канал с универсальным перехватчиком
        var interceptor = new FinamAuthInterceptor(() => _currentJwtToken);
        _invoker = _channel.Intercept(interceptor);

        // Инициализируем клиенты сервисов
        Accounts = new AccountsService.AccountsServiceClient(_invoker);
        Orders = new OrdersService.OrdersServiceClient(_invoker);
    }

    /// <summary>
    /// Запуск клиента: первичная авторизация и старт фонового обновления JWT-токенов.
    /// </summary>
    public async Task StartAsync()
    {
        var authClient = new AuthService.AuthServiceClient(_invoker);

        Console.WriteLine("[SDK] Запрос первичного JWT-токена у AuthService...");

        var authResult = await authClient.AuthAsync(new AuthRequest
        {
            Secret = _secretKey,
            SourceAppId = _appId
        });

        _currentJwtToken = authResult.Token;
        Console.WriteLine("[SDK] Первичный JWT-токен успешно сохранен в памяти.");

        // Запускаем фоновую задачу прослушивания стрима автоматического обновления JWT
        _streamCts = new CancellationTokenSource();
        _ = Task.Run(() => StartJwtRenewalStreamAsync(authClient, _streamCts.Token));
    }

    /// <summary>
    /// Фоновый поток, который непрерывно получает новые JWT-токены от сервера.
    /// </summary>
    private async Task StartJwtRenewalStreamAsync(AuthService.AuthServiceClient authClient, CancellationToken cancellationToken)
    {
        // Начальная пауза при потере связи — 2 секунды, максимальная — 60 секунд
        int baseDelaySeconds = 2;
        int maxDelaySeconds = 60;
        int currentDelaySeconds = baseDelaySeconds;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine("[SDK] Открытие стрима автоматического продления JWT...");

                using var streamingCall = authClient.SubscribeJwtRenewal(new SubscribeJwtRenewalRequest
                {
                    Secret = _secretKey,
                    SourceAppId = _appId
                });

                if (streamingCall?.ResponseStream == null)
                {
                    throw new InvalidOperationException("Сервер Финам вернул пустой поток ответов.");
                }

                // Бесконечное чтение токенов из сети
                await foreach (var response in streamingCall.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    if (response != null && !string.IsNullOrEmpty(response.Token))
                    {
                        _currentJwtToken = response.Token;
                        Console.WriteLine("[SDK] Фоновое обновление: Успешно применен новый JWT-токен сессии.");

                        // Как только получили хотя бы один успешный ответ — сбрасываем паузу переподключения к начальной
                        currentDelaySeconds = baseDelaySeconds;
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Console.WriteLine("[SDK] Стрим обновления JWT принудительно остановлен пользователем (Dispose).");
                break;
            }
            catch (RpcException rpcEx)
            {
                // Обрабатываем специфичные сетевые ошибки gRPC (брокер разорвал связь, клиринг и т.д.)
                Console.WriteLine($"[SDK] Сетевая ошибка gRPC в стриме обновлений (Код: {rpcEx.StatusCode}): {rpcEx.Status.Detail}");

                // Рассчитываем следующую паузу по экспоненте
                Console.WriteLine($"[SDK] Ожидание перед повторным подключением: {currentDelaySeconds} сек...");
                await Task.Delay(TimeSpan.FromSeconds(currentDelaySeconds), cancellationToken);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, maxDelaySeconds);
            }
            catch (Exception ex)
            {
                // Общие системные ошибки
                Console.WriteLine($"[SDK] Непредвиденная ошибка в стриме обновлений: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(currentDelaySeconds), cancellationToken);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, maxDelaySeconds);
            }
        }
    }

    public void Dispose()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _channel?.Dispose();
    }
}
