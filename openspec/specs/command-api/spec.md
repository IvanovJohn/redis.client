# command-api Specification

## Purpose
Типизированный API команд Redis: ключи, строки, хеши, списки, множества, sorted sets и выполнение произвольных команд.
## Requirements
### Requirement: Структура публичного API
Точкой входа SHALL быть потокобезопасный интерфейс `IRedisClient : IAsyncDisposable`. Команды MUST быть сгруппированы в интерфейсы-свойства: `Keys`, `Strings`, `Hashes`, `Lists`, `Sets`, `SortedSets`. Каждая группа MUST быть отдельным публичным интерфейсом, чтобы её можно было замокать. Все методы команд SHALL быть асинхронными, возвращать `ValueTask`/`ValueTask<T>` и принимать `CancellationToken`.

#### Scenario: Мок группы команд
- **WHEN** в unit-тесте приложения подставлен мок `IStringCommands` в мок `IRedisClient.Strings`
- **THEN** код приложения работает с моком без реального Redis

### Requirement: Типы ключей и значений
Ключи SHALL передаваться как `RedisKey`, значения — как `RedisValue`. Оба типа MUST поддерживать неявное преобразование из `string` (UTF-8), `byte[]` и `ReadOnlyMemory<byte>`. `RedisValue` MUST дополнительно поддерживать `long` и `double`, а также явное преобразование обратно в эти типы. Отсутствующее значение (`nil`) SHALL быть представлено как `RedisValue.Null` и MUST NOT приводить к исключению.

#### Scenario: Чтение отсутствующего ключа
- **WHEN** вызывается `Strings.GetAsync("missing")`
- **THEN** возвращается `RedisValue.Null`, и `IsNull == true`

#### Scenario: Числовое значение
- **WHEN** вызывается `Strings.SetAsync("n", 42)`, а затем `(long)await Strings.GetAsync("n")`
- **THEN** результат равен `42`

#### Scenario: Преобразование нечислового значения
- **WHEN** значение `"abc"` явно приводится к `long`
- **THEN** выбрасывается `InvalidCastException`

### Requirement: Независимость результата от протокола
Типизированные методы SHALL возвращать одинаковые результаты при работе по RESP2 и по RESP3.

#### Scenario: HGETALL на разных протоколах
- **WHEN** `Hashes.GetAllAsync` вызывается для одного и того же хэша через клиенты с `Protocol = Resp2` и `Protocol = Resp3`
- **THEN** оба вызова возвращают одинаковый набор пар поле–значение

### Requirement: Команды над ключами
Группа `Keys` SHALL поддерживать:
- `DEL`, `UNLINK`, `EXISTS`;
- `EXPIRE`, `PEXPIRE`, `EXPIREAT`, `PEXPIREAT`, `TTL`, `PTTL`, `PERSIST`;
- `TYPE`, `RENAME`, `RENAMENX`, `COPY`, `TOUCH`;
- `SCAN`.

`TTL` для ключа без срока жизни и для отсутствующего ключа MUST различаться в результате.

#### Scenario: Время жизни ключа
- **WHEN** ключу задан `EXPIRE` на 100 секунд
- **THEN** `Keys.TimeToLiveAsync` возвращает значение больше 0 и не больше 100 секунд

#### Scenario: Ключ без срока жизни и отсутствующий ключ
- **WHEN** `Keys.TimeToLiveAsync` вызывается для постоянного ключа и для отсутствующего ключа
- **THEN** для постоянного ключа результат — «без срока жизни», а для отсутствующего — «ключ не существует»

### Requirement: Команды над строками
Группа `Strings` SHALL поддерживать:
- `GET`, `SET`, `MGET`, `MSET`, `MSETNX`;
- `GETDEL`, `GETEX`, `GETRANGE`, `SETRANGE`, `APPEND`, `STRLEN`;
- `INCR`, `INCRBY`, `INCRBYFLOAT`, `DECR`, `DECRBY`.

`SET` MUST поддерживать опции `EX`/`PX`/`EXAT`/`PXAT`/`KEEPTTL`, условия `NX`/`XX` и `GET`.

#### Scenario: SET NX на существующем ключе
- **WHEN** `Strings.SetAsync("k", "v2", when: NotExists)` вызывается для существующего ключа
- **THEN** метод возвращает `false`, и значение не меняется

#### Scenario: SET с временем жизни
- **WHEN** `Strings.SetAsync("k", "v", expiry: TimeSpan.FromSeconds(10))`
- **THEN** у ключа установлен TTL не больше 10 секунд

#### Scenario: MGET с отсутствующими ключами
- **WHEN** `MGET` вызывается для ключей `a` (существует) и `b` (не существует)
- **THEN** результат — массив из двух элементов: значение `a` и `RedisValue.Null`

### Requirement: Команды над хэшами
Группа `Hashes` SHALL поддерживать:
- `HGET`, `HMGET`, `HSET`, `HSETNX`, `HDEL`, `HEXISTS`, `HLEN`, `HSTRLEN`;
- `HGETALL`, `HKEYS`, `HVALS`;
- `HINCRBY`, `HINCRBYFLOAT`;
- `HRANDFIELD`, `HSCAN`.

#### Scenario: Запись нескольких полей
- **WHEN** `Hashes.SetAsync("h", [("a", 1), ("b", 2)])`
- **THEN** метод возвращает 2 (число новых полей), и `HGETALL` возвращает оба поля

### Requirement: Команды над списками
Группа `Lists` SHALL поддерживать:
- `LPUSH`, `RPUSH`, `LPUSHX`, `RPUSHX`, `LPOP`, `RPOP` (с параметром count);
- `LRANGE`, `LLEN`, `LINDEX`, `LSET`, `LINSERT`, `LREM`, `LTRIM`, `LPOS`;
- `LMOVE`;
- блокирующие `BLPOP`, `BRPOP`, `BLMOVE`.

Для блокирующих команд таймаут ожидания сервера MUST учитываться при вычислении дедлайна клиента, чтобы легитимное ожидание не завершалось `RedisTimeoutException`.

#### Scenario: Порядок элементов
- **WHEN** выполняется `RPUSH l a b c`, затем `LRANGE l 0 -1`
- **THEN** результат — `[a, b, c]`

#### Scenario: Блокирующее ожидание дольше CommandTimeout
- **WHEN** `CommandTimeout = 5 с`, вызван `Lists.BlockingLeftPopAsync("q", timeout: 10 с)`, и элемент добавлен через 7 секунд
- **THEN** вызов возвращает этот элемент, а не завершается таймаутом клиента

#### Scenario: Блокирующее ожидание без данных
- **WHEN** `BLPOP` по пустому списку завершился по таймауту сервера
- **THEN** метод возвращает `null`, а не выбрасывает исключение

### Requirement: Команды над множествами
Группа `Sets` SHALL поддерживать:
- `SADD`, `SREM`, `SMEMBERS`, `SISMEMBER`, `SMISMEMBER`, `SCARD`;
- `SPOP`, `SRANDMEMBER`, `SMOVE`;
- `SINTER`, `SUNION`, `SDIFF` и их `STORE`-варианты;
- `SSCAN`.

#### Scenario: Проверка нескольких членов
- **WHEN** `Sets.ContainsAsync("s", ["a", "x"])` вызывается для множества `{a, b}`
- **THEN** результат — `[true, false]`

### Requirement: Команды над отсортированными множествами
Группа `SortedSets` SHALL поддерживать:
- `ZADD` с опциями `NX`/`XX`/`GT`/`LT`/`CH`, `ZINCRBY`, `ZREM`;
- `ZSCORE`, `ZMSCORE`, `ZRANK`, `ZREVRANK`, `ZCARD`, `ZCOUNT`;
- `ZRANGE` с режимами по рангу, по score и лексикографически, с `REV`, `LIMIT`, `WITHSCORES`, а также `ZRANGESTORE`;
- `ZREMRANGEBYRANK`, `ZREMRANGEBYSCORE`, `ZREMRANGEBYLEX`;
- `ZPOPMIN`, `ZPOPMAX` и блокирующие `BZPOPMIN`, `BZPOPMAX`;
- `ZINTER`, `ZUNION`, `ZDIFF` и их `STORE`-варианты;
- `ZSCAN`.

Границы диапазонов SHALL поддерживать включающие, исключающие и бесконечные значения.

#### Scenario: Диапазон по score с исключающей границей
- **WHEN** множество содержит `a:1, b:2, c:3`, и выполняется запрос диапазона по score `(1 .. +inf` с `WITHSCORES`
- **THEN** результат — `[(b, 2), (c, 3)]`

#### Scenario: ZADD GT
- **WHEN** у `a` score 5, и выполняется `ZADD` с `GT` и score 3
- **THEN** score `a` остаётся 5

### Requirement: Итерация SCAN-семейства
Методы `SCAN`, `HSCAN`, `SSCAN` и `ZSCAN` SHALL возвращать `IAsyncEnumerable<T>` и прозрачно продолжать курсор, пока сервер не вернёт курсор `0`. Методы MUST поддерживать параметры `MATCH` и `COUNT`, а `SCAN` — ещё и `TYPE`. Итерация MUST прекращаться при отмене токена.

#### Scenario: Обход большого числа ключей
- **WHEN** в базе 10 000 ключей с префиксом `user:`, и вызывается `Keys.ScanAsync(pattern: "user:*")`
- **THEN** перечисление возвращает каждый из 10 000 ключей хотя бы один раз, используя несколько вызовов `SCAN`

### Requirement: Произвольные команды
`IRedisClient.ExecuteAsync(command, args, ct)` SHALL отправлять произвольную команду и возвращать `RedisResult` — дерево разобранного ответа с доступом к типу кадра и значениям. Ошибки сервера MUST приводить к `RedisServerException`, как и для типизированных команд.

#### Scenario: Команда без типизированного API
- **WHEN** вызывается `ExecuteAsync("XADD", ["stream", "*", "field", "value"])`
- **THEN** возвращается `RedisResult` с bulk string — идентификатором записи потока

#### Scenario: Ошибка в произвольной команде
- **WHEN** вызывается `ExecuteAsync("NOSUCHCOMMAND", [])`
- **THEN** вызов завершается `RedisServerException` с кодом `ERR`

### Requirement: Команды, меняющие состояние соединения
`ExecuteAsync` MUST отклонять команды, которые меняют состояние соединения и поэтому повлияли бы на всех вызывающих, разделяющих мультиплексированное соединение:
- `SELECT`, `MULTI`, `EXEC`, `DISCARD`, `WATCH`, `UNWATCH`;
- `SUBSCRIBE` и прочие команды подписки;
- `HELLO`, `AUTH`, `RESET`, `QUIT`, `MONITOR`, `CLIENT REPLY`.

Вызов SHALL завершаться `NotSupportedException` с указанием API, которое нужно использовать вместо команды. Команда MUST NOT отправляться на сервер.

#### Scenario: SELECT через ExecuteAsync
- **WHEN** вызывается `ExecuteAsync("SELECT", ["2"])`
- **THEN** выбрасывается `NotSupportedException`, а база данных соединения не меняется

