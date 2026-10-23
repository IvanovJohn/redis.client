## Why

Нужен собственный Redis-клиент для .NET 10, написанный с нуля поверх протокола RESP, без зависимости от StackExchange.Redis. Клиент предназначен для enterprise-сервисов, поэтому он должен быть предсказуемым под нагрузкой, переживать сбои сети, работать по TLS с ACL-авторизацией и быть наблюдаемым штатными средствами .NET (логи, метрики, трассировка).

## What Changes

- Новая библиотека `net10.0` (NuGet-пакет) с реализацией клиента Redis с нуля.
- Реализация протокола RESP2 и RESP3: потоковый парсер и сериализатор без лишних аллокаций, согласование версии через `HELLO`.
- Управление соединениями:
  - мультиплексирование и конвейерная отправка (pipelining) команд по одному соединению;
  - опциональный пул выделенных соединений для блокирующих команд;
  - автоматическое переподключение с backoff;
  - таймауты на подключение и на каждую команду, отмена через `CancellationToken`;
  - TLS (включая клиентские сертификаты);
  - `AUTH` (пароль и ACL username/password), `SELECT`, `CLIENT SETNAME`.
- Асинхронный API (async-first) с типизированными командами для generic keys, strings, hashes, lists, sets и sorted sets, а также низкоуровневый `ExecuteAsync` для произвольных команд.
- Транзакции (`MULTI`/`EXEC`/`DISCARD`, `WATCH`) и Lua-скрипты (`EVAL`/`EVALSHA` с автоматическим откатом на `EVAL` при `NOSCRIPT`).
- Pub/Sub: `SUBSCRIBE`/`PSUBSCRIBE`, `PUBLISH`, автоматическое восстановление подписок после переподключения.
- Конфигурация через строго типизированные options и строку подключения; интеграция с `Microsoft.Extensions.DependencyInjection`/`Options`.
- Наблюдаемость: `ILogger`, метрики через `System.Diagnostics.Metrics.Meter`, трассировка через `ActivitySource` (совместимо с OpenTelemetry).

### Вне рамок этого change

Каждая из этих возможностей станет отдельным change. Архитектура не должна закрывать дорогу к ним.
- Redis Cluster (топология слотов, `MOVED`/`ASK`).
- Redis Sentinel (обнаружение master и failover).
- Типизированные API для streams, geo, HyperLogLog, bitmaps и модулей (JSON, Search). До их появления эти команды доступны через `ExecuteAsync`.
- Client-side caching (`CLIENT TRACKING`).
- Синхронный (блокирующий) API.

## Capabilities

### New Capabilities
- `resp-protocol`: кодирование и декодирование RESP2/RESP3, все типы ответов, ошибки сервера, согласование протокола.
- `connection-management`: жизненный цикл соединения, мультиплексирование и pipelining, пул для блокирующих команд, переподключение, таймауты и отмена, TLS, аутентификация и handshake.
- `client-configuration`: модель options, парсинг строки подключения, валидация, регистрация в DI.
- `command-api`: типизированные async-команды для keys, strings, hashes, lists, sets и sorted sets, а также низкоуровневое выполнение произвольных команд.
- `transactions-scripting`: `MULTI`/`EXEC`/`WATCH` и выполнение Lua-скриптов с кэшированием SHA.
- `pub-sub`: подписки на каналы и паттерны, публикация, восстановление подписок после разрыва.
- `observability`: структурированные логи, метрики (латентность, ошибки, состояние соединений, размер очереди) и трассировка команд.

### Modified Capabilities
<!-- Нет: в проекте пока нет существующих spec. -->

## Impact

- **Код:** новый solution с нуля: библиотека клиента, unit-тесты и интеграционные тесты.
- **Зависимости:** `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Configuration.Abstractions` (привязка к `IConfiguration` без reflection, совместимая с NativeAOT), `Microsoft.Extensions.DependencyInjection.Abstractions`. `System.IO.Pipelines` входит в shared framework .NET 10, поэтому отдельный пакет не нужен. Других runtime-зависимостей нет.
- **Совместимость с сервером:** минимальная поддерживаемая версия — Redis 6.2.
- **Тестовая инфраструктура:** интеграционным тестам нужен настоящий Redis (через Testcontainers или docker-compose), включая конфигурации с TLS и ACL.
- **Публичный API:** новый контракт, после первого релиза к нему применяется SemVer.
