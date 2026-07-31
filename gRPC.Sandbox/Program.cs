using Google.Api;
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

            // Авторизации - AuthService
            #region Запускаем авторизацию
            Console.WriteLine("\n[Песочница] Заходим в авторизацию.");
            await FinamGrpcServices.AuthService.Auth();
            #endregion
            #region Запускаем автоматическое продление jwt токена
            Console.WriteLine("\n[Песочница] Запускает автоматическое продление jwt токена.");
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
        DateTime createdLocal = details.CreatedAt.ToDateTime().ToLocalTime();
        DateTime expiresLocal = details.ExpiresAt.ToDateTime().ToLocalTime();

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

    // Шаблон нового теста

    //Console.WriteLine("\n[Песочница] Нажатие любой клавиши - ... ");
    //Console.ReadKey();

    //#region . Получаем ...
    //Console.WriteLine("\n[Песочница] Запускаем ...");
    //var accountResponse = ... ;
    //Console.WriteLine($"[Песочница] Получили ...: {}");
    //#endregion

}
