/*
// Подключаем пространства имен Grpc.Core и Grpc.Tradeapi.V1.Auth Финама
using Grpc.Core;
//using Grpc.Net.Client;
using Grpc.Tradeapi.V1.Auth;

using Auth = Grpc.Tradeapi.V1.Auth;

namespace Finam.gRPC.Wrapper.ServicesWrappers;
public class AuthServiceWrapper
{
    #region Поля
    private readonly string                     _secretKey;
    private readonly string                     _accountId;
    private string?                             _currentJwtToken;

    private CancellationToken                   _cancellationToken;
    private CancellationTokenSource?            _streamCts;
    
    private readonly AuthRequest                _authRequest;
    private readonly SubscribeJwtRenewalRequest _subscribeJwtRenewalRequest;

    private readonly Auth.AuthService.AuthServiceClient _authClient;
    #endregion
    public AuthServiceWrapper(string secretKey, string accountId, Auth.AuthService.AuthServiceClient authClient)
    {
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _authRequest = new AuthRequest{ Secret = _secretKey, SourceAppId = _accountId };
        _subscribeJwtRenewalRequest = new Auth.SubscribeJwtRenewalRequest { Secret = _secretKey, SourceAppId = _secretKey }; 
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _streamCts = new CancellationTokenSource();
        _cancellationToken = _streamCts.Token;
    }
    public async Task<string> Auth()
    {
        var authResponse = await _authClient.AuthAsync(_authRequest);
        return authResponse.Token;
    }
    public async Task SubscribeJwtRenewal()
    {
        //var streamCts = new CancellationTokenSource();
        //var cancellationToken = streamCts.Token;
        // Начальная пауза при потере связи — 2 секунды, максимальная — 60 секунд
        int baseDelaySeconds = 2;
        int maxDelaySeconds = 60;
        int currentDelaySeconds = baseDelaySeconds;

        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine("[SDK] Открытие стрима автоматического продления JWT...");

                using var streamingCall = _authClient.SubscribeJwtRenewal(_subscribeJwtRenewalRequest);

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

    /*
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
    private async Task StartJwtRenewalStreamAsync(Auth.AuthService.AuthServiceClient authClient, CancellationToken cancellationToken)
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

                using var streamingCall = authClient.SubscribeJwtRenewal(_subscribeJwtRenewalRequest);

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
    */