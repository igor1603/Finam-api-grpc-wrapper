using Finam.gRPC.Wrapper;
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
        try
        {
            #region 1. Проверка наличия файлов параметров в выходной папке проекта
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
            #region 2. Загрузка входных параметров FinamClient из файлов
            var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("settings.json", optional: false)
            .AddJsonFile("settings.local.json", optional: true)
            .Build();

            var settings = config.GetSection("Finam.Api.gRPC").Get<Connection>()
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

            #region 3. Инициализируем наш клиент-обертку
            Console.WriteLine("[Песочница] Начинаем работу. Инициализируем клиента.");
            using var Services = new ServicesClients_Wrappers(
                targetUrl: mytargetUrl,
                secretKey: mySecretKey,
                accountId: myAccountId
            );
            #endregion
            #region 2. Запускаем авторизацию и автоматическое продление jwt токена
            Console.WriteLine("""
                [Песочница] Заходим в авторизацию. 
                """);
            bool autoStartJwtRenewal = true;
            await Services.AuthService.Auth(autoStartJwtRenewal);
            if (!autoStartJwtRenewal)
            {
                Console.WriteLine("""
                    [Песочница] Авторизовались.
                    Заходим в автоматическое продление токен
                    Если нажать любую клавишу автоматическое продление будет отключно.
                    """);
                await Services.AuthService.StartJwtRenewalAsync();
            }
            else
            {
                Console.WriteLine("""
                    [Песочница] Авторизовались.
                    autoStartJwtRenewal = true - автоматически запустили SubscribeJwtRenewal
                    """);
            }
            #endregion

            //Console.WriteLine("""[Песочница] Если нажать любую клавишу автоматическое продление будет отключно.""");
            //Console.WriteLine("""[Песочница] Если нажать любую клавишу, то перейдем к запуску GetAccount.""");
            Console.ReadKey();
            
            Console.WriteLine($"[Песочница] Запустили получение идентификатора торгового счета");
            var accountRequest = new GetAccountRequest() { AccountId = myAccountId };
            var accountResponse = await Services.AccountsService.GetAccountAsync(accountRequest);
            Console.WriteLine($"[Песочница] Получили идентификатор торгового счета: {accountResponse.AccountId}");
            

            #region 3. Останавливаем автоматическое обновление jwt токена
            await Services.AuthService.StopJwtRenewalAsync();
            #endregion

            Console.ReadKey();
        }
        #region catches
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
        #endregion
    }
}