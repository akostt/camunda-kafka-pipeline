# CamundaData — интеграция Галактика → Kafka → Camunda

Сквозной поток данных о заказах: **Галактика ERP** → **Apache Kafka** → **Camunda 8 BPMN**.

---

## Стек

- **Camunda 8.9** + Kafka Inbound Connector — BPMN-движок
- **Identity/Keycloak** — OIDC-аутентификация (Operate, Tasklist, Connectors)
- **Apache Kafka 7.7** + **Schema Registry** — шина (SSL/mTLS)
- **Apache Avro** — схемы сообщений
- **Python** + `pyzeebe` / `aiokafka` — job worker
- **Python** + `confluent-kafka` — CLI-генератор заказов
- **ASP.NET Core** (`order-manager`) — real-time мониторинг заказов (SignalR + Kafka)

## Структура

```
.
├── bpmn/                  # BPMN-диаграммы (order-lifecycle, order-kafka-process)
├── schemas/               # Avro-схемы (all_order_v2)
├── kafka/                 # Docker Compose + SSL для Kafka, Schema Registry, Kafka UI
├── data-creator/          # Генератор и отправка тестовых заказов в Kafka
├── worker/                # Zeebe job worker: задача из Camunda → Kafka
├── order-manager/         # ASP.NET Core — мониторинг заказов в реальном времени
├── infra/                 # Скрипт развёртывания k3s + Camunda (Helm)
└── docs/                  # Документация
```

## Быстрый старт (локально)

```bash
# 1. SSL-сертификаты
bash kafka/gen-certs.sh

# 2. Kafka
cd kafka && docker compose up -d

# 3. Camunda c8run
cd /path/to/c8run-8.9.8 && KAFKA_PFX_PASSWORD=yourpass ./c8run start

# 4. Деплой BPMN через Camunda Modeler на localhost:8080

# 5. Job worker
cd worker && uv sync && uv run worker.py

# 6. Генератор тестовых данных
cd data-creator && uv sync && uv run producer.py
```

## Деплой на VPS

```bash
scp infra/camunda-k3s-setup.sh user@SERVER_IP:~/
ssh user@SERVER_IP
bash ~/camunda-k3s-setup.sh
```

Подробнее: [docs/deployment.md](docs/deployment.md)

## Docs

| Документ | Описание |
|----------|---------|
| [docs/deployment.md](docs/deployment.md) | Развёртывание Camunda на VPS: k3s + Helm + Elasticsearch + Identity/Keycloak + Kafka SSL |
| [docs/kafka-ssl-pfx-guide.md](docs/kafka-ssl-pfx-guide.md) | Kafka SSL с PFX-сертификатом (c8run, Docker, k3s) |
| [docs/statuses.md](docs/statuses.md) | Справочник статусов заказа Галактика |
| [docs/statuses-lines.md](docs/statuses-lines.md) | Статусы строк заказа |
| [docs/status-transition-validation.md](docs/status-transition-validation.md) | Валидация статусных переходов |

## Требования (локальная разработка)

- Python ≥ 3.11 + `uv`
- Docker ≥ 24
- Java 21 (для c8run)

## Переменные окружения SSL

| Переменная | Описание | Дефолт |
|-----------|---------|--------|
| `KAFKA_SSL` | Включить SSL | `0` |
| `KAFKA_PFX_PATH` | Путь к PFX | — |
| `KAFKA_PFX_PASSWORD` | Пароль PFX | — |
| `KAFKA_BOOTSTRAP` | Брокер | `localhost:9093` |
| `SCHEMA_REGISTRY_URL` | SR URL | `http://localhost:8082` |
