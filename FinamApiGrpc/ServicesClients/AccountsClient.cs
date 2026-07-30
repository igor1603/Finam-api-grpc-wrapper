using Grpc.Core;
using Grpc.Tradeapi.V1.Accounts;

namespace FinamApiGrpc.ServicesClients;

public class AccountsClient : AccountsService.AccountsServiceClient, IDisposable
{
    #region Поля
    private readonly string _accountId;
    private readonly GetAccountRequest _getAccountRequest;
    #endregion

    /// <summary>
    /// Конструктор
    /// </summary>
    /// <param name="accountId"> Номер счета без префикса КлФ- только цифры </param>
    /// <param name="_invoker"> CallInvoker канала </param>
    /// <exception cref="ArgumentNullException">Генерируется, когда параметры имеют значение null. </exception>
    public AccountsClient(string accountId, CallInvoker _invoker) : base(_invoker)
    {
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _getAccountRequest = new GetAccountRequest { AccountId = _accountId };
    }

    /// <summary>
    /// Получает информацию по конкретному аккаунту.
    /// </summary>
    /// <returns> Информация о счёте. </returns>
    public async Task<GetAccountResponse> GetAccount()
    {
        if (string.IsNullOrWhiteSpace(_accountId))
        {
            throw new InvalidOperationException("Невозможно запросить информацию по счёту: accountId пуст или не инициализирован.");
        }
#if DEBUG
        Console.WriteLine($"[Accounts] Запрашиваем информацию по счёту {_accountId}");
#endif
        var response = await GetAccountAsync(_getAccountRequest);
#if DEBUG
        Console.WriteLine($"[Accounts] Получили информацию по счёту {_accountId}");
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