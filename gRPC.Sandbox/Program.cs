using Finam.gRPC.Wrapper;
// Подключаем точное пространство имен для работы со счетами Финама
using Grpc.Tradeapi.V1.Accounts;
using Microsoft.Extensions.Configuration;

internal class Program
{
    /// <summary>
    /// Точка входа и инициализация клиента сервисов Финам
    /// </summary>
    /// <param name="args"></param>
    private static async Task Main(string[] args)
    {
        /*
        // Вставьте ваш реальный секретный ключ сессии (вида tapi_sk_...)
        string mytargetUrl = "https://api.finam.ru:443";
        string mySecretKey = "tapi_sk_3_G8TqeFTD-wXBWwo8wIrw";
        string myAccountId = "143047";
        */

        try
        {
            #region Проверка наличия файлов параметров в выходной папке проекта
            var basePath = AppContext.BaseDirectory;
            var settingsPath = Path.Combine(basePath, "settings.json");
            var settingsLocalPath = Path.Combine(basePath, "settings.local.json");

            // Проверка наличия обязательного файла
            if (!File.Exists(settingsPath))
            {
                Console.WriteLine($"Ошибка: Файл 'settings.json' не найден по пути: {settingsPath}");
                Console.WriteLine("Убедитесь, что файл находится в папке проекта и имеет свойство 'Копировать в выходной каталог' = 'Копировать более позднюю версию'");
                Console.ReadKey();
                return;
            } else if (!File.Exists(settingsLocalPath))
            {
                Console.WriteLine($"Ошибка: Файл 'settings.local.json' не найден по пути: {settingsLocalPath}");
                Console.WriteLine("Убедитесь, что файл находится в папке проекта и имеет свойство 'Копировать в выходной каталог' = 'Копировать более позднюю версию'");
                Console.ReadKey();
                return;
            }
            #endregion
            #region Загрузка входных параметров FinamClient из файлов
            var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("settings.json", optional: false)
            .AddJsonFile("settings.local.json", optional: true)
            .Build();

            var settings = config.GetSection("Connection").Get<Connection>()
                ?? throw new Exception("Секция Connection не найдена в конфигурации.");

            if (string.IsNullOrEmpty(settings.SecretKey))
            {
                Console.WriteLine("Ошибка: SecretKey не задан в settings.local.json");
                Console.ReadKey();
                return;
            }

            string mytargetUrl = settings.BaseUrl;
            string mySecretKey = settings.SecretKey;
            string myAccountId = settings.AccountId;
            #endregion

            Console.WriteLine("[Песочница] Начинаем работу. Инициализируем клиента.");
            // 1. Инициализируем наш клиент-обертку
            using var client = new FinamClient(
                targetUrl: mytargetUrl,
                secretKey: mySecretKey,
                accountId: myAccountId
            );

            // 2. Параметры StartAsync true - автоматическая авторизация, true - автоматическое фоновое продление токенов
            await client.StartAsync(
                autoAuthorization: true,
                autoJwtRenewal: true
            );
            Console.WriteLine("""
                [Песочница] Запустили фоновое продление токенов. Для проверки продления ждите 15 минут.
                Для остановки ожидания нового токена или для продолжения теста - нажмите любую клавишу
                """);

            Console.ReadKey();
            await client.StopJwtRenewalAsync();
            Console.WriteLine("[Песочница] Остановили ожидание нового токена от Финама. Продолжаем тест");

            // 3. Запрашиваем данные аккаунта
            Console.WriteLine($"[Песочница] Запустили получение идентификатора торгового счета");
            var accountRequest = new GetAccountRequest(){ AccountId = myAccountId };
            var accountResponse = await client.Accounts.GetAccountAsync(accountRequest);
            Console.WriteLine($"[Песочница] Получили идентификатор торгового счета: {accountResponse.AccountId}");

            client.Dispose();
        }
        catch (FileNotFoundException fileEx)
        {
            Console.WriteLine($"Ошибка файла: {fileEx.Message}");
        }
        catch (Grpc.Core.RpcException rpcEx)
        {
            Console.WriteLine($"[Песочница] Ошибка gRPC ({rpcEx.StatusCode}): {rpcEx.Status.Detail}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Песочница] Системная ошибка: {ex.Message}");
        }

        Console.WriteLine("[Песочница] Завершили работу. Нажмите любую клавишу для выхода.");
        Console.ReadKey();
    }
}