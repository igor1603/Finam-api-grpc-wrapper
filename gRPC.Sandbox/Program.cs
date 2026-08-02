using Google.Protobuf.WellKnownTypes;
using Google.Type;
using Grpc.Tradeapi.V1.Accounts;
//using Grpc.
using Grpc.Tradeapi.V1.Auth;
using Microsoft.Extensions.Configuration;
using static Grpc.Tradeapi.V1.Auth.MDPermission.Types;

internal class Program
{
    /// <summary>
    /// Точка входа и инициализация клиента сервисов Финам
    /// </summary>
    /// <param name="args"></param>
    private static async Task Main()
    {
        try
        {
            #region Проверяем наличия файлов параметров в выходной папке проекта
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
            #region Загружаем входные параметры FinamApiGrpc из файлов
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
            #endregion

            #region Инициализируем клиента Финам grpc
            Console.WriteLine("[Песочница] Начинаем работу. Инициализируем клиента.");
            using var FinamGrpcServices = new FinamApiGrpc.FinamApiGrpc(
                targetUrl: settings.TargetUrl,
                secretKey: settings.SecretKey,
                sourceAppId: settings.SourceAppId
            );
            #endregion

            // Авторизация - AuthService
            #region Запускаем авторизацию
            Console.WriteLine("\n[Песочница] Заходим в авторизацию.");
            await FinamGrpcServices.AuthService.Auth();
            #endregion
            #region Запускаем автоматическое продление jwt токена
            Console.WriteLine("\n[Песочница] Запускаем автоматическое продление jwt токена.");
            await FinamGrpcServices.AuthService.SubscribeJwtRenewal();
            #endregion
            #region Получаем детали токена
            Console.WriteLine("\n[Песочница] Нажатие любой клавиши - переход к получению деталей токена jwt");
            Console.ReadKey();
            Console.WriteLine("\n[Песочница] Запустили получение деталей токена jwt");
            var tokenDetailsResponse = await FinamGrpcServices.AuthService.TokenDetails();
            PrintTokenDetails(tokenDetailsResponse);
            Console.WriteLine("[Песочница] Получили детали токена jwt");
            #endregion

            // Счета - AccountsService
            #region Получаем информацию по счёту
            Console.WriteLine("\n[Песочница] Нажатие любой клавиши - переход к получению информации по счету");
            Console.ReadKey();
            Console.WriteLine("\n[Песочница] Получаем информацию по счёту.");
            var accountResponse = await FinamGrpcServices.AccountsService.GetAccount("143047");
            PrintAccountInformation(accountResponse);
            Console.WriteLine($"[Песочница] Получили информацию по счёту");
            #endregion
            #region Получаем историю сделок
            Console.WriteLine("\n[Песочница] Нажатие любой клавиши - переход к получению истории сделок");
            Console.ReadKey();
            Console.WriteLine("\n[Песочница] Получаем историю сделок.");
            var interval = new Interval
            {
                StartTime = Timestamp.FromDateTime(System.DateTime.UtcNow.AddDays(-3)),
                EndTime = Timestamp.FromDateTime(System.DateTime.UtcNow)
            };
            var tradesResponse = await FinamGrpcServices.AccountsService.Trades("143047", limit: 10, interval);
            PrintTradesHistory(tradesResponse);
            #endregion
            #region Получаем историю транзакций
            Console.WriteLine("\n[Песочница] Нажатие любой клавиши - переход к получению истории транзакций");
            Console.ReadKey();
            Console.WriteLine("\n[Песочница] Получаем историю транзакций.");
            var transactionsResponse = await FinamGrpcServices.AccountsService.Transactions("143047", limit: 10);
            PrintTransactionsHistory(transactionsResponse);
            #endregion
            #region Подписка на обновления аккаунта
            Console.WriteLine("\n[Песочница] Нажатие любой клавиши - переход к подписке на обновления аккаунта");
            Console.ReadKey();
            Console.WriteLine("\n[Песочница] Подписываемся на обновления аккаунта.");
            await FinamGrpcServices.AccountsService.SubscribeAccount("143047", PrintAccountInformation);
            Console.WriteLine("[Песочница] Подписка активна. Нажмите любую клавишу, чтобы остановить.");
            Console.ReadKey();
            await FinamGrpcServices.AccountsService.UnsubscribeAccount();
            #endregion

            // Останавливка автоматического продления jwt токена
            #region Останавливаем автоматическое обновление jwt токена
            Console.WriteLine("\n[Песочница] Нажатие любой клавиши - переход к останове автоматического обновления jwt токена");
            Console.ReadKey();
            Console.WriteLine("\n[Песочница] Останавливаем автоматическое продление jwt токена");
            await FinamGrpcServices.AuthService.UnsubscribeJwtRenewal();
            #endregion

            Console.WriteLine("\n[Песочница] Нажатие любой клавиши - выход из try");
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
        #endregion

        Console.WriteLine("\n[Песочница] Завершили работу. Нажмите любую клавишу для закрытия программы");
        Console.ReadKey();
    }

    public static void PrintTokenDetails(TokenDetailsResponse details)
    {
        Console.WriteLine("\nДетали токена jwt");

        // 1. РАБОТА С ДАТАМИ (Обе даты теперь Timestamp)
        System.DateTime createdLocal = details.CreatedAt.ToDateTime().ToLocalTime();
        System.DateTime expiresLocal = details.ExpiresAt.ToDateTime().ToLocalTime();

        Console.WriteLine($"Создан (Локально): {createdLocal:yyyy-MM-dd HH:mm:ss.fff}");
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
    }

    public static void PrintAccountInformation(GetAccountResponse information)
    {
        Console.WriteLine("\nИнформация по аккаунту");

        Console.WriteLine($"ID: {information.AccountId}");
        Console.WriteLine($"Тип: {information.Type}");
        Console.WriteLine($"Статус: {information.Status}");
    }

    public static void PrintTradesHistory(TradesResponse tradesResponse)
    {
        Console.WriteLine("\nИстория сделок");

        Console.WriteLine($"Количество сделок: {tradesResponse.Trades.Count}");

        if (tradesResponse.Trades.Count == 0)
        {
            Console.WriteLine("Сделки отсутствуют.");
            return;
        }

        foreach (var trade in tradesResponse.Trades)
        {
            Console.WriteLine($"- TradeId: {trade.TradeId}");
            Console.WriteLine($"  Symbol: {trade.Symbol}");
            Console.WriteLine($"  Price: {trade.Price?.Value}");
            Console.WriteLine($"  Size: {trade.Size?.Value}");
            Console.WriteLine($"  Side: {trade.Side}");
            Console.WriteLine($"  OrderId: {trade.OrderId}");
            Console.WriteLine($"  AccountId: {trade.AccountId}");
            Console.WriteLine($"  Timestamp: {trade.Timestamp.ToDateTime().ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
        }
    }

    public static void PrintTransactionsHistory(TransactionsResponse transactionsResponse)
    {
        Console.WriteLine("\nИстория транзакций");

        Console.WriteLine($"Количество транзакций: {transactionsResponse.Transactions.Count}");

        if (transactionsResponse.Transactions.Count == 0)
        {
            Console.WriteLine("Транзакции отсутствуют.");
            return;
        }

        foreach (var transaction in transactionsResponse.Transactions)
        {
            Console.WriteLine($"- Id: {transaction.Id}");
            Console.WriteLine($"  Symbol: {transaction.Symbol}");
            Console.WriteLine($"  Category: {transaction.TransactionCategory}");
            Console.WriteLine($"  Name: {transaction.TransactionName}");
            Console.WriteLine($"  Timestamp: {transaction.Timestamp.ToDateTime().ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            if (transaction.Change != null)
            {
                Console.WriteLine($"  Change: {transaction.Change.Units} {transaction.Change.CurrencyCode}");
            }

            if (transaction.Trade != null)
            {
                Console.WriteLine($"  Trade size: {transaction.Trade.Size?.Value}");
                Console.WriteLine($"  Trade price: {transaction.Trade.Price?.Value}");
            }

            Console.WriteLine();
        }
    }

    // Шаблон нового теста

    //Console.WriteLine("\n[Песочница] Нажатие любой клавиши - ... ");
    //Console.ReadKey();

    //#region . Получаем ...
    //Console.WriteLine("\n[Песочница] Запускаем ...");
    //var accountResponse = ... ;
    //Console.WriteLine($"[Песочница] Получили ...: {}");
    //#endregion

}
