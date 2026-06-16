# CamundaData — Производственная практика

Проект производственной практики: интеграция ERP-системы **Галактика** с шиной данных на базе **Apache Kafka** через процессный движок **Camunda 8**.

Сквозной поток данных о заказах: инициация BPMN-процесса в Camunda → публикация сообщений в Kafka в формате Avro.

---

## Архитектура

```
┌──────────────────┐   BPMN/gRPC   ┌──────────────┐   Avro/Kafka   ┌──────────────────────┐
│   Camunda 8.9    │ ─────────────► │ Zeebe Worker │ ─────────────► │  Apache Kafka (SSL)  │
│  c8run / k3s     │               │  (Python)    │                │ + Schema Registry    │
└──────────────────┘               └──────────────┘                └──────────────────────┘
         │
   Kafka Inbound Connector
   читает топик → стартует процесс
```

**Стек:**
- **Camunda 8.9** + Kafka Inbound Connector — BPMN-движок
- **Apache Kafka 7.7** + **Confluent Schema Registry** — шина сообщений (Docker Compose, SSL/mTLS)
- **Apache Avro** — схема сообщений (`all_order_v2`)
- **Python** + `pyzeebe` + `aiokafka` — Zeebe job worker
- **Python** + `confluent-kafka` + `rich` — CLI-инструмент для генерации тестовых данных

---

## Структура репозитория

```
.
├── bpmn/                          # BPMN-диаграммы и формы
│   ├── order-lifecycle.bpmn       # Жизненный цикл заказа (Kafka Inbound + Service Task)
│   ├── order-kafka-process.bpmn   # Простой процесс через Kafka
│   └── ConfirmOrderForm.form      # Форма подтверждения заказа
│
├── schemas/                       # Avro-схемы
│   ├── all_order_v2.json          # Основная схема заказа
│   ├── all_order_v2.min.json      # Минифицированная версия
│   └── all_order_v2.string.json   # Строковая версия (для BPMN inlineSchema)
│
├── kafka/                         # Kafka + Schema Registry (Docker Compose)
│   ├── docker-compose.yml         # Kafka (9092 PLAINTEXT, 9093 SSL), SR, Kafka UI
│   ├── gen-certs.sh               # Генерация self-signed сертификатов
│   └── kafka-ssl/                 # Сертификаты (gitignored, создаются gen-certs.sh)
│
├── data-creator/                  # CLI-инструмент: генерация и отправка тестовых заказов
│   ├── producer.py                # Интерактивное меню
│   ├── describe_schema.py         # Утилита просмотра структуры Avro-схемы
│   ├── generators/all_order_v2.py # Доменный генератор случайных заказов
│   └── .env                       # Kafka SSL env (gitignored)
│
├── worker/                        # Zeebe job worker
│   ├── worker.py                  # Читает задачи из Camunda, публикует в Kafka
│   └── .env                       # Kafka SSL env (gitignored)
│
├── infra/                         # Скрипты развёртывания
│   └── camunda-k3s-setup.sh       # Установка Camunda на VPS (k3s + Helm)
│
└── docs/                          # Документация
```

---

## Требования

| Компонент | Версия |
|-----------|--------|
| Python | ≥ 3.11 |
| uv | любая |
| Docker + Compose v2 | ≥ 24 |
| Java (для c8run) | 21 |

---

## Локальный запуск (c8run + Docker Kafka)

### 1. Сгенерировать SSL-сертификаты

```bash
bash kafka/gen-certs.sh
```

Создаёт `kafka/kafka-ssl/` с сертификатами брокера и `client-full.pfx` для клиентов.  
Пароль по умолчанию: `KafkaSsl2024`. Переопределить: `KAFKA_PFX_PASSWORD=MyPass bash kafka/gen-certs.sh`.

> Папка `kafka/kafka-ssl/` в git не попадает.

### 2. Запустить Kafka

```bash
cd kafka
docker compose up -d
```

`kafka/.env` подхватывается автоматически — задаёт `KAFKA_HOST=localhost` и путь к сертификатам.

| Сервис | Адрес |
|--------|-------|
| Kafka PLAINTEXT | `localhost:9092` |
| Kafka SSL/mTLS | `localhost:9093` |
| Schema Registry | `http://localhost:8082` |
| Kafka UI | `http://localhost:8081` |

### 3. Запустить Camunda (c8run)

```bash
cd /path/to/c8run-8.9.8
./c8run start
```

c8run читает `.env` из своей директории — переменная `KAFKA_PFX_PASSWORD` уже прописана там и становится доступна как `secrets.KAFKA_PFX_PASSWORD` в FEEL-выражениях BPMN-коннектора.

| Сервис | Адрес |
|--------|-------|
| Camunda Operate | `http://localhost:8080` |
| Zeebe gRPC | `localhost:26500` |
| Connector Runtime | `http://localhost:8086` |

### 4. Задеплоить BPMN

Открыть в **Camunda Modeler** → задеплоить на `http://localhost:8080`:
- `bpmn/order-lifecycle.bpmn`
- `bpmn/order-kafka-process.bpmn`

В поле `additionalProperties` Kafka-коннектора нужно указать путь к `client-full.pfx`.  
Подробнее: [docs/kafka-ssl-pfx-guide.md](docs/kafka-ssl-pfx-guide.md)

### 5. Запустить Zeebe Worker

```bash
cd worker
uv sync
uv run worker.py
```

`worker/.env` подхватывается автоматически — SSL включён, подключение к `localhost:9093`.

### 6. Запустить генератор тестовых данных

```bash
cd data-creator
uv sync
uv run producer.py
```

`data-creator/.env` подхватывается автоматически — SSL включён, подключение к `localhost:9093`.

```bash
# Просмотр структуры Avro-схемы
uv run describe_schema.py ../schemas/all_order_v2.json
```

---

## Переменные окружения SSL

Оба приложения поддерживают SSL через `.env` (загружается `uv run` автоматически):

| Переменная | Описание | По умолчанию |
|-----------|----------|--------------|
| `KAFKA_SSL` | Включить SSL (`1` / `0`) | `0` |
| `KAFKA_PFX_PATH` | Абсолютный путь к `client-full.pfx` | — |
| `KAFKA_PFX_PASSWORD` | Пароль от PFX | — |
| `KAFKA_BOOTSTRAP` | Адрес брокера | `localhost:9093` если SSL, иначе `9092` |
| `SCHEMA_REGISTRY_URL` | URL Schema Registry | `http://localhost:8082` |

---

## Развёртывание на сервере (k3s)

```bash
scp infra/camunda-k3s-setup.sh user@server:~/
ssh user@server "bash camunda-k3s-setup.sh"
```

Подробнее: [docs/deployment.md](docs/deployment.md)

---

## Документация

| Документ | Описание |
|----------|----------|
| [docs/deployment.md](docs/deployment.md) | Развёртывание Camunda 8 на VPS через k3s + Helm |
| [docs/kafka-ssl-pfx-guide.md](docs/kafka-ssl-pfx-guide.md) | Подключение к Kafka по SSL/TLS с PFX-сертификатом (c8run, Docker Compose, k3s) |
| [docs/status-transition-validation.md](docs/status-transition-validation.md) | Валидация переходов статусов заказов |
| [docs/statuses.md](docs/statuses.md) | Справочник статусов заказа Галактика |
