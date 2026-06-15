# Валидация переходов статусов заказа

Цель: выявлять подозрительные заказы, у которых статус меняется в обход допустимой цепочки,
определённой в `bpmn/order-lifecycle.bpmn`.

---

## Контекст: что такое «подозрительный» переход

Текущий BPMN маршрутизирует заказы по статусам через `GW_RouteByStatus`, но **не проверяет**,
откуда пришёл этот статус. Каждый переход принимается как корректный.
Задача — добавить логику, которая проверяет: «а был ли предыдущий статус допустимым
для того, чтобы перейти в текущий?»

Пример некорректного перехода:
```
Исполняемый → Центральный склад   ← пропущены Оформляемый + Производство
Оформляемый → Выполнен            ← прыжок через всю логистику
```

Пример корректного перехода:
```
[старт] → Исполняемый → Оформляемый → Обрабатывается-Ковров → ... → Выполнен
```

---

## Карта допустимых переходов (baseline)

> Уточните под реальную бизнес-логику. Коды ID — из `docs/statuses.md`.

```
[старт процесса — первое событие]
  → Исполняемый    (C0008686474408E5)
  → Оформляемый    (C0003089DD24DC9A)
  → Отменён        (8010000000000108)

Исполняемый
  → Оформляемый
  → Отменён

Оформляемый
  → Обрабатывается-Ковров      (8001000000001500)
  → Обрабатывается-Новосибирск (8001000000001501)
  → Внимание                   (80010000000020AE)   ← блокировка
  → Нет ткани                  (80010000000020A4)
  → Ожидание                   (8001000000001523)
  → Жду оплату                 (8001000000001564)
  → Отменён

Состояния блокировки [Внимание / Нет ткани / Ожидание / Жду оплату]
  → Оформляемый                                    ← снятие блокировки
  → Отменён

Обрабатывается-Ковров / Обрабатывается-Новосибирск
  → Ковров-планирование        (800100000000011F)
  → Новосибирск-планирование   (8001000000000120)
  → Поставка                   (8001000000000121)
  → Отменён

Ковров/Новосибирск-планирование
  → Обрабатывается-Ковров
  → Обрабатывается-Новосибирск
  → Поставка
  → Отменён

Поставка
  → Центральный склад          (80010000000020A9)
  → Отменён

Центральный склад
  → Перемещение                (80010000000034F4)
  → РС                         (80010000000034F5)
  → Отменён

Перемещение / РС
  → В пути                     (800100000000195B)
  → Региональный склад         (80010000000020B3)
  → Транзит                    (8001000000003E0E)
  → Смежный склад              (80010000000052A0)
  → Отменён

В пути / Транзит / Региональный склад
  → Перемещение на ТТ          (80010000000020B4)
  → Торговая точка             (80010000000020B5)
  → Готов к отгрузке           (8001000000000122)
  → Отменён

Перемещение на ТТ
  → Торговая точка
  → Отменён

Торговая точка
  → Готов к отгрузке
  → Отменён

Готов к отгрузке / Смежный склад
  → Отгрузка завершена         (8001000000000154)
  → Отменён

Отгрузка завершена
  → Выполнен                   (8001000000000118)
  → Отменён

[Терминальные: Выполнен, Отменён]
```

---

## Подходы к реализации

---

### Подход 1. FEEL-выражение + переменная `previousStatusId` внутри BPMN

#### Суть

Хранить предыдущий статус в переменной Camunda и проверять допустимость перехода
в Script Task прямо в BPMN-процессе.

#### Как реализовать

**Шаг 1.** В каждом статусном Task добавить Output Mapping, сохраняющий текущий статус:

```xml
<ns2:output source="= orderStatusId" target="previousStatusId" />
```

**Шаг 2.** Перед `GW_RouteByStatus` добавить `Task_ValidateTransition` (Script Task, FEEL):

```feel
= if previousStatusId = null then
    true
  else if previousStatusId = "C0008686474408E5" then
    orderStatusId in ["C0003089DD24DC9A", "8010000000000108"]
  else if previousStatusId = "C0003089DD24DC9A" then
    orderStatusId in [
      "8001000000001500", "8001000000001501",
      "80010000000020AE", "80010000000020A4",
      "8001000000001523", "8001000000001564",
      "8010000000000108"
    ]
  else
    true
```

Результат пишется в переменную `isValidTransition`.

**Шаг 3.** Добавить XOR-шлюз `GW_CheckTransition` после валидации:

```
GW_LoopJoin
  → Task_ValidateTransition
    → GW_CheckTransition
        [isValidTransition = true]  → GW_RouteByStatus
        [isValidTransition = false] → Task_FlagSuspicious → GW_RouteByStatus
```

#### Плюсы

- Не требует внешнего сервиса
- Логика видна прямо в Camunda Modeler
- Быстрая реализация

#### Минусы

- FEEL неудобен для матрицы 24×24 — код раздувается
- Изменение правил требует передеплоя BPMN
- Нет централизованного места для матрицы переходов
- Практически нетестируем изолированно

---

### Подход 2. Job Worker с FSM-картой (рекомендуется для Camunda 8)

#### Суть

Создать отдельный Job Worker (Java/Kotlin/Python), реализующий конечный автомат (FSM).
Worker вызывается из Service Task в BPMN, получает `previousStatusId` и `orderStatusId`,
проверяет переход и возвращает `isValidTransition`.

#### Как реализовать

**BPMN:** добавить `Task_ValidateTransition` типа Service Task, topic: `validate-status-transition`.

**Матрица переходов (Python):**

```python
# transition_matrix.py
from __future__ import annotations

ALLOWED: dict[str | None, set[str]] = {
    # None = первый статус в процессе
    None: {
        "C0008686474408E5",   # Исполняемый
        "C0003089DD24DC9A",   # Оформляемый
        "8010000000000108",   # Отменён
    },
    "C0008686474408E5": {    # Исполняемый →
        "C0003089DD24DC9A",  # Оформляемый
        "8010000000000108",  # Отменён
    },
    "C0003089DD24DC9A": {    # Оформляемый →
        "8001000000001500",  # Обр.-Ковров
        "8001000000001501",  # Обр.-НСК
        "80010000000020AE",  # Внимание
        "80010000000020A4",  # Нет ткани
        "8001000000001523",  # Ожидание
        "8001000000001564",  # Жду оплату
        "8010000000000108",  # Отменён
    },
    "80010000000020AE": {"C0003089DD24DC9A", "8010000000000108"},  # Внимание →
    "80010000000020A4": {"C0003089DD24DC9A", "8010000000000108"},  # Нет ткани →
    "8001000000001523": {"C0003089DD24DC9A", "8010000000000108"},  # Ожидание →
    "8001000000001564": {"C0003089DD24DC9A", "8010000000000108"},  # Жду оплату →
    "8001000000001500": {    # Обр.-Ковров →
        "800100000000011F", "8001000000000121", "8010000000000108",
    },
    "8001000000001501": {    # Обр.-НСК →
        "8001000000000120", "8001000000000121", "8010000000000108",
    },
    "800100000000011F": {    # К-Планирование →
        "8001000000001500", "8001000000000121", "8010000000000108",
    },
    "8001000000000120": {    # НСК-Планирование →
        "8001000000001501", "8001000000000121", "8010000000000108",
    },
    "8001000000000121": {    # Поставка →
        "80010000000020A9", "8010000000000108",
    },
    "80010000000020A9": {    # Центральный склад →
        "80010000000034F4", "80010000000034F5", "8010000000000108",
    },
    # ... дополнить по карте переходов выше
}


def is_allowed(from_status: str | None, to_status: str) -> bool:
    return to_status in ALLOWED.get(from_status, set())
```

**Worker:**

```python
# worker.py
from pyzeebe import ZeebeWorker, Job, create_insecure_channel
from transition_matrix import is_allowed

channel = create_insecure_channel(hostname="localhost", port=26500)
worker = ZeebeWorker(channel)


@worker.task(task_type="validate-status-transition")
async def validate(job: Job) -> dict:
    prev        = job.variables.get("previousStatusId")
    next_status = job.variables["orderStatusId"]
    order_id    = job.variables["orderId"]

    valid = is_allowed(prev, next_status)

    return {
        "isValidTransition": valid,
        "previousStatusId":  next_status,  # сдвигаем для следующей итерации
        "suspiciousReason":  (
            f"Недопустимый переход: {prev} → {next_status} (заказ {order_id})"
            if not valid else None
        ),
    }
```

**XOR-шлюз после Task_ValidateTransition:**

```
[isValidTransition = true]  → GW_RouteByStatus (обычный путь)
[isValidTransition = false] → Task_FlagSuspicious → GW_RouteByStatus
```

`Task_FlagSuspicious` может публиковать в Kafka-топик `suspicious_orders` или выставлять
переменную `isSuspicious = true` для последующего мониторинга через Operate.

**Unit-тест:**

```python
# test_transition_matrix.py
from transition_matrix import is_allowed


def test_исполняемый_только_в_оформляемый_или_отменён():
    from_status = "C0008686474408E5"
    assert is_allowed(from_status, "C0003089DD24DC9A")
    assert is_allowed(from_status, "8010000000000108")
    assert not is_allowed(from_status, "80010000000020A9")  # Центр. склад
    assert not is_allowed(from_status, "8001000000000118")  # Выполнен


def test_оформляемый_не_может_прыгнуть_в_выполнен():
    assert not is_allowed("C0003089DD24DC9A", "8001000000000118")
```

#### Плюсы

- Полная матрица переходов в одном месте — в коде
- Легко покрывается unit-тестами
- Изменение правил = обновление worker без передеплоя BPMN
- Можно добавить метрики, enrichment, логирование

#### Минусы

- Требует дополнительного модуля в worker-сервисе
- Добавляет latency на каждое событие (~1–5 мс)

---

### Подход 3. DMN Decision Table (таблица допустимых переходов)

#### Суть

Определить матрицу переходов в виде DMN-таблицы и вызывать её из Business Rule Task в BPMN.
Camunda 8 поддерживает DMN 1.3 нативно.

#### Как реализовать

**Создать `transition-rules.dmn`** (Hit Policy: `FIRST`):

```
Inputs:
  previousStatusId (string)
  orderStatusId    (string)

Output:
  isAllowed (boolean)

Rows:
  null                | "C0008686474408E5" → true   (старт → Исполняемый)
  null                | "C0003089DD24DC9A" → true   (старт → Оформляемый)
  "C0008686474408E5"  | "C0003089DD24DC9A" → true
  "C0008686474408E5"  | "8010000000000108" → true
  "C0003089DD24DC9A"  | "8001000000001500" → true
  "C0003089DD24DC9A"  | "8001000000001501" → true
  ...
  -                   | -                  → false  ← catch-all
```

**BPMN:** заменить Script Task на Business Rule Task, указать `camunda:decisionRef="IsTransitionAllowed"`.

Вывод DMN → переменная `isAllowed` → тот же XOR-шлюз.

#### Плюсы

- Таблица читаема бизнес-аналитиком без программирования
- Редактируется в Camunda Modeler визуально
- Стандарт OMG, переносимо между движками
- Не требует компиляции

#### Минусы

- 24 статуса × N переходов = таблица 150+ строк — громоздко в UI
- Нет удобного способа записать «Отменён допустим из любого статуса» без дублирования
- Версионирование DMN отдельно от BPMN требует дисциплины
- Сложнее тестировать автоматически по сравнению с кодом

---

### Подход 4. Kafka Streams / ksqlDB — потоковая обработка вне Camunda

#### Суть

Независимый от Camunda pipeline: читает топик `all_orders`, хранит последний статус заказа
в KTable, на каждом новом событии проверяет переход и публикует в топик `suspicious_orders`.

#### Как реализовать

**Вариант A: Faust (Python)**

```python
# app.py
import faust
from transition_matrix import is_allowed


class OrderEvent(faust.Record):
    order_id:  str
    status_id: str


app = faust.App("order-validation", broker="kafka://localhost:9092")

all_orders_topic = app.topic("all_orders", value_type=OrderEvent)
suspicious_topic = app.topic("suspicious_orders")

# KTable: orderId → последний statusId
order_state = app.Table("order_last_status", default=lambda: None)


@app.agent(all_orders_topic)
async def process(stream):
    async for event in stream:
        prev_status  = order_state[event.order_id]
        next_status  = event.status_id

        if not is_allowed(prev_status, next_status):
            await suspicious_topic.send(value={
                "order_id": event.order_id,
                "reason":   f"Переход {prev_status} → {next_status} не разрешён",
            })

        order_state[event.order_id] = next_status  # сдвигаем окно
```

**Вариант B: ksqlDB**

```sql
-- Таблица последних статусов
CREATE TABLE order_last_status AS
  SELECT order_id,
         LATEST_BY_OFFSET(status_id) AS last_status
  FROM all_orders
  GROUP BY order_id
  EMIT CHANGES;

-- Поток с предыдущим статусом
CREATE STREAM orders_with_prev AS
  SELECT
    o.order_id,
    o.status_id  AS new_status,
    t.last_status AS prev_status
  FROM all_orders o
  LEFT JOIN order_last_status t ON o.order_id = t.order_id
  EMIT CHANGES;

-- Подозрительные: Исполняемый → не Оформляемый и не Отменён
CREATE STREAM suspicious_orders AS
  SELECT *
  FROM orders_with_prev
  WHERE
    (prev_status = 'C0008686474408E5'
     AND new_status NOT IN ('C0003089DD24DC9A', '8010000000000108'))
  OR
    (prev_status = 'C0003089DD24DC9A'
     AND new_status NOT IN (
       '8001000000001500', '8001000000001501',
       '80010000000020AE', '80010000000020A4',
       '8001000000001523', '8001000000001564',
       '8010000000000108'
     ))
  -- ... остальные правила
  EMIT CHANGES;
```

#### Плюсы

- Полностью независим от Camunda — работает параллельно, не влияет на основной процесс
- Real-time детектирование без задержек BPMN-цикла
- Масштабируется на большой поток событий
- Kafka Streams поддерживает exactly-once семантику

#### Минусы

- Требует отдельного Kafka Streams приложения или ksqlDB-кластера
- Нужно синхронизировать матрицу переходов с основной логикой (два места)
- KTable содержит только «последний» статус — при параллельных событиях возможна гонка
- Высокий порог входа: нужна экспертиза Kafka Streams

---

### Подход 5. Отдельный аудит-сервис с историей переходов

#### Суть

Микросервис (или модуль в существующем worker) подписывается на `all_orders`,
хранит полную историю переходов в БД и валидирует каждый шаг.
Предоставляет REST API для дашборда и отчётов.

#### Как реализовать

**Схема БД (PostgreSQL):**

```sql
CREATE TABLE order_transitions (
    id          BIGSERIAL    PRIMARY KEY,
    order_id    TEXT         NOT NULL,
    from_status TEXT,                      -- NULL для первого статуса
    to_status   TEXT         NOT NULL,
    occurred_at TIMESTAMPTZ  NOT NULL DEFAULT now(),
    is_valid    BOOLEAN      NOT NULL,
    reason      TEXT
);

CREATE INDEX ON order_transitions (order_id, occurred_at DESC);
CREATE INDEX ON order_transitions (is_valid) WHERE is_valid = false;
CREATE INDEX ON order_transitions (occurred_at DESC);
```

**Сервис (Python / FastAPI):**

```python
ALLOWED: dict[str | None, set[str]] = {
    None: {"C0008686474408E5", "C0003089DD24DC9A", "8010000000000108"},
    "C0008686474408E5": {"C0003089DD24DC9A", "8010000000000108"},
    "C0003089DD24DC9A": {
        "8001000000001500", "8001000000001501",
        "80010000000020AE", "80010000000020A4",
        "8001000000001523", "8001000000001564",
        "8010000000000108",
    },
    # ...
}

async def process_event(event: OrderEvent, db: AsyncSession) -> None:
    # Последний известный статус заказа
    prev = await db.scalar(
        select(OrderTransition.to_status)
        .where(OrderTransition.order_id == event.order_id)
        .order_by(OrderTransition.occurred_at.desc())
        .limit(1)
    )

    is_valid = event.status_id in ALLOWED.get(prev, set())

    db.add(OrderTransition(
        order_id   = event.order_id,
        from_status = prev,
        to_status  = event.status_id,
        is_valid   = is_valid,
        reason     = None if is_valid
                     else f"Недопустимый переход: {prev} → {event.status_id}",
    ))
    await db.commit()

    if not is_valid:
        await alert_service.notify(event)
```

**REST API:**

```python
# Все подозрительные переходы за период
@app.get("/suspicious")
async def get_suspicious(
    since: datetime = Query(default=None),
    db: AsyncSession = Depends(get_db),
):
    q = select(OrderTransition).where(OrderTransition.is_valid == False)
    if since:
        q = q.where(OrderTransition.occurred_at >= since)
    return (await db.scalars(q.order_by(OrderTransition.occurred_at.desc()))).all()

# Полная история конкретного заказа
@app.get("/orders/{order_id}/transitions")
async def get_order_history(order_id: str, db: AsyncSession = Depends(get_db)):
    return (await db.scalars(
        select(OrderTransition)
        .where(OrderTransition.order_id == order_id)
        .order_by(OrderTransition.occurred_at)
    )).all()
```

#### Плюсы

- Полная история всех переходов — можно анализировать ретроспективно
- REST API для дашборда: «сколько аномалий за неделю», «какие магазины дают больше всего»
- Независим от Camunda
- Наиболее гибкое решение для аналитики

#### Минусы

- Требует разворачивания сервиса + PostgreSQL (или можно переиспользовать существующую БД)
- Нужна синхронизация матрицы переходов
- При большом потоке — нагрузка на БД (решается партиционированием таблицы)
- Не блокирует некорректный переход в основном процессе — только детектирует

---

### Подход 6. Camunda History API + периодический batch-аудит

#### Суть

Использовать встроенную историю Camunda. Хранить `previousStatusId` как переменную процесса,
периодически запрашивать через Operate REST API экземпляры, у которых `isValidTransition = false`.

#### Как реализовать

**Шаг 1.** В BPMN хранить `previousStatusId` (аналогично Подходу 1 или 2).

**Шаг 2.** Записывать `isSuspicious = true` и `suspiciousReason` в переменные при обнаружении.

**Шаг 3.** Cron-скрипт запрашивает Operate API:

```python
import httpx, asyncio

OPERATE = "http://localhost:8081"

async def find_suspicious_instances():
    async with httpx.AsyncClient() as client:
        resp = await client.post(
            f"{OPERATE}/v1/variables/search",
            json={
                "filter": {"name": "isSuspicious", "value": "true"},
                "size": 100,
            },
            headers={"Authorization": "Bearer ..."},
        )
        items = resp.json()["items"]

    # Для каждого — получить orderId и причину
    for item in items:
        process_key = item["processInstanceKey"]
        # запросить остальные переменные по process_key...
        print(f"Подозрительный процесс: {process_key}")

asyncio.run(find_suspicious_instances())
```

#### Плюсы

- Не требует внешней инфраструктуры — данные уже в Camunda
- Operate UI сам показывает переменные
- Минимум дополнительного кода

#### Минусы

- Только ретроспективный анализ, не real-time
- Operate REST API ограничен по возможностям фильтрации
- История хранится ограниченное время (зависит от retention Elasticsearch / RDBMS)
- Нельзя строить произвольную аналитику

---

## Сравнение подходов

| Критерий                     | 1. FEEL | 2. Worker | 3. DMN | 4. Kafka Streams | 5. Аудит-сервис | 6. History API |
|------------------------------|:-------:|:---------:|:------:|:----------------:|:---------------:|:--------------:|
| Скорость внедрения           |   ★★★   |    ★★☆    |  ★★☆   |       ★☆☆        |      ★☆☆        |      ★★★       |
| Читаемость правил            |   ★☆☆   |    ★★☆    |  ★★★   |       ★★☆        |      ★★☆        |      ★☆☆       |
| Тестируемость                |   ★☆☆   |    ★★★    |  ★★☆   |       ★★★        |      ★★★        |      ★☆☆       |
| Real-time детектирование     |   ★★★   |    ★★★    |  ★★★   |       ★★★        |      ★★★        |      ★☆☆       |
| История переходов            |   ☆☆☆   |    ☆☆☆    |  ☆☆☆   |       ★★☆        |      ★★★        |      ★★☆       |
| Независимость от Camunda     |   ☆☆☆   |    ★☆☆    |  ☆☆☆   |       ★★★        |      ★★★        |      ☆☆☆       |
| Инфраструктурная сложность   |  низкая |   низкая  | низкая |     высокая      |    средняя      |     низкая     |

---

## Рекомендуемая комбинация

Для вашей архитектуры (Camunda 8.9 + Kafka + k3s) оптимально:

**Подход 2 (Job Worker) + Подход 5 (Аудит-сервис)**

```
Kafka all_orders
       │
       ├──► Camunda process
       │         │
       │         └── Task_ValidateTransition  (Job Worker, topic: validate-status-transition)
       │                  │ isValidTransition = false
       │                  └── Task_FlagSuspicious  →  публикует в Kafka
       │                                              выставляет isSuspicious = true
       │
       └──► Audit Service  (независимый consumer)
                  ├── хранит полную историю в PostgreSQL
                  └── REST /suspicious → дашборд / алерты
```

- **Job Worker** проверяет переход синхронно в процессе и помечает экземпляр в Camunda
- **Audit Service** накапливает историю независимо и позволяет строить отчёты:
  «сколько заказов за неделю прыгнули через этапы», «какие магазины дают больше всего аномалий»
- **Матрица переходов** — единый источник истины, вынесенный в общую библиотеку,
  которую используют и Worker, и Audit Service

---

## Что делать с подозрительным заказом

После обнаружения аномалии нужно решить — **блокировать** процесс или **продолжать с меткой**:

| Стратегия              | Когда применять                           | Реализация в BPMN                              |
|------------------------|-------------------------------------------|------------------------------------------------|
| Только пометить        | Нужен мониторинг, аномалия не критична    | `isSuspicious = true`, процесс продолжается    |
| Уведомить и продолжить | Нужен алерт, тормозить процесс не нужно   | Service Task → Kafka/webhook, затем продолжить |
| Поставить на hold      | Нужна ручная проверка менеджером          | User Task для ответственного                   |
| Завершить аномально    | Заказ не должен дальше обрабатываться     | End Event `End_SuspiciousOrder`                |

Рекомендуется начать со стратегии **«уведомить и продолжить»**:
собрать статистику, понять природу аномалий (баг в ERP? реальное мошенничество?),
затем принять решение о жёсткой блокировке.
