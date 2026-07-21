using Grpc.Core;
using Grpc.Tradeapi.V1.Auth;
using Auth = Grpc.Tradeapi.V1.Auth;

namespace FinamApiGrpc.ServicesClients;

public class AuthClient : Auth.AuthService.AuthServiceClient, IDisposable
{
    #region Поля
    private readonly string                     _secretKey;
    private readonly string                     _accountId;
    private string?                             _currentJwtToken;

    private CancellationTokenSource?            _streamCts;
    private readonly Action<string>             _setJwtToken;

    private readonly AuthRequest                _authRequest;
    private readonly SubscribeJwtRenewalRequest _subscribeJwtRenewalRequest;

    private Task?                               _jwtRenewalTask;
    #endregion

    /// <summary>
    /// Конструктор 
    /// </summary>
    /// <param name="secretKey"> Секретный ключ. Генерируется на сайте Финам API https://api.finam.ru/tokens/</param>
    /// <param name="accountId"> Номер счета без префикса КлФ- только цифры</param>
    /// <param name="_invoker"> CallInvoker канала</param>
    /// <param name="setJwtToken"> Делегат из ServicesClients_Wrappers, обновляющий jwt токен </param>
    /// <exception cref="ArgumentNullException">Генерируется, когда параметры имеют значение null. </exception>
    public AuthClient(string secretKey, string accountId, CallInvoker _invoker, Action<string> setJwtToken) : base(_invoker)
    {
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _authRequest = new AuthRequest { Secret = _secretKey, SourceAppId = _accountId };
        _subscribeJwtRenewalRequest = new SubscribeJwtRenewalRequest { Secret = _secretKey, SourceAppId = _secretKey };
        _streamCts = new CancellationTokenSource();
        _setJwtToken = setJwtToken;
    }

    /// <summary>
    /// 1. Посылает запрос на авторизацию.
    /// 2. Если autoStartJwtRenewal = true, то вызывает StartJwtRenewalAsync - 
    /// стартовую процедуру отправления запроса на включение автоматического обновления jwt токена. 
    /// </summary>
    /// <param name="autoStartJwtRenewal"> Тип bool. Посылать ли запрос на автоматическое обновление jwt токена. </param>
    /// <returns> jwt токен </returns>
    public async Task<string> Auth()
    {
        var authResponse = await AuthAsync(_authRequest);
        _currentJwtToken = authResponse.Token;
#if DEBUG
        Console.WriteLine($"[Auth] Прошли авторизацию");
#endif
        /*
        if (autoStartJwtRenewal)
        {
#if DEBUG
            Console.WriteLine($"[Auth] Запускаем автоматическое продление jwt токена");
#endif
            await SubscribeJwtRenewal();
        }
        return authResponse.Token;
        */
        return _currentJwtToken;
    }

    public Task<TokenDetailsResponse> TokenDetails()
    {
        return StartTokenDetails();
    }

    /// <summary>
    /// Создает задачу в новом потоке, в которой запускает метод включения 
    /// автоматического обновления jwt токена - Base.SubscribeJwtRenewal.
    /// </summary>
    /// <returns> Task.CompletedTask - задача, которая уже была успешно выполнена. </returns>
    public Task SubscribeJwtRenewal()
    {
        if ( _jwtRenewalTask == null ) {
            _jwtRenewalTask = Task.Run(() => SrartSubscribeJwtRenewal());
        } else
        {
#if true
            Console.WriteLine($"[Auth] Автоматическое продление jwt токена уже работает"); 
#endif
        }
        return Task.CompletedTask;
    }
    /// <summary>
    /// Отменяет процесс обновления JWT и ожидает завершения задачи обновления, если она выполняется.
    /// </summary>
    /// <returns> Задача, представляющая собой асинхронную операцию.</returns>
    public async Task UnsubscribeJwtRenewal()
    {
        _streamCts?.Cancel();
        if (_jwtRenewalTask != null) await _jwtRenewalTask.ConfigureAwait(false);

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
        //Console.WriteLine("[SDK] Зашли в Dispose");

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
    private async Task SrartSubscribeJwtRenewal()
    {
        int baseDelaySeconds = 2;
        int maxDelaySeconds = 60;
        int currentDelaySeconds = baseDelaySeconds;

        // Проверяем, что _streamCts не null перед использованием
        if (_streamCts == null)
        {
            throw new InvalidOperationException("_streamCts не инициализирован.");
        }

        while (!_streamCts.Token.IsCancellationRequested)
        {
            try
            {
#if DEBUG
                Console.WriteLine("[Auth] Открываем стрим автоматического продления JWT..."); 
#endif
                using var streamingCall = SubscribeJwtRenewal(_subscribeJwtRenewalRequest);

                if (streamingCall?.ResponseStream == null)
                {
                    throw new InvalidOperationException("[Auth] Сервер Финам вернул пустой поток ответов.");
                }

                // Бесконечное чтение токенов из сети
                await foreach (var response in streamingCall.ResponseStream.ReadAllAsync(_streamCts.Token))
                {
#if DEBUG
                    Console.WriteLine("[Auth] Ожидаем новый JWT-токен сессии."); 
#endif
                    if (response != null && !string.IsNullOrEmpty(response.Token))
                    {
                        _currentJwtToken = response.Token;
                        _setJwtToken(_currentJwtToken);
#if DEBUG
                        Console.WriteLine($"""[Auth] Получен и сохранен новый JWT-токен сессии.{_currentJwtToken}""");
#endif
                        // Как только получили хотя бы один успешный ответ — сбрасываем паузу переподключения к начальной
                        currentDelaySeconds = baseDelaySeconds;
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
#if DEBUG
                Console.WriteLine("[Auth] Стрим обновления JWT принудительно остановлен пользователем (Dispose)."); 
#endif
                break;
            }
            catch (RpcException rpcEx)
            {
                // Обрабатываем специфичные сетевые ошибки gRPC (брокер разорвал связь, клиринг и т.д.)
#if DEBUG
                Console.WriteLine($"[Auth] Сетевая ошибка gRPC в стриме обновлений (Код: {rpcEx.StatusCode}): {rpcEx.Status.Detail}");
#endif
                // Рассчитываем следующую паузу по экспоненте
#if DEBUG
                Console.WriteLine($"[Auth] Ожидание перед повторным подключением: {currentDelaySeconds} сек...");
#endif
                await Task.Delay(TimeSpan.FromSeconds(currentDelaySeconds), _streamCts.Token);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, maxDelaySeconds);
            }
            catch (Exception ex)
            {
                // Общие системные ошибки
#if DEBUG
                Console.WriteLine($"[Auth] Непредвиденная ошибка в стриме обновлений: {ex.Message}"); 
#endif
                await Task.Delay(TimeSpan.FromSeconds(currentDelaySeconds), _streamCts.Token);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, maxDelaySeconds);
            }
        }
    }
    private async Task<TokenDetailsResponse> StartTokenDetails()
    {
#if DEBUG
        Console.WriteLine($"[Auth] Запускаем получение деталей токена");
#endif
        // Проверяем, есть ли вообще токен в памяти
        if (string.IsNullOrEmpty(_currentJwtToken))
        {
            throw new InvalidOperationException(
                "Невозможно запросить детали токена: локальный JWT-токен пуст или еще не инициализирован.");
        }

        // 1. Формируем Protobuf запрос
        var request = new TokenDetailsRequest{ Token = _currentJwtToken };

        // 2. Вызываем базовый асинхронный gRPC-метод
        TokenDetailsResponse tokenDetailsResponse = await TokenDetailsAsync(request);

        // 3. Возвращаем полученный от Финама Protobuf-объект наружу пользователю
#if DEBUG
        Console.WriteLine($"[Auth] Получили детали jwt токена");
#endif
        return tokenDetailsResponse;
    }
}
