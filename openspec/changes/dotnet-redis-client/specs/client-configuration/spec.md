## ADDED Requirements

### Requirement: Модель настроек
Клиент SHALL настраиваться через класс `RedisClientOptions` со следующими параметрами и значениями по умолчанию:

| Параметр | По умолчанию |
|---|---|
| `Endpoints` | обязателен |
| `Username` | нет |
| `Password` | нет |
| `CredentialsProvider` | нет |
| `Database` | `0` |
| `ClientName` | нет |
| `Protocol` | `Auto` |
| `Ssl` | выключен |
| `SslOptions` | нет |
| `ConnectTimeout` | 5 с |
| `CommandTimeout` | 5 с |
| `KeepAliveInterval` | 60 с |
| `ReconnectBaseDelay` | 100 мс |
| `ReconnectMaxDelay` | 30 с |
| `BacklogPolicy` | `Queue` |
| `MultiplexedConnectionCount` | `1` |
| `DedicatedPoolSize` | `16` |
| `MaxBulkLength` | 512 МБ |
| `MaxNestingDepth` | `64` |
| `SubscriptionBufferSize` | `1024` |
| `SubscriptionOverflowPolicy` | `DropOldest` |
| `RecordCommandArguments` | `false` |

`Endpoints` SHALL быть списком. В этом change MUST поддерживаться ровно один endpoint.

#### Scenario: Минимальная конфигурация
- **WHEN** задан только endpoint `localhost:6379`
- **THEN** клиент работает с перечисленными значениями по умолчанию

#### Scenario: Несколько endpoint'ов
- **WHEN** в `Endpoints` указано больше одного адреса
- **THEN** валидация завершается ошибкой с сообщением, что несколько endpoint'ов пока не поддерживаются

### Requirement: Строка подключения
Клиент SHALL принимать строку подключения в формате URI:
- `redis://[[username]:password@]host[:port][/database][?параметры]`;
- `rediss://...` — то же самое, но с включённым TLS.

Порт по умолчанию — 6379. Query-параметры SHALL сопоставляться с одноимёнными опциями без учёта регистра. Значения имени пользователя и пароля MUST проходить URL-декодирование.

#### Scenario: Полная строка
- **WHEN** разбирается `rediss://app:p%40ss@redis.prod:6380/2?clientName=orders&commandTimeout=00:00:02`
- **THEN** получаются настройки: TLS включён, username `app`, password `p@ss`, host `redis.prod`, port `6380`, database `2`, `ClientName = orders`, `CommandTimeout = 2 с`

#### Scenario: Некорректная строка
- **WHEN** разбирается строка с неизвестной схемой или неизвестным параметром
- **THEN** выбрасывается `FormatException`, и в его сообщении нет пароля из строки

### Requirement: Валидация настроек
Настройки SHALL проверяться при создании клиента и при регистрации через `IValidateOptions<RedisClientOptions>`. Некорректная конфигурация MUST приводить к ошибке до первой попытки подключения. Некорректными считаются:
- пустой список endpoint'ов;
- отрицательные или нулевые таймауты;
- `Database < 0`;
- размеры пулов меньше 1;
- `ReconnectBaseDelay > ReconnectMaxDelay`;
- одновременно заданные `Password` и `CredentialsProvider`.

#### Scenario: Нулевой таймаут
- **WHEN** `CommandTimeout = TimeSpan.Zero`
- **THEN** создание клиента завершается `OptionsValidationException` с указанием параметра `CommandTimeout`

#### Scenario: Конфликт источников учётных данных
- **WHEN** заданы и `Password`, и `CredentialsProvider`
- **THEN** валидация завершается ошибкой

### Requirement: Секреты не раскрываются
Строковые представления настроек (`ToString`, отладочный вывод, сообщения исключений) MUST NOT содержать пароль.

#### Scenario: Вывод настроек в лог
- **WHEN** приложение логирует `options.ToString()`
- **THEN** вместо пароля выводится `***`

### Requirement: Регистрация в DI
Библиотека SHALL предоставлять `IServiceCollection.AddRedisClient(...)`. Метод MUST регистрировать `IRedisClient` как singleton и поддерживать три способа настройки:
- делегат;
- строка подключения;
- секция `IConfiguration`.

Клиент MUST освобождаться вместе с контейнером. Для нескольких клиентов SHALL поддерживаться `AddKeyedRedisClient(key, ...)` на базе keyed services.

#### Scenario: Настройка из конфигурации
- **WHEN** вызывается `services.AddRedisClient(configuration.GetSection("Redis"))`, и в секции заданы `ConnectionString` и `CommandTimeout`
- **THEN** `IRedisClient`, полученный из контейнера, использует эти значения

#### Scenario: Именованные клиенты
- **WHEN** зарегистрированы `AddKeyedRedisClient("cache", ...)` и `AddKeyedRedisClient("queue", ...)` с разными endpoint'ами
- **THEN** `[FromKeyedServices("cache")] IRedisClient` и `[FromKeyedServices("queue")] IRedisClient` — разные экземпляры, подключённые к своим endpoint'ам

#### Scenario: Освобождение вместе с контейнером
- **WHEN** `ServiceProvider` освобождается
- **THEN** вызывается `DisposeAsync` клиента, и соединения закрываются
