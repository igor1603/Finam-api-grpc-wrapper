using Grpc.Core;
using Grpc.Tradeapi.V1.Auth;
using Auth = Grpc.Tradeapi.V1.Auth;

namespace FinamApiGrpc.ServicesClients;

public class AuthClient : AuthService.AuthServiceClient, IDisposable
{
    #region Поля
    private readonly string             _secretKey;
    private readonly string             _accountId;
    private string?                     _currentJwtToken;

    private CancellationTokenSource?    _streamCts;
    private CancellationToken           _cancellationToken;
    private readonly Action<string>     _setJwtToken;

    private readonly AuthRequest        _authRequest;
    private SubscribeJwtRenewalRequest  _subscribeJwtRenewalRequest;

    private Task?                       _jwtRenewalTask;
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
        _cancellationToken = _streamCts.Token;
        _setJwtToken = setJwtToken;
    }

    /// <summary>
    /// 1. Посылает запрос на авторизацию.
    /// 2. Если autoStartJwtRenewal = true, то вызывает StartJwtRenewalAsync - 
    /// стартовую процедуру отправления запроса на включение автоматического обновления jwt токена. 
    /// </summary>
    /// <param name="autoStartJwtRenewal"> Тип bool. Посылать ли запрос на автоматическое обновление jwt токена. </param>
    /// <returns> jwt токен </returns>
    public async Task<string> Auth(bool autoStartJwtRenewal)
    {
        var authResponse = await AuthAsync(_authRequest);
        _currentJwtToken = authResponse.Token;
#if DEBUG
            Console.WriteLine($"[Auth] Прошли авторизацию. Получили jwt токен: {authResponse.Token}"); 
#endif
        if (autoStartJwtRenewal)
        { 
#if DEBUG
            Console.WriteLine($"[Auth] Запускаем автоматическое продление jwt токена"); 
#endif
            await StartJwtRenewalAsync();
        }
        return authResponse.Token;
    }

    /// <summary>
    /// Создает задачу в новом потоке, в которой запускает метод включения автоматического обновления jwt токена - SubscribeJwtRenewal.
    /// </summary>
    /// <returns> Task.CompletedTask - задача, которая уже была успешно выполнена. </returns>
    public Task StartJwtRenewalAsync()
    {
        _jwtRenewalTask = Task.Run(() => SubscribeJwtRenewal());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Посылает запрос на подписку на поток данных сервера для автоматического обновления JWT.
    /// Обновляет текущий токен и обрабатывает переподключения с экспоненциальной задержкой.
    /// </summary>
    /// <returns>Задача, представляющая собой асинхронную операцию подписки.</returns>
    /// <exception cref="InvalidOperationException">Генерируется, когда сервер возвращает пустой поток ответа.</exception>
    public async Task SubscribeJwtRenewal()
    {
        int baseDelaySeconds = 2;
        int maxDelaySeconds = 60;
        int currentDelaySeconds = baseDelaySeconds;

        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
#if DEBUG
                Console.WriteLine("[Auth] Открытие стрима автоматического продления JWT..."); 
#endif
                using var streamingCall = SubscribeJwtRenewal(_subscribeJwtRenewalRequest);

                if (streamingCall?.ResponseStream == null)
                {
                    throw new InvalidOperationException("[SDK] Сервер Финам вернул пустой поток ответов.");
                }

                // Бесконечное чтение токенов из сети
#if DEBUG
                Console.WriteLine("[Auth] Фоновое обновление: Ожидание нового JWT-токен сессии."); 
#endif
                await foreach (var response in streamingCall.ResponseStream.ReadAllAsync(_cancellationToken))
                {
                    if (response != null && !string.IsNullOrEmpty(response.Token))
                    {
                        _currentJwtToken = response.Token;
                        _setJwtToken(_currentJwtToken);
#if DEBUG
                        Console.WriteLine($"[Auth] Фоновое обновление: Успешно применен новый JWT-токен сессии.{_currentJwtToken}");
                        Console.WriteLine($"[из Auth в Песочницу] Для продолжения - нажать любую клавишу");
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
                await Task.Delay(TimeSpan.FromSeconds(currentDelaySeconds), _cancellationToken);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, maxDelaySeconds);
            }
            catch (Exception ex)
            {
                // Общие системные ошибки
#if DEBUG
                Console.WriteLine($"[Auth] Непредвиденная ошибка в стриме обновлений: {ex.Message}"); 
#endif
                await Task.Delay(TimeSpan.FromSeconds(currentDelaySeconds), _cancellationToken);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, maxDelaySeconds);
            }
        }
    }

    /// <summary>
    /// Отменяет процесс обновления JWT и ожидает завершения задачи обновления, если она выполняется.
    /// </summary>
    /// <returns> Задача, представляющая собой асинхронную операцию.</returns>
    public async Task StopJwtRenewalAsync()
    {
        _streamCts?.Cancel();
        if (_jwtRenewalTask != null) await _jwtRenewalTask.ConfigureAwait(false);

        _streamCts?.Dispose();
        _streamCts = null;
        _jwtRenewalTask = null;
    }

    public void Dispose()
    {
        //Console.WriteLine("[SDK] Зашли в Dispose");

        _streamCts?.Cancel();
        _streamCts?.Dispose();
        //_channel.ShutdownAsync();
        //_channel?.Dispose();
    }

    //public async Task StopJwtRenewalAsync()
    //{
    //    if (_streamCts != null)
    //    {
    //        // 1. Сигнализируем об отмене
    //        _streamCts.Cancel();

    //        // 2. Ждем завершения задачи чтения стрима
    //        // Это гарантирует, что стрим полностью закрыт и ресурсы освобождены
    //        if (_jwtRenewalTask != null)
    //        {
    //            try
    //            {
    //                await _jwtRenewalTask.ConfigureAwait(false);
    //            }
    //            catch (OperationCanceledException)
    //            {
    //                // Игнорируем, так как мы сами инициировали отмену
    //            }
    //            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
    //            {
    //                // Игнорируем штатную отмену gRPC
    //            }
    //        }

    //        // 3. Очищаем ресурсы
    //        _streamCts.Dispose();
    //        _streamCts = null;
    //        _jwtRenewalTask = null;
    //    }
    //}

}
