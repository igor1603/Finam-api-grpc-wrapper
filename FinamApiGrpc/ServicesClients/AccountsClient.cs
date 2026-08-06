using Google.Protobuf.WellKnownTypes;
using Google.Type;
using Grpc.Core;
using Grpc.Tradeapi.V1.Accounts;

using FinamApiGrpc.Streams;

namespace FinamApiGrpc.ServicesClients;

public class AccountsClient : AccountsService.AccountsServiceClient, IDisposable
{
    #region Поля
    private CancellationTokenSource? _subscriptionCts;
    private Task? _subscriptionTask;
    private Action<GetAccountResponse>? _subscriptionHandler;

    private readonly StreamReconnectPolicy _streamReconnectPolicy;
    private readonly ServerStreamingLoop<GetAccountRequest, GetAccountResponse> _subscriptionStreamLoop;
    #endregion

    public AccountsClient(
        CallInvoker invoker,
        int reconnectBaseDelaySeconds = 2,
        int reconnectMaxDelaySeconds = 65,
        int? maxReconnectAttempts = null) : base(invoker)
    {
        _streamReconnectPolicy = new StreamReconnectPolicy
        {
            BaseDelaySeconds = reconnectBaseDelaySeconds,
            MaxDelaySeconds = reconnectMaxDelaySeconds,
            MaxAttempts = maxReconnectAttempts
        };

        _subscriptionStreamLoop = new ServerStreamingLoop<GetAccountRequest, GetAccountResponse>(
            _streamReconnectPolicy,
            runForever: true,
            logger: message =>
            {
#if DEBUG
                Console.WriteLine(message);
#endif
            });
    }

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
    /// Включает подписку не обновление информации по аккаунту.
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
    /// Выключает подписку на обновление информации по аккаунту.
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

    /// <summary>
    /// Освобождает ресурсы клиента.
    /// </summary>
    public void Dispose()
    {
        _subscriptionCts?.Cancel();
        _subscriptionCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task StartSubscribeAccount(string accountId, CancellationToken cancellationToken)
    {
        var request = new GetAccountRequest { AccountId = accountId };

        await _subscriptionStreamLoop.RunAsync(
            request,
            (req, ct) => base.SubscribeAccount(req, cancellationToken: ct),
            (response, ct) =>
            {
                if (response is not null)
                {
                    _subscriptionHandler?.Invoke(response);
                }

                return Task.CompletedTask;
            },
            cancellationToken);
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
}