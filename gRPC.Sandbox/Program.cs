using Finam.gRPC.Wrapper;
// Подключаем точное пространство имен для работы со счетами Финама
using Grpc.Tradeapi.V1.Accounts;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("=== СТАРТ ТЕСТА FINAM API SDK ===");

        // Вставьте ваш реальный секретный ключ сессии (вида tapi_sk_...)
        string mySecretKey = "tapi_sk_3_G8TqeFTD-wXBWwo8wIrw";
        string myAppId = "143047";

        try
        {
            // 1. Инициализируем наш клиент-обертку
            using var client = new FinamClient(mySecretKey, myAppId);

            // 2. Запускаем автоматическую авторизацию и фоновое продление токенов
            await client.StartAsync();

            Console.WriteLine("[Песочница] Делаем запрос GetAccountAsync...");

            // 3. Запрашиваем данные аккаунта
            var accountRequest = new GetAccountRequest(){ AccountId = myAppId };
            var accountResponse = await client.Accounts.GetAccountAsync(accountRequest);

            // Выводим идентификатор аккаунта напрямую из ответа сервера Финам
            Console.WriteLine($"[Песочница] Успех! Ответ получен.");
            Console.WriteLine($"- Идентификатор вашего торгового счета (AccountId): {accountResponse.AccountId}");
        }
        catch (Grpc.Core.RpcException rpcEx)
        {
            Console.WriteLine($"[Песочница] Ошибка gRPC ({rpcEx.StatusCode}): {rpcEx.Status.Detail}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Песочница] Системная ошибка: {ex.Message}");
        }

        Console.WriteLine("=== ТЕСТ ЗАВЕРШЕН ===");
        Console.ReadLine();
    }
}