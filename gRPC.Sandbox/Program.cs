using Google.Protobuf.WellKnownTypes;
using Google.Type;
using Grpc.Tradeapi.V1;
using Grpc.Tradeapi.V1.Accounts;
using Grpc.Tradeapi.V1.Auth;
using static Grpc.Tradeapi.V1.Auth.MDPermission.Types;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FinamApiGrpc.Models.Orders;

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
                return;
            } else if (!File.Exists(settingsLocalPath))
            {
                Console.WriteLine($"Ошибка: Файл 'settings.local.json' не найден по пути: {settingsLocalPath}");
                Console.WriteLine("Убедитесь, что файл находится в папке проекта и имеет свойство 'Копировать в выходной каталог' = 'Копировать более позднюю версию'");
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
                return;
            }
            #endregion

            #region Инициализируем клиента Финам grpc
            Console.WriteLine("[Песочница] Начинаем работу. Инициализируем клиента.");

            // Настройка логгирования для Sandbox
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Information); // Здесь можно менять уровень: Trace, Debug, Information и т.д.
            });
            ILogger logger = loggerFactory.CreateLogger<Program>();

            using var FinamGrpcServices = new FinamApiGrpc.FinamApiGrpc(
                targetUrl: settings.TargetUrl,
                secretKey: settings.SecretKey,
                sourceAppId: settings.SourceAppId,
                logger: logger
            );
            #endregion
            #region Выполняем обязательные команды. Запускаем меню команд на выбор.
            var jwtRenewalStarted = false;
            try
            {
                // Обязательная последовательность выполняется без ожидания ввода.
                Console.WriteLine("\n[Песочница] Заходим в авторизацию.");
                await FinamGrpcServices.AuthService.Auth();

                Console.WriteLine("\n[Песочница] Запускаем автоматическое продление jwt токена.");
                await FinamGrpcServices.AuthService.SubscribeJwtRenewal();
                jwtRenewalStarted = true;

                Console.WriteLine("\n[Песочница] Получаем детали токена jwt.");
                //var tokenDetailsResponse = FinamGrpcServices.AuthService.TokenDetails();
                var tokenDetailsResponse = await FinamGrpcServices.AuthService.TokenDetails();
                PrintTokenDetails(tokenDetailsResponse);
                Console.WriteLine("[Песочница] Получили детали токена jwt");

                Console.WriteLine("[Песочница] Нажмите любую клавишу, чтобы перейти к выбору других команд");
                Console.ReadKey();

                await RunOptionalCommands(FinamGrpcServices);
            }
            finally
            {
                if (jwtRenewalStarted)
                {
                    Console.WriteLine("\n[Песочница] Останавливаем автоматическое продление jwt токена.");
                    try
                    {
                        await FinamGrpcServices.AuthService.UnsubscribeJwtRenewal();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Песочница] Не удалось остановить автоматическое продление jwt токена: {ex.Message}");
                    }
                }
            }
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

        Console.WriteLine("\n[Песочница] Завершили работу. Нажмите любую клавишу для закрытия программы");
        Console.ReadKey();
    }

    private static async Task RunOptionalCommands(FinamApiGrpc.FinamApiGrpc services)
    {
        const string accountId = "143047";

        while (true)
        {
            Console.WriteLine("\n--- ДОПОЛНИТЕЛЬНЫЕ КОМАНДЫ ---");
            Console.WriteLine("1. Получить информацию по счету");
            Console.WriteLine("2. Получить историю сделок");
            Console.WriteLine("3. Получить историю транзакций");
            Console.WriteLine("4. Подписаться на обновления аккаунта");
            Console.WriteLine("5. Тест PlaceOrder/CancelOrder");
            Console.WriteLine("0. Завершить работу и остановить обновление JWT");
            Console.Write("Выберите команду: ");

            var choice = Console.ReadLine()?.Trim();
            if (choice is null or "0")
            {
                return;
            }

            switch (choice)
            {
                case "1":
                    Console.WriteLine("\n[Песочница] Получаем информацию по счёту.");
                    var accountResponse = await services.AccountsService.GetAccount(accountId);
                    PrintAccountInformation(accountResponse);
                    break;
                case "2":
                    Console.WriteLine("\n[Песочница] Получаем историю сделок.");
                    var interval = new Interval
                    {
                        StartTime = Timestamp.FromDateTime(System.DateTime.UtcNow.AddDays(-7)),
                        EndTime = Timestamp.FromDateTime(System.DateTime.UtcNow)
                    };
                    var tradesResponse = await services.AccountsService.Trades(accountId, limit: 10, interval);
                    PrintTradesHistory(tradesResponse);
                    break;
                case "3":
                    Console.WriteLine("\n[Песочница] Получаем историю транзакций.");
                    var transactionsResponse = await services.AccountsService.Transactions(accountId, limit: 10);
                    PrintTransactionsHistory(transactionsResponse);
                    break;
                case "4":
                    await SubscribeAccountUntilKeyPress(services, accountId);
                    break;
                case "5":
                    await RunPlaceCancelOrderTest(services, accountId);
                    break;
                default:
                    Console.WriteLine("Неизвестная команда. Выберите номер от 0 до 5.");
                    break;
            }
        }
    }

    private static async Task SubscribeAccountUntilKeyPress(FinamApiGrpc.FinamApiGrpc services, string accountId)
    {
        Console.WriteLine("\n[Песочница] Подписываемся на обновления аккаунта.");
        await services.AccountsService.SubscribeAccount(accountId, PrintRenewalAccountInformation);
        try
        {
            Console.WriteLine("[Песочница] Подписка активна. Нажмите любую клавишу, чтобы остановить.");
            Console.ReadKey();
        }
        finally
        {
            await services.AccountsService.UnsubscribeAccount();
        }
    }
    private static async Task RunPlaceCancelOrderTest(FinamApiGrpc.FinamApiGrpc services, string accountId)
    {
        Console.WriteLine("\n[Песочница] Выставляем заявку на покупку BMZ6");
        Console.WriteLine("---------------------------------------------------------------");

        var orderRequest = new OrderRequest
        {
            Symbol = "BMZ6@RTSX",
            Quantity = 1m,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            TimeInForce = TimeInForce.Day,
            LimitPrice = 98.00m,
            ClientOrderId = "2"
        };

        var orderResponse = await services.OrdersService.PlaceOrder(accountId, orderRequest);
        PrintOrderResponse(orderResponse);
        Console.WriteLine($"[Песочница] Заявка выставлена. OrderId: {orderResponse.OrderId}");

        Console.WriteLine("\n[Песочница] Нажмите любую клавишу для отмены заявки.");
        Console.ReadKey();

        Console.WriteLine($"\n[Песочница] Отменяем заявку OrderId: {orderResponse.OrderId}");
        Console.WriteLine("---------------------------------------------------------------");

        var cancelResponse = await services.OrdersService.CancelOrder(accountId, orderResponse.OrderId);
        PrintOrderResponse(cancelResponse);
        Console.WriteLine($"[Песочница] Заявка отменена. Статус: {cancelResponse.Status}");
        Console.WriteLine("[Песочница] Тест PlaceOrder/CancelOrder завершен успешно.");
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
        Console.Write($"\nДоступные торговые счета. Всего {details.AccountIds.Count}.\nНомера: {details.AccountIds.ElementAt(0)}");
        foreach (string accountId in details.AccountIds)
        {
            if (accountId != details.AccountIds.ElementAt(0)) {
                Console.Write($", {accountId}");
            }
        }
        Console.WriteLine();

        // 3. РАБОТА СО СЛОЖНЫМ ВЛОЖЕННЫМ МАССИВОМ (RepeatedField<MDPermission>)
        Console.WriteLine($"\nРазрешения на рыночные данные (Всего: {details.MdPermissions.Count}):");
        int countIteration = 0;
        foreach (MDPermission permission in details.MdPermissions)
        {
            if (++countIteration > 1) break;
            Console.WriteLine($"  ----------------------------------------");
            Console.WriteLine($"  Биржа (MIC):      {permission.Mic}");
            Console.WriteLine($"  Страна/Континент: {permission.Country} / {permission.Continent}");
            Console.WriteLine($"  Весь мир?         {(permission.Worldwide ? "Да" : "Нет")}");
            Console.WriteLine($"  Задержка данных:  {permission.DelayMinutes} мин.");

            // РАБОТА С ENUM (QuoteLevel)
            // В C# это будет выглядеть как проверка именованных констант
            Console.Write("  Уровень стакана: ");
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
    public static void PrintRenewalAccountInformation(GetAccountResponse information)
    {
        Console.WriteLine("\nОбновление аккаунта");

        Console.WriteLine($"Идентификатор: {information.AccountId}");
        Console.WriteLine($"Доступные средства: {FormatDecimal(information.Equity)}");
        Console.WriteLine($"Нереализованная прибыль: {FormatDecimal(information.UnrealizedProfit)}");

        var forts = information.PortfolioForts;
        if (forts is not null)
        {
            Console.WriteLine("FORTS:");
            Console.WriteLine($"  Собственные средства: {FormatDecimal(forts.AvailableCash)}");
            Console.WriteLine($"  Минимальная маржа: {FormatDecimal(forts.MoneyReserved)}");
        }
        else
        {
            Console.WriteLine("FORTS: данные отсутствуют.");
        }

        Console.WriteLine("Позиции:");
        if (information.Positions.Count == 0)
        {
            Console.WriteLine("  отсутствуют.");
            return;
        }

        foreach (var position in information.Positions)
        {
            Console.WriteLine($"  Символ инструмента: {position.Symbol}");
            Console.WriteLine($"  Количество: {FormatDecimal(position.Quantity)}");
            Console.WriteLine($"  Средняя цена: {FormatDecimal(position.AveragePrice)}");
            Console.WriteLine($"  Текущая цена: {FormatDecimal(position.CurrentPrice)}");
            Console.WriteLine($"  Поддерживающее гарантийное обеспечение: {FormatDecimal(position.MaintenanceMargin)}");
            Console.WriteLine($"  Прибыль/убыток за текущий день: {FormatDecimal(position.DailyPnl)}");
            Console.WriteLine($"  Нереализованная прибыль: {FormatDecimal(position.UnrealizedPnl)}");
            Console.WriteLine();
        }
    }
    public static void PrintOrderResponse(OrderResponse response)
    {
        Console.WriteLine("\nСостояние заявки");
        Console.WriteLine($"  OrderId: {response.OrderId}");
        Console.WriteLine($"  Status: {response.Status}");
        Console.WriteLine($"  Символ: {response.Order.Symbol}");
        Console.WriteLine($"  Количество: {FormatDecimal(response.Order.Quantity)}");
        Console.WriteLine($"  Сторона: {response.Order.Side}");
        Console.WriteLine($"  Тип: {response.Order.Type}");
        Console.WriteLine($"  Срок действия: {response.Order.TimeInForce}");
        Console.WriteLine($"  LimitPrice: {FormatDecimal(response.Order.LimitPrice)}");
        Console.WriteLine($"  StopPrice: {FormatDecimal(response.Order.StopPrice)}");
        Console.WriteLine($"  StopCondition: {response.Order.StopCondition}");
        Console.WriteLine($"  ClientOrderId: {response.Order.ClientOrderId ?? "(не задано)"}");
        Console.WriteLine($"  Comment: {response.Order.Comment ?? "(не задано)"}");
        Console.WriteLine($"  TransactAt: {response.TransactAt?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "не указано"}");
        Console.WriteLine($"  AcceptAt: {response.AcceptAt?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "не указано"}");
        Console.WriteLine($"  WithdrawAt: {response.WithdrawAt?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "не указано"}");
        Console.WriteLine($"  InitialQuantity: {FormatDecimal(response.InitialQuantity)}");
        Console.WriteLine($"  ExecutedQuantity: {FormatDecimal(response.ExecutedQuantity)}");
        Console.WriteLine($"  RemainingQuantity: {FormatDecimal(response.RemainingQuantity)}");
    }

    private static string FormatDecimal(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "не указано";
    }
    private static string FormatDecimal(Google.Type.Decimal? value)
    {
        return string.IsNullOrWhiteSpace(value?.Value) ? "не указано" : value.Value;
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
