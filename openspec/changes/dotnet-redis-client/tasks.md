## 1. Инфраструктура решения

- [x] 1.1 Утвердить имя пакета и корневой namespace (рабочее — `Redis.Client`) и проверить, что имя свободно в NuGet
- [x] 1.2 Создать solution и проекты: `src/Redis.Client` (`net10.0`), `tests/Redis.Client.Tests`, `tests/Redis.Client.IntegrationTests`, `benchmarks/Redis.Client.Benchmarks`
- [x] 1.3 Добавить `Directory.Build.props` со строгими настройками сборки (D14):
  - `Nullable`, `TreatWarningsAsErrors`, `IsAotCompatible`, анализаторы trimming и AOT;
  - детерминированная сборка, SourceLink.
- [x] 1.4 Подключить `Microsoft.CodeAnalysis.PublicApiAnalyzers` и пустые `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`
- [x] 1.5 Добавить runtime-зависимости из proposal (`System.IO.Pipelines` встроен в .NET 10): `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions`
- [x] 1.6 Настроить тестовую инфраструктуру: xUnit, Testcontainers с фикстурами Redis 6.2 и 7.x, отдельные фикстуры с TLS (сгенерированные сертификаты) и ACL-пользователем
- [x] 1.7 Настроить CI: сборка, unit-тесты, интеграционные тесты на матрице Redis 6.2/7.x, проверка публикации с AOT (`dotnet publish -p:PublishAot=true` тестового консольного приложения)

## 2. Базовые типы и исключения

- [x] 2.1 Реализовать `RedisKey` (readonly struct) с неявными преобразованиями из `string` (UTF-8), `byte[]` и `ReadOnlyMemory<byte>`, равенством и хэшированием
- [x] 2.2 Реализовать `RedisValue` (readonly struct):
  - неявные преобразования из `string`, `byte[]`, `ReadOnlyMemory<byte>`, `long`, `double`;
  - явные преобразования обратно, `InvalidCastException` для нечисловых значений;
  - `RedisValue.Null` и `IsNull`.
- [x] 2.3 Unit-тесты преобразований `RedisKey`/`RedisValue`: UTF-8 для не-ASCII, бинарные данные, граничные `long`/`double`
- [x] 2.4 Реализовать иерархию исключений:
  - `RedisException`;
  - `RedisServerException` с `ErrorCode` и `Message`;
  - `RedisConnectionException` и `RedisTimeoutException` с `MayHaveExecuted`;
  - `RedisAuthenticationException`, `RedisProtocolException`.

## 3. RESP: сериализация команд

- [x] 3.1 Описать внутреннюю модель команды: имя, аргументы, метаданные (позиции ключей, флаги `ReadOnly`/`Blocking`/`NoKey`) для D3 и D13
- [x] 3.2 Реализовать запись команды как RESP-массива bulk strings напрямую в `IBufferWriter<byte>` без промежуточных аллокаций
- [x] 3.3 Unit-тесты сериализации: точные байты для `SET key value`, бинарные аргументы с `\0\r\n`, UTF-8 длины, пустые аргументы

## 4. RESP: парсер

- [x] 4.1 Реализовать `RespFrame` (тип и срез `ReadOnlySequence<byte>`, дочерние элементы для агрегатов)
- [x] 4.2 Реализовать разбор типов RESP2 поверх `SequenceReader<byte>` с результатом `Incomplete` при нехватке данных
- [x] 4.3 Добавить разбор типов RESP3: null, boolean, double (`inf`/`-inf`/`nan`), big number, bulk error, verbatim, map, set, push, attribute
- [x] 4.4 Реализовать ограничения `MaxBulkLength` и `MaxNestingDepth`: проверка до аллокации, `RedisProtocolException`
- [x] 4.5 Реализовать преобразование ошибок `-`/`!` в `RedisServerException` с выделением кода ошибки
- [x] 4.6 Unit-тесты парсера по сценариям spec `resp-protocol`: побайтовая подача, несколько кадров в одном буфере, разрыв между `\r` и `\n`, вложенность, атрибуты, некорректные данные
- [x] 4.7 Property-based и fuzz-тесты парсера:
  - round-trip «сгенерированный кадр → байты → кадр» при произвольной нарезке буфера;
  - случайные байты не приводят ни к чему, кроме `RedisProtocolException`.

## 5. Настройки и строка подключения

- [x] 5.1 Реализовать `RedisClientOptions` со всеми параметрами и значениями по умолчанию из spec `client-configuration`
- [x] 5.2 Реализовать `ToString` настроек с маскированием пароля
- [x] 5.3 Реализовать разбор строки подключения:
  - схемы `redis://` и `rediss://`;
  - URL-декодирование учётных данных;
  - query-параметры без учёта регистра;
  - `FormatException` без пароля в сообщении.
- [x] 5.4 Реализовать `IValidateOptions<RedisClientOptions>` со всеми правилами валидации, включая запрет нескольких endpoint'ов в этом change
- [x] 5.5 Unit-тесты разбора строки подключения и валидации по сценариям spec

## 6. Соединение: транспорт и handshake

- [x] 6.1 Реализовать открытие TCP-соединения (`Socket`, TCP keepalive, `NoDelay`) с `ConnectTimeout` и обёртку в `PipeReader`/`PipeWriter` (D1)
- [x] 6.2 Добавить TLS через `SslStream`: `SslClientAuthenticationOptions`, SNI, клиентские сертификаты, callback валидации
- [x] 6.3 Реализовать handshake (D5):
  - `HELLO 3` с `AUTH`/`SETNAME`;
  - откат на RESP2 при ошибке `HELLO`;
  - режимы явно заданного протокола (`Protocol`);
  - `SELECT`;
  - чтение версии сервера.
- [x] 6.4 Реализовать получение учётных данных через `CredentialsProvider` при каждом подключении и `RedisAuthenticationException` при отказе в аутентификации
- [x] 6.5 Интеграционные тесты: подключение с ACL-пользователем, неверный пароль, TLS с доверенным и недоверенным сертификатом, mTLS, выбор базы, принудительный RESP2

## 7. Мультиплексор

- [x] 7.1 Реализовать writer-loop: `Channel<PendingCommand>`, вычитывание накопившихся команд пачкой, сериализация и один `FlushAsync` на пачку (D2)
- [x] 7.2 Реализовать FIFO ожиданий и reader-loop:
  - сопоставление ответов в порядке записи;
  - маршрутизация push-кадров в отдельный обработчик.
- [x] 7.3 Реализовать запись непрерывного блока команд (для транзакций без `WATCH`): чужие команды не попадают внутрь блока
- [x] 7.4 Реализовать разрыв соединения при `RedisProtocolException` и нарушении инвариантов: все ожидания завершаются ошибкой
- [x] 7.5 Реализовать `MultiplexedConnectionCount > 1` с распределением round-robin
- [x] 7.6 Тест на конкурентность: 10 000 задач с `INCR`/`GET` по своим ключам, сверка «запрос ↔ ответ»; проверка числа соединений через `CLIENT LIST`

## 8. Таймауты, отмена и backlog

- [x] 8.1 Реализовать дедлайн `CommandTimeout`:
  - снятие незаписанной команды с очереди;
  - пометка записанной команды как abandoned с отбрасыванием позднего ответа;
  - `RedisTimeoutException` с корректным `MayHaveExecuted`.
- [x] 8.2 Реализовать отмену через `CancellationToken` по тем же правилам
- [x] 8.3 Реализовать `BacklogPolicy` (`Queue`/`FailFast`) для команд, поступивших без живого соединения
- [x] 8.4 Интеграционные тесты: `DEBUG SLEEP` с последующей командой получает правильный ответ; отмена до записи не выполняет команду на сервере; поведение обеих политик backlog

## 9. Переподключение и проверка живости

- [x] 9.1 Реализовать завершение записанных команд `RedisConnectionException` (`MayHaveExecuted = true`) при разрыве, без автоповтора
- [x] 9.2 Реализовать цикл переподключения: экспоненциальный backoff с jitter в пределах `ReconnectBaseDelay`..`ReconnectMaxDelay`, повторный handshake
- [x] 9.3 Реализовать `PING` на простое с интервалом `KeepAliveInterval`; таймаут `PING` считается разрывом
- [x] 9.4 Интеграционные тесты: `CLIENT KILL` с командой в полёте, перезапуск контейнера Redis, «зависшая» сеть (пауза контейнера), ротация пароля через `CredentialsProvider`

## 10. Маршрутизация и пул выделенных соединений

- [x] 10.1 Ввести внутренний `ICommandRouter` и `SingleNodeRouter` (D13)
- [x] 10.2 Реализовать пул выделенных соединений размером `DedicatedPoolSize` с арендой, ожиданием до таймаута и сбросом состояния при возврате
- [x] 10.3 Направлять команды с флагом `Blocking` в пул, включая распознавание блокирующих команд по имени в `ExecuteAsync`
- [x] 10.4 Вычислять дедлайн блокирующей команды как её серверный таймаут плюс `CommandTimeout`
- [x] 10.5 Интеграционные тесты: `BLPOP` не задерживает `GET`; `BLPOP` через `ExecuteAsync` идёт на выделенное соединение; исчерпание пула ведёт к `RedisTimeoutException`

## 11. Клиент и произвольные команды

- [x] 11.1 Реализовать `IRedisClient`/`RedisClient`: ленивое подключение, `ConnectAsync`, свойства групп команд
- [x] 11.2 Реализовать `ExecuteAsync(command, args, ct)` и `RedisResult` с доступом к типу кадра и значениям
- [x] 11.3 Реализовать `DisposeAsync`:
  - ожидание записанных команд до `CommandTimeout`;
  - `ObjectDisposedException` для незаписанных команд и для вызовов после освобождения.
- [x] 11.4 Реализовать result-парсеры с нормализацией различий RESP2/RESP3 (map и плоский массив, double, boolean)
- [x] 11.5 Тесты: `ExecuteAsync` для `XADD` и неизвестной команды; поведение `DisposeAsync`

## 12. Типизированные команды

- [x] 12.1 `IKeyCommands`: `DEL`, `UNLINK`, `EXISTS`, `EXPIRE*`, `PEXPIRE*`, `TTL`, `PTTL`, `PERSIST`, `TYPE`, `RENAME`, `RENAMENX`, `COPY`, `TOUCH`; результат TTL различает «без срока жизни» и «ключа нет»
- [x] 12.2 `IStringCommands`: `GET`, `SET` со всеми опциями, `MGET`, `MSET`, `MSETNX`, `GETDEL`, `GETEX`, `GETRANGE`, `SETRANGE`, `APPEND`, `STRLEN`, `INCR*`/`DECR*`
- [x] 12.3 `IHashCommands`: `HGET`, `HMGET`, `HSET`, `HSETNX`, `HDEL`, `HEXISTS`, `HLEN`, `HSTRLEN`, `HGETALL`, `HKEYS`, `HVALS`, `HINCRBY`, `HINCRBYFLOAT`, `HRANDFIELD`
- [x] 12.4 `IListCommands`: `LPUSH`/`RPUSH`(`X`), `LPOP`/`RPOP` с count, `LRANGE`, `LLEN`, `LINDEX`, `LSET`, `LINSERT`, `LREM`, `LTRIM`, `LPOS`, `LMOVE`, `BLPOP`, `BRPOP`, `BLMOVE`
- [x] 12.5 `ISetCommands`: `SADD`, `SREM`, `SMEMBERS`, `SISMEMBER`, `SMISMEMBER`, `SCARD`, `SPOP`, `SRANDMEMBER`, `SMOVE`, `SINTER`/`SUNION`/`SDIFF` и их `STORE`-варианты
- [x] 12.6 `ISortedSetCommands`, часть 1: `ZADD` (`NX`/`XX`/`GT`/`LT`/`CH`), `ZINCRBY`, `ZREM`, `ZSCORE`, `ZMSCORE`, `ZRANK`, `ZREVRANK`, `ZCARD`, `ZCOUNT`, `ZPOPMIN`/`ZPOPMAX`, `BZPOPMIN`/`BZPOPMAX`
- [x] 12.7 `ISortedSetCommands`, часть 2: единый `ZRANGE` (по рангу/score/lex, `REV`, `LIMIT`, `WITHSCORES`) с типами границ (включающая, исключающая, бесконечная), `ZRANGESTORE`, `ZREMRANGEBY*`, `ZINTER`/`ZUNION`/`ZDIFF` и их `STORE`-варианты
- [x] 12.8 SCAN-семейство: `SCAN` (`MATCH`/`COUNT`/`TYPE`), `HSCAN`, `SSCAN`, `ZSCAN` как `IAsyncEnumerable<T>` с прозрачным курсором и поддержкой отмены
- [x] 12.9 XML-документация всех методов с минимальной версией Redis для команд новее 6.2
- [x] 12.10 Интеграционные тесты сценариев spec `command-api` для каждой группы, прогон на RESP2 и RESP3 с проверкой одинаковых результатов

## 13. Транзакции

- [x] 13.1 Реализовать `CreateTransaction()`: накопление команд с задачами результатов и отправку блоком `MULTI … EXEC` через мультиплексор
- [x] 13.2 Реализовать разбор ответа `EXEC`:
  - распределение результатов по задачам;
  - ошибки отдельных команд;
  - `EXECABORT` для всех задач.
- [x] 13.3 Реализовать `BeginWatchedTransactionAsync`:
  - аренда выделенного соединения и `WATCH`;
  - чтение через объект транзакции;
  - `ExecuteAsync`, возвращающий `true`/`false`;
  - очистка состояния и возврат соединения при освобождении.
- [x] 13.4 Интеграционные тесты: атомарность под конкурентной нагрузкой, `EXECABORT`, `WRONGTYPE` внутри транзакции, конфликт `WATCH`, возврат соединения без выполнения

## 14. Lua-скрипты

- [x] 14.1 Реализовать `LuaScript` с предвычисленным SHA1
- [x] 14.2 Реализовать `ScriptEvaluateAsync`: `EVALSHA` с откатом на `EVAL` при `NOSCRIPT`, раздельные `KEYS`/`ARGV`, флаг `readOnly` (`EVALSHA_RO`/`EVAL_RO`)
- [x] 14.3 Реализовать `ScriptLoadAsync`
- [x] 14.4 Интеграционные тесты: первый вызов после `SCRIPT FLUSH`, повторный вызов только через `EVALSHA` (проверка по `MONITOR` или `INFO commandstats`), ошибка внутри скрипта, `readOnly` на Redis 7.x

## 15. Pub/Sub

- [x] 15.1 Реализовать выделенное соединение подписок с ленивым созданием и разбором сообщений `message`/`pmessage` (RESP2 array и RESP3 push)
- [x] 15.2 Реализовать реестр подписок:
  - одна серверная подписка на несколько локальных;
  - `UNSUBSCRIBE`/`PUNSUBSCRIBE` при освобождении последней локальной подписки.
- [x] 15.3 Реализовать локальные подписки:
  - API `IAsyncEnumerable<RedisMessage>` и callback;
  - ограниченный буфер `SubscriptionBufferSize`;
  - политики `DropOldest`/`Wait`;
  - логирование исключений обработчиков.
- [x] 15.4 Реализовать восстановление подписок после переподключения и уведомления о разрыве и восстановлении
- [x] 15.5 Реализовать `PublishAsync` через мультиплексор
- [x] 15.6 Интеграционные тесты сценариев spec `pub-sub`: каналы, паттерны, два локальных подписчика, перезапуск Redis, переполнение буфера, изоляция медленной подписки, исключение в обработчике

## 16. Наблюдаемость

- [x] 16.1 Добавить `ActivitySource` `Redis.Client`:
  - Activity на команду, транзакцию и скрипт с атрибутами OpenTelemetry;
  - статус `Error` и `error.type` при ошибке.
- [x] 16.2 Реализовать `RecordCommandArguments` и безусловное маскирование `AUTH`/`HELLO`
- [x] 16.3 Добавить `Meter` `Redis.Client` со всеми метриками из spec `observability`
- [x] 16.4 Перевести все логи на source-generated `LoggerMessage` с уровнями из spec; убедиться, что в логах нет ключей, значений и секретов
- [x] 16.5 Тесты:
  - Activity создаётся только при наличии слушателя и связан с родительским контекстом;
  - метрики фиксируются через `MeterListener`;
  - тест-«ловушка» проверяет, что пароль и значения не попадают ни в Activity, ни в логи, ни в исключения.

## 17. Интеграция с DI

- [x] 17.1 Реализовать `AddRedisClient` с перегрузками: делегат, строка подключения, секция `IConfiguration`, а также привязку к `IOptionsMonitor`
- [x] 17.2 Реализовать `AddKeyedRedisClient(key, ...)` на keyed services
- [x] 17.3 Тесты: настройка из конфигурации, два именованных клиента, освобождение клиента вместе с `ServiceProvider`

## 18. Нагрузка и производительность

- [x] 18.1 Бенчмарки BenchmarkDotNet: парсер и сериализатор (аллокации на горячем пути), `GET`/`SET` с разной конкурентностью, сравнение со StackExchange.Redis как ориентир
- [x] 18.2 Нагрузочный soak-тест: длительная конкурентная нагрузка с периодическими `CLIENT KILL` и перезапусками Redis, сверка, что ни один ответ не ушёл не тому вызывающему

## 19. Документация и релиз

- [x] 19.1 README: быстрый старт, DI, строка подключения, TLS/ACL, транзакции, скрипты, pub/sub, наблюдаемость
- [x] 19.2 Документировать семантику сбоев: `MayHaveExecuted`, отсутствие автоповторов, рекомендации по Polly/`Microsoft.Extensions.Resilience`, head-of-line blocking и `MultiplexedConnectionCount`
- [x] 19.3 Зафиксировать публичный API в `PublicAPI.Unshipped.txt` и провести ревью
- [x] 19.4 Настроить упаковку NuGet (метаданные, SourceLink, символы) и выпустить версию `0.1.0` как prerelease — пакет собирается (`dotnet pack`), публикация в feed отложена
