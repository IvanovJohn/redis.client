# Redis.Client

Асинхронный клиент Redis для .NET 10, написанный с нуля поверх протокола RESP2/RESP3.

Что внутри:
- мультиплексирование и автоматический pipelining;
- TLS и ACL;
- транзакции, Lua-скрипты, Pub/Sub;
- автоматическое переподключение;
- трассировка и метрики в формате OpenTelemetry;
- совместимость с NativeAOT.

**Требования:** .NET 10, Redis 6.2 или новее.

**Не поддерживается в этой версии:** Redis Cluster, Sentinel, client-side caching, синхронный API, Redis-совместимые серверы (Valkey, Dragonfly, KeyDB).

## Быстрый старт

```csharp
await using var redis = new RedisClient("redis://app:secret@redis.prod:6379/0");

await redis.Strings.SetAsync("user:1:name", "Alice", expiry: TimeSpan.FromMinutes(10));
var name = (string?)await redis.Strings.GetAsync("user:1:name");   // "Alice"; null, если ключа нет

await redis.Hashes.SetAsync("user:1", [("email", "alice@example.com"), ("visits", 1)]);
var visits = await redis.Hashes.IncrementAsync("user:1", "visits");
```

Клиент потокобезопасен. Создайте **один экземпляр** на приложение (на каждый сервер Redis) и используйте его отовсюду. Соединение открывается при первой команде или явно через `ConnectAsync()`, если нужно упасть на старте при недоступном Redis.

## Подключение

### Строка подключения

```
redis://[[username]:password@]host[:port][/database][?option=value&...]
rediss://...   (то же самое, но с TLS)
```

Параметры query называются как свойства `RedisClientOptions`, регистр не важен, например `rediss://app:p%40ss@redis:6380/2?clientName=orders&commandTimeout=00:00:02`. Время задаётся как `TimeSpan` (`00:00:02`) или числом миллисекунд (`2000`). Специальные символы в пароле нужно кодировать в URL-формат (`@` → `%40`). Ошибки разбора никогда не содержат пароль.

### Основные настройки

| Параметр | По умолчанию | Назначение |
|---|---|---|
| `ConnectTimeout` | 5 с | TCP, TLS и handshake |
| `CommandTimeout` | 5 с | Дедлайн каждой команды |
| `KeepAliveInterval` | 60 с | `PING` после простоя соединения |
| `ReconnectBaseDelay` / `ReconnectMaxDelay` | 100 мс / 30 с | Экспоненциальный backoff с jitter |
| `BacklogPolicy` | `Queue` | Что делать с командами, пока нет соединения |
| `MultiplexedConnectionCount` | 1 | Число общих соединений |
| `DedicatedPoolSize` | 16 | Соединения для блокирующих команд и транзакций с `WATCH` |
| `Protocol` | `Auto` | RESP3 с откатом на RESP2 |
| `SubscriptionBufferSize` / `SubscriptionOverflowPolicy` | 1024 / `DropOldest` | Буфер каждой подписки |
| `RecordCommandArguments` | `false` | Писать текст команд в трассировку |

Полный список настроек — в XML-документации `RedisClientOptions`. Некорректные настройки отклоняются при создании клиента с `OptionsValidationException`.

### TLS, ACL и ротация паролей

```csharp
var options = RedisClientOptions.FromConnectionString("rediss://redis.prod:6380");
options.SslOptions = new SslClientAuthenticationOptions
{
    ClientCertificates = [clientCertificate],                       // mTLS, если сервер требует
    CertificateChainPolicy = new X509ChainPolicy
    {
        TrustMode = X509ChainTrustMode.CustomRootTrust,
        CustomTrustStore = { corporateRootCa },
    },
};

// Пароль запрашивается при каждом подключении: ротация в Vault/Key Vault подхватывается без перезапуска.
options.CredentialsProvider = async ct =>
{
    var secret = await vault.GetSecretAsync("redis/app", ct);
    return new RedisCredentials("app", secret);
};

await using var redis = new RedisClient(options, loggerFactory);
```

Проверка сертификата сервера по умолчанию включена. Отключить её можно только явно, через `RemoteCertificateValidationCallback`.

## Внедрение зависимостей

```csharp
// appsettings.json: "Redis": { "ConnectionString": "rediss://...", "CommandTimeout": "00:00:02" }
services.AddRedisClient(configuration.GetSection("Redis"));

// Несколько серверов: keyed services
services.AddKeyedRedisClient("cache", configuration.GetSection("Redis:Cache"));
services.AddKeyedRedisClient("queue", "redis://queue:6379", o => o.DedicatedPoolSize = 64);

public sealed class OrderService([FromKeyedServices("queue")] IRedisClient queue) { ... }
```

Особенности привязки к конфигурации:
- она не использует reflection, поэтому совместима с NativeAOT;
- неизвестный ключ в секции — ошибка при старте, так что опечатка вроде `CommandTimeuot` не пройдёт незамеченной;
- клиент регистрируется как singleton и освобождается вместе с контейнером.

Для unit-тестов интерфейсы `IRedisClient`, `IStringCommands`, `IHashCommands` и остальные группы команд можно мокать по отдельности.

## Команды

Команды сгруппированы по типам данных: `Keys`, `Strings`, `Hashes`, `Lists`, `Sets`, `SortedSets`. Все методы асинхронные и принимают `CancellationToken`. Результат не зависит от протокола: RESP2 и RESP3 возвращают одно и то же.

```csharp
await redis.SortedSets.AddAsync("leaderboard", [new("alice", 120), new("bob", 95)]);
var top = await redis.SortedSets.RangeByScoreWithScoresAsync(
    "leaderboard", ScoreBound.Exclusive(100), ScoreBound.PositiveInfinity, Order.Descending);

var ttl = await redis.Keys.TimeToLiveAsync("session:42");   // Expires / NoExpiry / KeyMissing

await foreach (var key in redis.Keys.ScanAsync(pattern: "session:*", pageSize: 500)) { ... }
```

Ключи передаются как `RedisKey`, значения — как `RedisValue`. Они неявно создаются из `string` (UTF-8), `byte[]`, `ReadOnlyMemory<byte>`, `long` и `double`. Обратно значения преобразуются явным приведением: `(string?)`, `(long)`, `(double?)`, `(byte[]?)`. Отсутствующее значение — `RedisValue.Null`, а не исключение.

Для команд без типизированного API (streams, модули) есть `ExecuteAsync`:

```csharp
var id = await redis.ExecuteAsync("XADD", ["orders", "*", "id", 42]);
```

Блокирующие команды (`BLPOP`, `XREAD BLOCK` и подобные) распознаются автоматически и выполняются на выделенных соединениях. Команды, которые меняют состояние общего соединения (`SELECT`, `MULTI`, `WATCH`, `SUBSCRIBE`, `AUTH` и другие), `ExecuteAsync` отклоняет. Для них есть отдельные API.

## Транзакции

```csharp
// MULTI/EXEC: команды уходят одним блоком, чужие команды между ними не попадут.
var tx = redis.CreateTransaction();
var incremented = tx.Strings.IncrementAsync("counter");
var total = tx.Hashes.IncrementAsync("stats", "total");
await tx.ExecuteAsync();                 // только после этого завершаются задачи выше
Console.WriteLine(await incremented);

// Оптимистичная блокировка: WATCH на выделенном соединении.
await using var watched = await redis.BeginWatchedTransactionAsync(["balance"]);
var balance = (long)await watched.Connection.Strings.GetAsync("balance");
_ = watched.Strings.SetAsync("balance", balance - 30);
if (!await watched.ExecuteAsync())
{
    // ключ изменили конкуренты: ничего не выполнено, повторите попытку
}
```

Задачи команд, поставленных в транзакцию, нельзя ждать до вызова `ExecuteAsync`: они завершатся только после `EXEC`. Ошибки отдельных команд (например, `WRONGTYPE`) приходят в их задачи. `EXECABORT` бросается из `ExecuteAsync`, и в этом случае ничего не выполнено.

## Lua-скрипты

```csharp
private static readonly LuaScript TakeToken = new("""
    local tokens = tonumber(redis.call('GET', KEYS[1]) or ARGV[1])
    if tokens > 0 then redis.call('SET', KEYS[1], tokens - 1) return 1 end
    return 0
    """);

var allowed = (long)await redis.ScriptEvaluateAsync(TakeToken, ["bucket:api"], [100]) == 1;
```

Скрипт выполняется через `EVALSHA`. Если сервер не знает скрипт (`NOSCRIPT`), клиент один раз прозрачно выполняет `EVAL`. `ScriptLoadAsync` прогревает кэш на старте. Флаг `readOnly: true` использует `EVAL_RO`, для этого нужен Redis 7.0+.

## Pub/Sub

```csharp
await using var subscription = await redis.Subscriber.SubscribeAsync("orders");
await foreach (var message in subscription)
{
    Console.WriteLine($"{message.Channel}: {(string?)message.Value}");
}

// Или с обработчиком; исключения в обработчике логируются и не останавливают доставку.
await using var handled = await redis.Subscriber.SubscribePatternAsync("orders.*", async (m, ct) => await HandleAsync(m, ct));

await redis.Subscriber.PublishAsync("orders", "42");
```

Как работают подписки:
- все подписки используют одно выделенное соединение, поэтому поток сообщений не тормозит обычные команды;
- после разрыва подписки восстанавливаются автоматически, а события `ConnectionLost`/`ConnectionRestored` сообщают об окне, в котором сообщения могли быть потеряны (это семантика Redis Pub/Sub);
- у каждой подписки свой ограниченный буфер. По умолчанию медленный потребитель теряет самые старые сообщения (`DroppedMessages`, метрика `redis.client.pubsub.dropped`) и не мешает остальным.

## Семантика сбоев

Это главный раздел для эксплуатации. Клиент **никогда не повторяет команды сам**: только вызывающий знает, идемпотентна ли операция.

| Исключение | Когда | `MayHaveExecuted` |
|---|---|---|
| `RedisServerException` | Сервер вернул ошибку (`ErrorCode`: `WRONGTYPE`, `NOSCRIPT`, ...). Соединение в порядке | — |
| `RedisTimeoutException` | Дедлайн `CommandTimeout` истёк | `false`: команда не отправлялась; `true`: отправлена и могла выполниться |
| `RedisConnectionException` | Соединение не установлено или потеряно | `false`: не отправлялась; `true`: могла выполниться |
| `RedisAuthenticationException` | Неверные учётные данные (наследник `RedisConnectionException`) | `false` |
| `RedisProtocolException` | Повреждённый поток данных; соединение пересоздаётся | — |
| `OperationCanceledException` | Сработал ваш `CancellationToken` | — |

**Повторы.** Безопасно повторять, если `MayHaveExecuted == false` или если операция идемпотентна (`GET`, `SET` того же значения). Для повторов используйте Polly или `Microsoft.Extensions.Resilience`:

```csharp
var retry = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        ShouldHandle = new PredicateBuilder()
            .Handle<RedisConnectionException>(e => !e.MayHaveExecuted)
            .Handle<RedisTimeoutException>(e => !e.MayHaveExecuted),
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
    })
    .Build();

await retry.ExecuteAsync(async ct => await redis.Strings.IncrementAsync("orders:count", cancellationToken: ct));
```

**Пока соединения нет.** `BacklogPolicy.Queue` (по умолчанию) держит новые команды до переподключения, но не дольше их дедлайна. `BacklogPolicy.FailFast` сразу бросает `RedisConnectionException`: это удобно, если за Redis стоит fallback, например запрос в основную БД.

**Таймауты.** Запоздавший ответ на команду, у которой истёк дедлайн, отбрасывается и никогда не попадает к следующему вызывающему. У блокирующих команд дедлайн равен их серверному таймауту плюс `CommandTimeout`: `BLPOP` на 30 секунд не оборвётся через 5.

**Обнаружение мёртвых соединений.** На простаивающем соединении отправляется `PING` раз в `KeepAliveInterval`. Если команды ждут ответа, а с соединения ничего не приходит 2×`CommandTimeout`, отправляется контрольный `PING`. Без ответа на него соединение пересоздаётся, примерно через 3×`CommandTimeout`. Так обнаруживаются «полуоткрытые» соединения за балансировщиками и NAT. Серверная команда, молчащая дольше 3×`CommandTimeout`, тоже приведёт к переподключению.

**Head-of-line blocking.** Команды на одном соединении выполняются по очереди. Одно большое значение или медленная команда (`KEYS`, `SMEMBERS` на миллионе элементов, `HGETALL` огромного хэша) задерживает всех на этом соединении. Рекомендации:
- не храните многомегабайтные значения;
- для больших коллекций используйте `ScanAsync` вместо выборки целиком;
- при смешанной нагрузке увеличьте `MultiplexedConnectionCount` до 2–4;
- смотрите метрики `redis.client.pending.size` и `db.client.operation.duration`.

## Наблюдаемость

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Redis.Client"))
    .WithMetrics(m => m.AddMeter("Redis.Client"));
```

**Трассировка.** На каждую команду создаётся span вида `Client` по конвенциям OpenTelemetry для баз данных (`db.system.name=redis`, `db.operation.name`, `server.address`, ...). Ключи и аргументы по умолчанию в трассировку **не попадают**. `RecordCommandArguments = true` включает `db.query.text`, при этом `AUTH`/`HELLO` маскируются всегда.

**Метрики:**
- `db.client.operation.duration` — длительность команд;
- `redis.client.connections` — открытые соединения;
- `redis.client.reconnects` — переподключения;
- `redis.client.timeouts` — таймауты команд;
- `redis.client.backlog.size` и `redis.client.pending.size` — размеры очередей;
- `redis.client.dedicated_pool.wait.duration` — ожидание выделенного соединения;
- `redis.client.pubsub.dropped` — потерянные сообщения Pub/Sub.

**Логи.** Пишутся в категорию `Redis.Client`, без ключей, значений и секретов:
- `Information` — подключение и переподключение;
- `Warning` — разрыв соединения;
- `Error` — ошибки протокола и обработчиков подписок.

## Производительность

Замеры BenchmarkDotNet против StackExchange.Redis 3.4 на одном и том же Redis 7.4 (Docker, WSL2), время на одну операцию:

| Конкурентность | `GET` | `SET` |
|---|---|---|
| 1 | ≈ равно | ≈ равно |
| 16 | ≈ равно | ≈ равно |
| 256 | на 26% быстрее | на 28% быстрее |

На горячем пути протокола аллокаций нет, кроме копии самого значения. На одну команду клиент выделяет около 0,5–1 КБ, у StackExchange.Redis 0,25–0,4 КБ. Это задача для будущей оптимизации (пулинг `IValueTaskSource`).

## Разработка

```bash
dotnet build                                   # Debug: изменения публичного API — предупреждения
dotnet build -c Release                        # Release/CI: API должен совпадать с PublicAPI.*.txt
dotnet test tests/Redis.Client.Tests           # unit-, property- и fuzz-тесты (без Docker)
dotnet test tests/Redis.Client.IntegrationTests              # нужен Docker; по умолчанию Redis 7.4
REDIS_IMAGE=redis:6.2-alpine dotnet test tests/Redis.Client.IntegrationTests
SOAK_SECONDS=300 dotnet test tests/Redis.Client.IntegrationTests --filter Category=Soak
REDIS_BENCH_ENDPOINT=127.0.0.1:6379 dotnet run -c Release --project benchmarks/Redis.Client.Benchmarks
```

Изменения публичного API фиксируются в `src/Redis.Client/PublicAPI.Unshipped.txt`. Спецификации, решения и задачи лежат в `openspec/changes/dotnet-redis-client/`.
