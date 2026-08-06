##### Общие положения







##### Ссылки на источники информации по Finam APE gRPC



Finam API - https://api.finam.ru

Finam API Начало работы - https://api.finam.ru/blog/start-tradeapi

Finam API блог - https://api.finam.ru/blog

Финам документация gRPC - https://api.finam.ru/docs/grpc



Репозиторий Finam API gRPC на github - https://github.com/FinamWeb/finam-trade-api

Репозиторий Finam MCP сервер на github https://github.com/FinamWeb/finam-mcp

Репозиторий моей библиотеки-обертки на github - https://github.com/igor1603/Finam-api-grpc-wrapper



Репозиторий gRPC на github - https://github.com/grpc/grpc-dotnet

https://github.com/grpc/grpc/blob/master/src/csharp/README.md



gRPC - https://grpc.io/ только vpn





##### Ссылки на описания команд сервисов Finam gRPC



Trade API

Protocol: grpc

Servers



Authentication

jwt-token (apiKey)

In: metadata







###### AuthService



* \[Auth](https://api.finam.ru/docs/grpc/auth.md): Получение JWT токена из API токена
* \[TokenDetails](https://api.finam.ru/docs/grpc/tokendetails.md): Получение информации о токене сессии
* \[SubscribeJwtRenewal](https://api.finam.ru/docs/grpc/subscribejwtrenewal.md): Подписка на обновление JWT токена. Стрим метод



###### AccountsService



\- \[GetAccount](https://api.finam.ru/docs/grpc/getaccount.md): Получение информации по конкретному аккаунту

\- \[Trades](https://api.finam.ru/docs/grpc/trades.md): Получение истории по сделкам аккаунта

\- \[Transactions](https://api.finam.ru/docs/grpc/transactions.md): Получение списка транзакций аккаунта

\- \[SubscribeAccount](https://api.finam.ru/docs/grpc/subscribeaccount.md): Подписка на информацию по аккаунту. Стрим метод



###### OrdersService



\- \[PlaceOrder](https://api.finam.ru/docs/grpc/placeorder.md): Выставление биржевой заявки

\- \[CancelOrder](https://api.finam.ru/docs/grpc/cancelorder.md): Отмена биржевой заявки

\- \[GetOrders](https://api.finam.ru/docs/grpc/getorders.md): Получение списка заявок для аккаунта

\- \[GetOrder](https://api.finam.ru/docs/grpc/getorder.md): Получение информации о конкретном ордере

\- \[SubscribeOrderTrade](https://api.finam.ru/docs/grpc/subscribeordertrade.md): Подписка на собственные заявки и сделки. Стрим метод

\- \[SubscribeOrders](https://api.finam.ru/docs/grpc/subscribeorders.md): Подписка на собственные заявки. Стрим метод

\- \[SubscribeTrades](https://api.finam.ru/docs/grpc/subscribetrades.md): Подписка на собственные сделки. Стрим метод

\- \[PlaceSLTPOrder](https://api.finam.ru/docs/grpc/placesltporder.md): Выставление SL/TP заявки



###### MarketDataService



\- \[Bars](https://api.finam.ru/docs/grpc/bars.md): Получение исторических данных по инструменту (агрегированные свечи)

\- \[LastQuote](https://api.finam.ru/docs/grpc/lastquote.md): Получение последней котировки по инструменту

\- \[OrderBook](https://api.finam.ru/docs/grpc/orderbook.md): Получение текущего стакана по инструменту

\- \[LatestTrades](https://api.finam.ru/docs/grpc/latesttrades.md): Получение списка последних сделок по инструменту

\- \[SubscribeQuote](https://api.finam.ru/docs/grpc/subscribequote.md): Подписка на котировки по инструменту. Стрим метод

\- \[SubscribeOrderBook](https://api.finam.ru/docs/grpc/subscribeorderbook.md): Подписка на стакан по инструменту. Стрим метод

\- \[SubscribeLatestTrades](https://api.finam.ru/docs/grpc/subscribelatesttrades.md): Подписка на сделки по инструменту. Стрим метод

\- \[SubscribeBars](https://api.finam.ru/docs/grpc/subscribebars.md): Подписка на агрегированные свечи. Стрим метод



###### AssetsService



\- \[Exchanges](https://api.finam.ru/docs/grpc/exchanges.md): Получение списка доступных бирж, названия и mic коды

\- \[Assets](https://api.finam.ru/docs/grpc/assets.md): Получение списка доступных для торговли инструментов, их описание

\- \[AllAssets](https://api.finam.ru/docs/grpc/allassets.md): Получение списка всех инструментов, в том числе индикативных и архивных, их описание

\- \[GetAsset](https://api.finam.ru/docs/grpc/getasset.md): Получение информации по конкретному инструменту

\- \[GetAssetParams](https://api.finam.ru/docs/grpc/getassetparams.md): Получение торговых параметров по инструменту

\- \[OptionsChain](https://api.finam.ru/docs/grpc/optionschain.md): Получение цепочки опционов для базового актива

\- \[Schedule](https://api.finam.ru/docs/grpc/schedule.md): Получение расписания торгов для инструмента

\- \[Clock](https://api.finam.ru/docs/grpc/clock.md): Получение времени на сервере

\- \[GetConstituents](https://api.finam.ru/docs/grpc/getconstituents.md): Получить состав биржевого индекса по его символу



###### UsageMetricsService



\- \[GetUsageMetrics](https://api.finam.ru/docs/grpc/getusagemetrics.md): Получение текущих метрик использования для пользователя



###### ReportsService



\- \[CreateAccountReport](https://api.finam.ru/docs/grpc/createaccountreport.md): Запустить генерацию отчета по счету за период

\- \[GetAccountReportInfo]https://api.finam.ru(/docs/grpc/getaccountreportinfo.md): Получение информации о результате генерации отчета по счету

\- \[SubscribeAccountReportInfo](https://api.finam.ru/docs/grpc/subscribeaccountreportinfo.md): Подписка на информацию о результатах генерации отчета по счету. Стрим метод



###### CorporateActionsService



\- \[GetFutureSplits](https://api.finam.ru/docs/grpc/getfuturesplits.md): Получить предстоящие события сплитов по инструменту

\- \[GetPastSplits](https://api.finam.ru/docs/grpc/getpastsplits.md): Получить историю сплитов по инструменту

\- \[GetFutureDividends](https://api.finam.ru/docs/grpc/getfuturedividends.md): Получить список предстоящих (будущих) дивидендных выплат по инструменту.

\- \[GetPastDividends](https://api.finam.ru/docs/grpc/getpastdividends.md): Получить исторические данные по выплаченным дивидендам инструмента



###### See also



\- \[Tokens](https://api.finam.ru/tokens.md): API token management

