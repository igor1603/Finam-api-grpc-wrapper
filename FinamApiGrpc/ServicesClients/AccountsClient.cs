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
    /// Освобождает ресурсы клиента.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}