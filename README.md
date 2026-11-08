# Shortly — сервис коротких ссылок

Pet-проект на .NET 10: сокращение ссылок, редиректы под нагрузкой и статистика
переходов в реальном времени.

Домен выбран из-за характерного перекоса нагрузки: **чтений редиректов и записей
кликов на порядки больше, чем создания ссылок**. Это задаёт все архитектурные
решения ниже.

## Стек

**Backend:** C#, .NET 10, ASP.NET Core (Minimal API), MediatR, FluentValidation
**Данные:** PostgreSQL, EF Core, Dapper, Redis
**Realtime и интеграции:** SignalR, gRPC
**Инфраструктура:** Docker Compose, Serilog, Prometheus, Grafana
**Тесты:** xUnit, FluentAssertions, NSubstitute

## Архитектура

Clean Architecture, зависимости направлены внутрь:

Domain ← Application ← Infrastructure ← Api

- **Domain** — сущности с инвариантами (валидация URL, срок жизни, деактивация).
  Без зависимостей от фреймворков, поэтому тестируется без БД и моков.
- **Application** — CQRS на MediatR: команды меняют состояние и инвалидируют кэш,
  запросы читают с `AsNoTracking`. Валидация вынесена в pipeline behavior.
- **Infrastructure** — EF Core, Dapper, Redis, фоновые задачи.
- **Api** — endpoints, SignalR hub, gRPC, Swagger, метрики.

## Ключевые решения

### Редирект не обращается к базе
`GET /{code}` читает двухуровневый кэш: `IMemoryCache` (L1, TTL 1 мин) →
`Redis` (L2, TTL 10 мин) → PostgreSQL. Промахи кэшируются отдельно
(negative caching, TTL 1 мин) — иначе перебор несуществующих кодов
бьёт прямо в БД.

Redis недоступен — сервис продолжает работать на L1 и БД
(`AbortOnConnectFail = false`), деградируя по производительности, но не по доступности.

### Клики пишутся батчами, а не по одному
Редирект только кладёт событие в `Channel<ClickEvent>` (bounded, 50k).
`ClickFlushService` (`BackgroundService`) сбрасывает накопленное в БД
по достижении 500 событий или раз в 2 секунды — одним запросом с массивами:

    INSERT INTO click_events (...)
    SELECT * FROM UNNEST(@link_ids, @occurred_at, ...)

Плюс один `UPDATE` денормализованного счётчика `total_clicks`, сгруппированный
по ссылкам. Вместо 500 round-trip'ов к БД — два запроса в одной транзакции.

Канал работает в режиме `DropWrite`: при переполнении теряется клик статистики,
но редирект не начинает ждать БД. Отброшенные клики видны в метрике
`shortly_clicks_dropped_total`.

### EF Core и Dapper вместе
- **EF Core** — запись и CRUD ссылок: миграции, трекинг изменений, инварианты.
- **Dapper** — агрегаты статистики (`date_trunc`, `GROUP BY`, топ рефереров)
  и батчевая вставка: сырой SQL даёт предсказуемый план запроса там, где
  генератор LINQ мешает.

### Индексы
- `short_links.code` — уникальный: гарантия отсутствия коллизий на уровне БД.
- `click_events (link_id, occurred_at_utc)` — составной, под основной запрос
  статистики `WHERE link_id = .. AND occurred_at_utc BETWEEN ..`.

### Realtime и межсервисный доступ
- **SignalR** (`/hubs/stats`) — после каждого flush'а дашборд получает событие
  `clicksFlushed` без поллинга.
- **gRPC** (порт 8081, h2c) — `Resolve`/`Create` для внутренних потребителей:
  бинарный протокол дешевле JSON на частых межсервисных вызовах.

### Наблюдаемость
Структурные логи Serilog, метрики prometheus-net на `/metrics`:
глубина очереди, длительность flush'а, попадания в кэш по уровням,
отброшенные клики + стандартные HTTP-метрики. Grafana подключена к Prometheus.

## Запуск

    docker compose up -d --build

| Сервис | Адрес |
|---|---|
| Swagger | http://localhost:8080/swagger |
| Health | http://localhost:8080/health |
| Метрики | http://localhost:8080/metrics |
| gRPC | localhost:8081 (h2c, без TLS) |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 (anonymous) |

Тесты:

    dotnet test

## Примеры

Создать ссылку:

    curl -X POST http://localhost:8080/api/links \
      -H "Content-Type: application/json" \
      -d '{"originalUrl":"https://learn.microsoft.com/dotnet/","customCode":"dotnet"}'

Редирект:

    curl -i http://localhost:8080/dotnet

Статистика:

    curl "http://localhost:8080/api/links/dotnet/stats"

## Что осознанно не сделано

- **Аутентификация** — ссылки анонимные; добавление JWT не меняет архитектуру.
- **Шардирование и партиционирование** `click_events` — нужно начиная
  с десятков миллионов строк; сейчас достаточно составного индекса.
  Следующий шаг — секционирование по месяцам.
- **Idempotency-key** на создании ссылок — повторный POST создаёт новый код.
- **Интеграционные тесты на Testcontainers** — покрыты только домен
  и обработчики; SQL-запросы проверялись вручную.