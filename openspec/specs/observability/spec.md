# observability Specification

## Purpose
Трассировка, метрики и логи клиента по соглашениям OpenTelemetry без утечки секретов.
## Requirements
### Requirement: Трассировка команд
Клиент SHALL создавать `Activity` через `ActivitySource` с именем `Redis.Client` для каждой команды пользователя. Транзакция и скрипт считаются одной командой. Activity MUST иметь вид `Client` и атрибуты по семантическим конвенциям OpenTelemetry для баз данных:
- `db.system.name = redis`;
- `db.operation.name` — имя команды;
- `db.namespace` — номер базы;
- `server.address` и `server.port`.

При ошибке статус Activity MUST быть `Error` с типом исключения в `error.type`.

#### Scenario: Успешная команда
- **WHEN** подключён `ActivityListener` на `Redis.Client`, и выполняется `GET k`
- **THEN** создаётся Activity с `db.operation.name = GET` и статусом `Ok`

#### Scenario: Ошибка сервера
- **WHEN** команда завершилась `RedisServerException` с кодом `WRONGTYPE`
- **THEN** Activity имеет статус `Error`, а `error.type` содержит `WRONGTYPE`

#### Scenario: Нет слушателей
- **WHEN** ни один `ActivityListener` не подписан на `Redis.Client`
- **THEN** объекты Activity не создаются

#### Scenario: Распространение контекста
- **WHEN** команда выполняется внутри родительской Activity (например, HTTP-запроса)
- **THEN** Activity команды является её дочерней

### Requirement: Аргументы команд в трассировке
По умолчанию Activity MUST NOT содержать ключи и аргументы команд. При `RecordCommandArguments = true` клиент SHALL записывать текст команды в `db.query.text`. Аргументы команд `AUTH` и `HELLO` MUST маскироваться всегда, независимо от настройки.

#### Scenario: Настройка по умолчанию
- **WHEN** выполняется `SET user:1 secret-value`
- **THEN** ни один атрибут Activity не содержит `user:1` или `secret-value`

#### Scenario: Запись аргументов включена
- **WHEN** `RecordCommandArguments = true`, и выполняется `SET user:1 v`
- **THEN** `db.query.text` содержит `SET user:1 v`

#### Scenario: Маскирование учётных данных
- **WHEN** `RecordCommandArguments = true`, и выполняется handshake с паролем
- **THEN** ни одна Activity и ни одна запись лога не содержит пароль

### Requirement: Метрики
Клиент SHALL публиковать метрики через `Meter` с именем `Redis.Client`.

| Метрика | Тип | Теги / содержание |
|---|---|---|
| `db.client.operation.duration` | гистограмма, в секундах | `db.operation.name`, `error.type` при ошибке |
| `redis.client.connections` | UpDownCounter | открытые соединения, тег `redis.connection.kind`: `multiplexed`, `dedicated`, `subscriber` |
| `redis.client.reconnects` | Counter | — |
| `redis.client.backlog.size` | ObservableGauge | незаписанные команды |
| `redis.client.pending.size` | ObservableGauge | записанные команды, ждущие ответа |
| `redis.client.timeouts` | Counter | — |
| `redis.client.dedicated_pool.wait.duration` | гистограмма | ожидание аренды выделенного соединения |
| `redis.client.pubsub.dropped` | Counter | потерянные pub/sub-сообщения |

#### Scenario: Длительность операции
- **WHEN** подключён `MeterListener`, и выполняется 10 команд `GET`
- **THEN** в `db.client.operation.duration` записано 10 измерений с `db.operation.name = GET`

#### Scenario: Учёт переподключения
- **WHEN** соединение разорвано и восстановлено
- **THEN** `redis.client.reconnects` увеличился на 1

### Requirement: Логирование
Клиент SHALL писать логи через `ILogger` с категориями в пространстве имён библиотеки. Логи MUST создаваться через source-generated `LoggerMessage`. События по уровням:
- `Information` — подключение, переподключение, закрытие соединения, согласованная версия протокола и версия сервера;
- `Warning` — разрыв соединения, неудачная попытка подключения, исчерпание пула выделенных соединений;
- `Error` — ошибки протокола и исключения в обработчиках подписок.

Логи MUST NOT содержать значения ключей, аргументов команд и учётные данные.

#### Scenario: Жизненный цикл соединения
- **WHEN** клиент подключается, соединение рвётся, и клиент переподключается
- **THEN** в лог записаны событие подключения (`Information`), разрыва (`Warning`) и переподключения (`Information`) с адресом endpoint'а

#### Scenario: Отсутствие данных в логах
- **WHEN** при уровне логирования `Trace` выполняется `SET user:1 secret-value`
- **THEN** ни одна запись лога не содержит `secret-value`

