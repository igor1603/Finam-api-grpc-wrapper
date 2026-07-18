using Microsoft.Extensions.Configuration;

using Grpc.Tradeapi.V1.Auth;
using static Grpc.Tradeapi.V1.Auth.MDPermission.Types;
using Grpc.Tradeapi.V1.Accounts;

using FinamApiGrpc; 

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
            #region 1. Проверяем наличия файлов параметров в выходной папке проекта
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
            #region 2. Загружаем входные параметры FinamApiGrpc из файлов
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
            using var Services = new FinamApiGrpc.FinamApiGrpc(
                targetUrl: mytargetUrl,
                secretKey: mySecretKey,
                accountId: myAccountId
            );
            #endregion

            #region 4. Запускаем авторизацию
            Console.WriteLine("""[Песочница] Заходим в авторизацию.""");
            await Services.AuthService.Auth();
            Console.WriteLine("""[Песочница] Авторизовались.""");
            #endregion

            #region 5. Запускаем автоматическое продление jwt токена
            Console.WriteLine("""[Песочница] Запускает автоматическое продление jwt токена.""");
            await Services.AuthService.SubscribeJwtRenewal();
            #endregion

            Console.WriteLine("""[Песочница] Нажатие любой клавиши - переход к следующему тесту""");
            Console.ReadKey();
            
            #region 6. Получаем идентификатор торгового счета
            Console.WriteLine($"[Песочница] Запустили получение идентификатора торгового счета");
            var accountRequest = new GetAccountRequest() { AccountId = myAccountId };
            var accountResponse = await Services.AccountsService.GetAccountAsync(accountRequest);
            Console.WriteLine($"[Песочница] Получили идентификатор торгового счета: {accountResponse.AccountId}");
            #endregion

            #region 7. Получаем детали токена
            Console.WriteLine($"[Песочница] Запустили получение деталей токена jwt");
            var tokenDetailsResponse = await Services.AuthService.TokenDetails();
            TokenDataProcessor.PrintActualTokenDetails( tokenDetailsResponse );
            Console.WriteLine($"[Песочница] Получили детали токена jwt");
            #endregion
            #region 7. Останавливаем автоматическое обновление jwt токена
            Console.WriteLine("""
                [Песочница] Если нажать любую клавишу, то будет отменено автоматическое
                продление jwt токена
                """);
            Console.ReadKey();
            await Services.AuthService.UnsubscribeJwtRenewal();
            Console.WriteLine($"[Песочница] : {accountResponse.AccountId}");
            #endregion

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
        #endregion

        Console.WriteLine("[Песочница] Завершили работу. Нажмите любую клавишу для выхода.");
        Console.ReadKey();
    }

    public static class TokenDataProcessor
    {
        public static void PrintActualTokenDetails(TokenDetailsResponse details)
        {
            Console.WriteLine("====== АКТУАЛЬНЫЙ АНАЛИЗ ТОКЕНА ФИНАМА ======");

            // 1. РАБОТА С ДАТАМИ (Обе даты теперь Timestamp)
            DateTime createdLocal = details.CreatedAt.ToDateTime().ToLocalTime();
            DateTime expiresLocal = details.ExpiresAt.ToDateTime().ToLocalTime();

            Console.WriteLine($"Создан (Локально): {createdLocal:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Истекает (Локально): {expiresLocal:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Режим 'Только чтение' (Readonly): {(details.Readonly ? "ДА (Торговля заблокирована)" : "НЕТ (Робот может торговать)")}");

            // 2. РАБОТА С МАССИВОМ ДОСТУПНЫХ СЧЕТОВ (RepeatedField<string>)
            Console.WriteLine($"\nДоступные торговые счета (Всего: {details.AccountIds.Count}):");
            foreach (string accountId in details.AccountIds)
            {
                Console.WriteLine($"  - Счёт: {accountId}");
            }

            // 3. РАБОТА СО СЛОЖНЫМ ВЛОЖЕННЫМ МАССИВОМ (RepeatedField<MDPermission>)
            Console.WriteLine($"\nРазрешения на рыночные данные (Всего: {details.MdPermissions.Count}):");
            foreach (MDPermission permission in details.MdPermissions)
            {
                Console.WriteLine($"  ----------------------------------------");
                Console.WriteLine($"  Биржа (MIC):      {permission.Mic}");
                Console.WriteLine($"  Страна/Континент: {permission.Country} / {permission.Continent}");
                Console.WriteLine($"  Весь мир?         {(permission.Worldwide ? "Да" : "Нет")}");
                Console.WriteLine($"  Задержка данных:  {permission.DelayMinutes} мин.");

                // РАБОТА С ENUM (QuoteLevel)
                // В C# это будет выглядеть как проверка именованных констант
                Console.Write(" Уровень стакана: ");
                switch (permission.QuoteLevel)
                {
                    case QuoteLevel.DepthOfBook:
                        Console.WriteLine("Полная глубина книги заявок (Максимальный доступ)");
                        break;
                    case QuoteLevel.DepthOfMarket:
                        Console.WriteLine("Обычный биржевой стакан (DOM)");
                        break;
                    case QuoteLevel.BestBidOffer:
                        Console.WriteLine("Только лучшая цена покупки/продажи (BBO)");
                        break;
                    case QuoteLevel.LastPrice:
                        Console.WriteLine("Только цена последней сделки");
                        break;
                    case QuoteLevel.AccessForbidden:
                        Console.WriteLine("ДОСТУП ЗАПРЕЩЕН");
                        break;
                    default:
                        Console.WriteLine($"Неизвестный статус ({permission.QuoteLevel})");
                        break;
                }
            }
            Console.WriteLine("=============================================");
        }
    }
}
