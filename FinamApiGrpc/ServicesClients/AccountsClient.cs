using Google.Protobuf.WellKnownTypes;
using Google.Type;
using Grpc.Core;
using Grpc.Tradeapi.V1.Accounts;

namespace FinamApiGrpc.ServicesClients;

public class AccountsClient(CallInvoker invoker) : AccountsService.AccountsServiceClient(invoker), IDisposable
{
    #region Поля
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
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}