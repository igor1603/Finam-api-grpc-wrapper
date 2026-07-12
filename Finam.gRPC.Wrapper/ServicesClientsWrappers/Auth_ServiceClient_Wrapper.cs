using Grpc.Core;
using Grpc.Tradeapi.V1.Auth;
using System.Threading.Channels;
//using System.Threading;

namespace Finam.gRPC.Wrapper.ServicesWrappers;

public class Auth_ServiceClient_Wrapper : AuthService.AuthServiceClient, IDisposable
{
    #region Поля
    private readonly string             _secretKey;
    private readonly string             _accountId;
    private string?                     _currentJwtToken;

    private CancellationTokenSource?    _streamCts;
    private CancellationToken           _cancellationToken;

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
    /// <exception cref="ArgumentNullException">Генерируется, когда параметры имеют значение null. </exception>
    public Auth_ServiceClient_Wrapper(string secretKey, string accountId, CallInvoker _invoker) : base(_invoker)
    {
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _authRequest = new AuthRequest { Secret = _secretKey, SourceAppId = _accountId };
        _subscribeJwtRenewalRequest = new SubscribeJwtRenewalRequest { Secret = _secretKey, SourceAppId = _secretKey };
        _streamCts = new CancellationTokenSource();
        _cancellationToken = _streamCts.Token;
    }

    /// <summary>
    /// 1. Посылает запрос на авторизацию.
    /// 2. Если autoStartJwtRenewal = true, то вызывает StartJwtRenewalAsync - 
    /// стартовую процедуру отправления запроса на включение автоматического обновления jwt токена. 
    /// </summary>
    /// <param name="autoStartJwtRenewal"> Посылать ли запрос на автоматическое обновление jwt токена. </param>
    /// <returns> jwt токен </returns>
    public async Task<string> Auth(bool autoStartJwtRenewal)
    {
        var authResponse = await AuthAsync(_authRequest);
        _currentJwtToken = authResponse.Token;
        Console.WriteLine($"[SDK] Прошли авторизацию. Получили jwt токен: {authResponse.Token}");
        if (autoStartJwtRenewal) await StartJwtRenewalAsync();
        return authResponse.Token;
    }

    /// <summary>
    /// Посылает запрос на включение автоматического обновления jwt токена.
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
                Console.WriteLine("[SDK] Открытие стрима автоматического продления JWT...");

                using var streamingCall = SubscribeJwtRenewal(_subscribeJwtRenewalRequest);

                if (streamingCall?.ResponseStream == null)
                {
                    throw new InvalidOperationException("[SDK] Сервер Финам вернул пустой поток ответов.");
                }

                // Бесконечное чтение токенов из сети
                Console.WriteLine("[SDK] Фоновое обновление: Ожидание нового JWT-токен сессии.");
                await foreach (var response in streamingCall.ResponseStream.ReadAllAsync(_cancellationToken))
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
                await Task.Delay(TimeSpan.FromSeconds(currentDelaySeconds), _cancellationToken);
                currentDelaySeconds = Math.Min(currentDelaySeconds * 2, maxDelaySeconds);
            }
            catch (Exception ex)
            {
                // Общие системные ошибки
                Console.WriteLine($"[SDK] Непредвиденная ошибка в стриме обновлений: {ex.Message}");
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

    public void Dispose()
    {
        //Console.WriteLine("[SDK] Зашли в Dispose");

        _streamCts?.Cancel();
        _streamCts?.Dispose();
        //_channel.ShutdownAsync();
        //_channel?.Dispose();
    }
}
