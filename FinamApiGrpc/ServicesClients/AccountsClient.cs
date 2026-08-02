using Google.Protobuf.WellKnownTypes;
using Google.Type;
using Grpc.Core;
using Grpc.Tradeapi.V1.Accounts;

namespace FinamApiGrpc.ServicesClients;

public class AccountsClient(CallInvoker invoker) : AccountsService.AccountsServiceClient(invoker), IDisposable
{
    #region Поля
    private CancellationTokenSource? _subscriptionCts;
    private Task? _subscriptionTask;
    private Action<GetAccountResponse>? _subscriptionHandler;
    #endregion

    #region Свойства
    #endregion

    /// <summary>
    /// Получает информацию по конкретному аккаунту.
    /// </summary>
    /// <returns> Информация о счёте. </returns>
    public async Task<GetAccountResponse> GetAccount(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("Невозможно запросить информацию по счёту: accountId пуст или не инициализирован.");
        }
#if DEBUG
        Console.WriteLine($"[Accounts] Запрашиваем информацию по счёту {accountId}");
#endif

        var getAccountRequest = new GetAccountRequest { AccountId = accountId };
        var response = await GetAccountAsync(getAccountRequest);

#if DEBUG
        Console.WriteLine($"[Accounts] Получили информацию по счёту {accountId}");
#endif

        return response;
    }

    /// <summary>
    /// Получает историю сделок по конкретному аккаунту.
    /// </summary>
    /// <param name="accountId"> Идентификатор аккаунта. </param>
    /// <param name="limit"> Максимальное количество сделок в ответе. </param>
    /// <param name="interval"> Опциональный интервал времени. </param>
    /// <returns> История сделок по счёту. </returns>
    public async Task<TradesResponse> Trades(string accountId, int limit = 50, Interval? interval = null)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("Невозможно запросить историю сделок: accountId пуст или не инициализирован.");
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "limit должен быть больше нуля.");
        }

#if DEBUG
        Console.WriteLine($"[Accounts] Запрашиваем историю сделок по счёту {accountId} (limit: {limit})");
#endif

        var tradesRequest = new TradesRequest
        {
            AccountId = accountId,
            Limit = limit
        };

        var effectiveInterval = interval ?? CreateDefaultInterval();
        tradesRequest.Interval = effectiveInterval;

        var response = await TradesAsync(tradesRequest);

#if DEBUG
        Console.WriteLine($"[Accounts] Получили историю сделок по счёту {accountId}");
#endif

        return response;
    }

    /// <summary>
    /// Получает историю транзакций по конкретному аккаунту.
    /// </summary>
    /// <param name="accountId"> Идентификатор аккаунта. </param>
    /// <param name="limit"> Максимальное количество транзакций в ответе. </param>
    /// <param name="interval"> Опциональный интервал времени. </param>
    /// <returns> История транзакций по счёту. </returns>
    public async Task<TransactionsResponse> Transactions(string accountId, int limit = 50, Interval? interval = null)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("Невозможно запросить историю транзакций: accountId пуст или не инициализирован.");
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "limit должен быть больше нуля.");
        }

#if DEBUG
        Console.WriteLine($"[Accounts] Запрашиваем историю транзакций по счёту {accountId} (limit: {limit})");
#endif

        var transactionsRequest = new TransactionsRequest
        {
            AccountId = accountId,
            Limit = limit
        };

        var effectiveInterval = interval ?? CreateDefaultInterval();
        transactionsRequest.Interval = effectiveInterval;

        var response = await TransactionsAsync(transactionsRequest);

#if DEBUG
        Console.WriteLine($"[Accounts] Получили историю транзакций по счёту {accountId}");
#endif

        return response;
    }

    /// <summary>
    /// Подписывается на поток обновлений информации по конкретному аккаунту.
    /// </summary>
    /// <param name="accountId"> Идентификатор аккаунта. </param>
    /// <param name="onAccountUpdate"> Обработчик каждого обновления. </param>
    /// <param name="cancellationToken"> Токен отмены подписки. </param>
    /// <returns> Задача, завершающаяся сразу после запуска подписки. </returns>
    public Task SubscribeAccount(string accountId, Action<GetAccountResponse> onAccountUpdate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("Невозможно подписаться на обновления аккаунта: accountId пуст или не инициализирован.");
        }

        if (onAccountUpdate is null)
        {
            throw new ArgumentNullException(nameof(onAccountUpdate));
        }

        if (_subscriptionTask is { IsCompleted: false })
        {
            Console.WriteLine("[Accounts] Подписка уже активна.");
            return Task.CompletedTask;
        }

#if DEBUG
        Console.WriteLine($"[Accounts] Запускаем подписку на обновления счёта {accountId}");
#endif

        _subscriptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _subscriptionHandler = onAccountUpdate;
        _subscriptionTask = Task.Run(() => StartSubscribeAccount(accountId, _subscriptionCts.Token));

        return Task.CompletedTask;
    }
    /// <summary>
    /// Останавливает активную подписку на обновления аккаунта.
    /// </summary>
    /// <returns> Задача, представляющая собой асинхронную операцию. </returns>
    public async Task UnsubscribeAccount()
    {
        _subscriptionCts?.Cancel();

        if (_subscriptionTask != null)
        {
            await _subscriptionTask.ConfigureAwait(false);
        }

        _subscriptionCts?.Dispose();
        _subscriptionCts = null;
        _subscriptionTask = null;
        _subscriptionHandler = null;
    }

    public void Dispose()
    {
        _subscriptionCts?.Cancel();
        _subscriptionCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task StartSubscribeAccount(string accountId, CancellationToken cancellationToken)
    {
        var request = new GetAccountRequest { AccountId = accountId };

        try
        {
            using var streamingCall = base.SubscribeAccount(request, cancellationToken: cancellationToken);

            if (streamingCall?.ResponseStream == null)
            {
                throw new InvalidOperationException("[Accounts] Сервер Финам вернул пустой поток ответов.");
            }

            await foreach (var response in streamingCall.ResponseStream.ReadAllAsync(cancellationToken))
            {
                if (response is not null)
                {
                    _subscriptionHandler?.Invoke(response);
                }
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
#if DEBUG
            Console.WriteLine("[Accounts] Подписка на обновления аккаунта остановлена пользователем.");
#endif
        }
        catch (RpcException rpcEx)
        {
#if DEBUG
            Console.WriteLine($"[Accounts] Ошибка подписки на обновления аккаунта: {rpcEx.StatusCode} | {rpcEx.Status.Detail}");
#endif
            throw;
        }
    }

    private static Interval CreateDefaultInterval()
    {
        var now = System.DateTime.UtcNow;
        return new Interval
        {
            StartTime = Timestamp.FromDateTime(now.AddDays(-1)),
            EndTime = Timestamp.FromDateTime(now)
        };
    }

    /// <summary>
    /// Освобождает ресурсы клиента.
    /// </summary>
}