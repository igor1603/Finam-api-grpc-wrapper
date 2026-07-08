using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
// Подключаем пространства имен Финама
using Grpc.Tradeapi.V1.Accounts;
using Grpc.Tradeapi.V1.Assets;
using Grpc.Tradeapi.V1.Auth;
using Grpc.Tradeapi.V1.Corporateactions;
using Grpc.Tradeapi.V1.Marketdata;
using Grpc.Tradeapi.V1.Metrics;
using Grpc.Tradeapi.V1.Orders;
using Grpc.Tradeapi.V1.Reports;

namespace Finam.gRPC.Wrapper;

/// <summary>
/// Главный клиент-управляющий для работы с Finam Trade API gRPC.
/// </summary>
public class FinamClient : IDisposable
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

    #region Свойства. Клиенты сервисов Финама
    // Сервисы из репозитория Финама
    public AccountsService.AccountsServiceClient Accounts { get; }
    public AssetsService.AssetsServiceClient Assets { get; }
    public AuthService.AuthServiceClient Auth { get; }
    public CorporateActionsService.CorporateActionsServiceClient CorporateActions { get; }
    public MarketDataService.MarketDataServiceClient MarketData { get; }
    public OrdersService.OrdersServiceClient Orders { get; }
    public ReportsService.ReportsServiceClient Reports { get; }
    public UsageMetricsService.UsageMetricsServiceClient UsageMetrics { get; }
    #endregion

    /// <summary>
    /// Конструктор класса FinamClient
    /// </summary>
    /// <param name="secretKey"> Секретный ключ </param>
    /// <param name="accountId"> Номер счета </param>
    /// <param name="targetUrl"> Адрес сервисов Finam API gRPC </param>
    public FinamClient(string targetUrl, string secretKey, string accountId)
    {
        #region Проверка входных параметров
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _targetUrl = targetUrl ?? throw new ArgumentNullException(nameof(targetUrl));
        #endregion

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
        _channel = GrpcChannel.ForAddress(_targetUrl, new GrpcChannelOptions
        {
            ServiceConfig = new ServiceConfig { MethodConfigs = { methodConfig } }
        });

        // Связываем канал с универсальным перехватчиком
        var interceptor = new FinamAuthInterceptor(() => _currentJwtToken);
        _invoker = _channel.Intercept(interceptor);

        // Инициализируем клиенты сервисов
        Accounts = new AccountsService.AccountsServiceClient(_invoker);
        Assets = new AssetsService.AssetsServiceClient(_invoker);
        Auth = new AuthService.AuthServiceClient(_invoker);
        CorporateActions = new CorporateActionsService.CorporateActionsServiceClient(_invoker);
        MarketData = new MarketDataService.MarketDataServiceClient(_invoker);
        Orders = new OrdersService.OrdersServiceClient(_invoker);
        Reports = new ReportsService.ReportsServiceClient(_invoker);
        UsageMetrics = new UsageMetricsService.UsageMetricsServiceClient(_invoker);
    }

    /// <summary>
    /// Авторизация и старт фонового обновления JWT-токенов.
    /// </summary>
    /// <param name="autoAuthorization"> Использовать ли автоматическую авторизацию </param>
    /// <param name="autoJwtRenewal"> Использовать ли автоматическое включение обновления JWT </param>
    public async Task StartAsync(bool autoAuthorization, bool autoJwtRenewal)
    {
        // Автоматическое обновление JWT невозможно без авторизации
        if (autoAuthorization)
        {
            // Запускаем авторизацию
            await AauthorizationOnlyAsync();

            // Прослушивание стрима обновления JWT возможно только после успешной авторизации
            if (autoJwtRenewal)
            {
                /*
                // Запускаем фоновую задачу прослушивания стрима автоматического обновления JWT
                _streamCts = new CancellationTokenSource();
                _ = Task.Run(() => StartJwtRenewalStreamAsync(Auth, _streamCts.Token));
                Console.WriteLine($"Включено автоматическое обновление JWT.");
                */
                await JwtRenewalOnlyAsync();
            }
        } else if (autoJwtRenewal)
        {
            Console.WriteLine($"[SDK] Для включения автоматического обновления JWT необходимо авторизоваться.");
        }
    }

    public async Task AauthorizationOnlyAsync()
    {
        var authResult = await Auth.AuthAsync(new AuthRequest
        {
            Secret = _secretKey,
            SourceAppId = _accountId
        });
        _currentJwtToken = authResult.Token;

        if (!string.IsNullOrEmpty(_currentJwtToken))
        {
            Console.WriteLine($"[SDK] Получен JWT-токен. {_currentJwtToken}");
        } else
        {
            Console.WriteLine($"[SDK] Авторизация не состоялась. JWT-токен не получен.{_currentJwtToken}");
        }

    }

    public async Task JwtRenewalOnlyAsync()
    {
        if (!string.IsNullOrEmpty(_currentJwtToken))
        {
            /*
            _streamCts = new CancellationTokenSource();
            await Task.Run(() => StartJwtRenewalStreamAsync(Auth, _streamCts.Token));
            */
            await StartJwtRenewalAsync();

            Console.WriteLine($"[SDK] Произведена попытка запуска авто обновления JWT.");
        } else
        {
            Console.WriteLine($"[SDK] JWT не был задан. Автоматическое обровление JWT не включено.");
        }

    }

    private Task StartJwtRenewalAsync()
    {
        _streamCts = new CancellationTokenSource();
        _jwtRenewalTask = Task.Run(() => StartJwtRenewalStreamAsync(Auth, _streamCts.Token));
        return Task.CompletedTask; 
    }

    public async Task StopJwtRenewalAsync()
    {
        _streamCts?.Cancel();
        if (_jwtRenewalTask != null) await _jwtRenewalTask;        
    }

    /// <summary>
    /// Фоновый поток, который непрерывно получает новые JWT-токены от сервера.
    /// </summary>
    /// <param name="authClient"> Клиент сервиса авторизации Финама </param>
    /// <param name="cancellationToken"> Токен отмены автоматческого обновления JWT </param>
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
                    SourceAppId = _accountId
                });

                if (streamingCall?.ResponseStream == null)
                {
                    throw new InvalidOperationException("[SDK] Сервер Финам вернул пустой поток ответов.");
                }

                // Бесконечное чтение токенов из сети
                Console.WriteLine("[SDK] Фоновое обновление: Ожидание нового JWT-токен сессии.");
                await foreach (var response in streamingCall.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    if (response != null && !string.IsNullOrEmpty(response.Token))
                    {
                        _currentJwtToken = response.Token;
                        Console.WriteLine($"[SDK] Фоновое обновление: Успешно применен новый JWT-токен сессии.{_currentJwtToken}");

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
        Console.WriteLine("[SDK] Зашли в Dispose");

        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _channel?.Dispose();
    }
}
