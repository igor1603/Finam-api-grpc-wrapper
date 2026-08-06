using FinamApiGrpc.Streams;
using Grpc.Core;
using Grpc.Tradeapi.V1.Auth;
using Auth = Grpc.Tradeapi.V1.Auth;

namespace FinamApiGrpc.ServicesClients;

public class AuthClient : AuthService.AuthServiceClient, IDisposable
{
    #region Поля
    private readonly string _secretKey;
    private readonly string _sourceAppId;
    private string? _currentJwtToken;

    private CancellationTokenSource? _streamCts;
    private readonly Action<string> _setJwtToken;

    private readonly AuthRequest _authRequest;
    private readonly SubscribeJwtRenewalRequest _subscribeJwtRenewalRequest;

    private Task? _jwtRenewalTask;
    private readonly ServerStreamingLoop<SubscribeJwtRenewalRequest, SubscribeJwtRenewalResponse> _jwtRenewalStreamLoop;
    #endregion

    /// <summary>
    /// Конструктор 
    /// </summary>
    /// <param name="secretKey"> Секретный ключ. Генерируется на сайте Финам API https://api.finam.ru/tokens/</param>
    /// <param name="sourceAppId"> Номер счета без префикса КлФ- только цифры</param>
    /// <param name="invoker"> CallInvoker канала</param>
    /// <param name="setJwtToken"> Делегат из FinamApiGrpc, обновляющий jwt токен </param>
    /// <exception cref="ArgumentNullException">Генерируется, когда параметры имеют значение null. </exception>
    public AuthClient(string secretKey, string sourceAppId, CallInvoker invoker, Action<string> setJwtToken) : base(invoker)
    {
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _sourceAppId = sourceAppId ?? throw new ArgumentNullException(nameof(sourceAppId));
        _authRequest = new AuthRequest { Secret = _secretKey, SourceAppId = _sourceAppId };
        _subscribeJwtRenewalRequest = new SubscribeJwtRenewalRequest { Secret = _secretKey, SourceAppId = _sourceAppId };
        _streamCts = new CancellationTokenSource();
        _setJwtToken = setJwtToken;

        _jwtRenewalStreamLoop = new ServerStreamingLoop<SubscribeJwtRenewalRequest, SubscribeJwtRenewalResponse>(
            new StreamReconnectPolicy
            {
                BaseDelaySeconds = 2,
                MaxDelaySeconds = 65
            },
            runForever: true,
            logger: message =>
            {
#if DEBUG
                Console.WriteLine(message);
#endif
            });
    }

    /// <summary>
    /// Посылает запрос на авторизацию.
    /// <returns> jwt токен </returns>
    public async Task<string> Auth()
    {
        var authResponse = await AuthAsync(_authRequest);
        _currentJwtToken = authResponse.Token;
        _setJwtToken(_currentJwtToken);
#if DEBUG
        Console.WriteLine($"[Auth] Прошли авторизацию");
#endif
        return _currentJwtToken;
    }

    /// <summary>
    /// Посылает запрос на получение деталей jwt токена. 
    /// </summary>
    /// <returns></returns>
    public Task<TokenDetailsResponse> TokenDetails()
    {
        return StartTokenDetails();
    }

    /// <summary>
    /// Включает подписку на автоматическое обновление jwt токена.
    /// </summary>
    /// <returns> Task.CompletedTask - задача, которая уже была успешно выполнена. </returns>
    public Task SubscribeJwtRenewal()
    {
        if (_jwtRenewalTask == null)
        {
            _jwtRenewalTask = Task.Run(() => StartSubscribeJwtRenewal());
        }
        else
        {
#if DEBUG
            Console.WriteLine("[Auth] Автоматическое продление jwt токена уже работает");
#endif
        }

        return Task.CompletedTask;
    }
    /// <summary>
    /// Выключает подписку на обновление JWT.
    /// </summary>
    /// <returns> Задача, представляющая собой асинхронную операцию.</returns>
    public async Task UnsubscribeJwtRenewal()
    {
        _streamCts?.Cancel();

        if (_jwtRenewalTask != null)
        {
            await _jwtRenewalTask.ConfigureAwait(false);
        }

        _streamCts?.Dispose();
        _streamCts = null;
        _jwtRenewalTask = null;
    }

    /// <summary>
    /// Освобождает ресурсы стрима.
    /// </summary>
    /// <remarks>
    /// Вызовите, когда экземпляр больше не нужен, чтобы отменить текущие операции и освободить 
    /// ресурсы
    /// </remarks>
    public void Dispose()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Посылает запрос на подписку на поток данных сервера для автоматического обновления JWT.
    /// Обновляет текущий токен и обрабатывает переподключения с экспоненциальной задержкой.
    /// </summary>
    /// <returns>Задача, представляющая собой асинхронную операцию подписки.</returns>
    /// <exception cref="InvalidOperationException">Генерируется, когда сервер возвращает пустой поток ответа.</exception>
    private async Task StartSubscribeJwtRenewal()
    {
        if (_streamCts == null)
        {
            throw new InvalidOperationException("_streamCts не инициализирован.");
        }

        await _jwtRenewalStreamLoop.RunAsync(
            _subscribeJwtRenewalRequest,
            (request, cancellationToken) => base.SubscribeJwtRenewal(request, cancellationToken: cancellationToken),
            (response, cancellationToken) =>
            {
                if (response != null && !string.IsNullOrEmpty(response.Token))
                {
                    _currentJwtToken = response.Token;
                    _setJwtToken(_currentJwtToken);
#if DEBUG
                    Console.WriteLine($"[Auth] Получен и сохранен новый JWT-токен сессии.{_currentJwtToken}");
#endif
                }

                return Task.CompletedTask;
            },
            _streamCts.Token
        );
    }
    private async Task<TokenDetailsResponse> StartTokenDetails()
    {
#if DEBUG
        Console.WriteLine("[Auth] Запускаем получение деталей токена");
#endif

        if (string.IsNullOrEmpty(_currentJwtToken))
        {
            throw new InvalidOperationException(
                "Невозможно запросить детали токена: локальный JWT-токен пуст или еще не инициализирован.");
        }

        var request = new TokenDetailsRequest { Token = _currentJwtToken };

        TokenDetailsResponse tokenDetailsResponse = await TokenDetailsAsync(request);

#if DEBUG
        Console.WriteLine("[Auth] Получили детали jwt токена");
#endif

        return tokenDetailsResponse;
    }
}
