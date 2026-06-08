# CamundaData — Производственная практика

Проект производственной практики: интеграция ERP-системы **Галактика** с шиной данных на базе **Apache Kafka** через процессный движок **Camunda 8**.

Цель — отработать сквозной поток данных о заказах: от инициации BPMN-процесса в Camunda до публикации сообщений в Kafka в формате Avro.

---

## Архитектура

```
┌──────────────┐    gRPC     ┌──────────────┐    Avro/Kafka    ┌─────────────────┐
│   Camunda 8  │ ──────────► │ Zeebe Worker │ ───────────────► │  Apache Kafka   │
│  (k3s / VPS) │             │  (Python)    │                  │ + Schema Regist.│
└──────────────┘             └──────────────┘                  └─────────────────┘
       │
  BPMN-процесс
  order-lifecycle.bpmn
```

**Стек:**
- **Camunda 8.9** — BPMN-движок
- **Zeebe** — движок исполнения процессов (gRPC на порту 26500)
- **Apache Kafka** + **Confluent Schema Registry** — шина сообщений (Docker Compose)
- **Apache Avro** — схема сообщений (`all_order_v2`)
- **Python** + `pyzeebe` + `aiokafka` — Zeebe job worker
- **Python** + `confluent-kafka` + `rich` — CLI-инструмент для генерации тестовых данных

---

## Структура репозитория

```
.
├── bpmn/                     # BPMN-диаграммы и формы
│   ├── order-lifecycle.bpmn  # Основной процесс жизненного цикла заказа
│   ├── order-kafka-process.bpmn
│   └── ConfirmOrderForm.form
│
├── schemas/                  # Avro-схемы сообщений
│   └── all_order_v2.json
│
├── kafka/                    # Kafka + Schema Registry (Docker Compose)
│   └── docker-compose.yml
│
├── data-creator/             # CLI-инструмент: генерация и отправка тестовых заказов
│   ├── producer.py           # Точка входа (интерактивное меню)
│   ├── describe_schema.py    # Утилита для просмотра структуры Avro-схемы
│   └── generators/
│       └── all_order_v2.py   # Доменный генератор случайных заказов
│
├── worker/                   # Zeebe job worker
│   └── worker.py             # Читает задачи из Camunda, публикует в Kafka
│
├── infra/                    # Скрипты развёртывания
│   └── camunda-k3s-setup.sh  # Автоматическая установка Camunda на VPS (k3s + Helm)
│
└── docs/                     # Документация
    ├── deployment.md         # Инструкция по развёртыванию Camunda
    └── statuses.md           # Справочник статусов заказа (Галактика)
```

---

## Быстрый старт

### 1. Kafka локально

```bash
cd kafka
docker compose up -d
```

Сервисы после запуска:
- Kafka: `localhost:9092`
- Schema Registry: `http://localhost:8082`
- Kafka UI: `http://localhost:8081`

### 2. Генератор тестовых данных

```bash
cd data-creator
uv sync
uv run producer.py
```

Интерактивное меню позволяет выбрать схему, количество сообщений и режим заполнения.

```bash
# Просмотр структуры схемы
uv run describe_schema.py ../schemas/all_order_v2.json
```

### 3. Zeebe Worker

```bash
cd worker
uv sync
uv run worker.py
```

Worker подключается к Zeebe на `localhost:26500` и ждёт задачи типа `write-order-to-kafka`.

### 4. Развёртывание Camunda на VPS

```bash
# Скопировать скрипт на сервер и запустить
scp infra/camunda-k3s-setup.sh user@server:~/
ssh user@server "bash camunda-k3s-setup.sh"
```

Подробнее: [docs/deployment.md](docs/deployment.md)

---

## Документация

| Документ | Описание |
|----------|----------|
| [docs/deployment.md](docs/deployment.md) | Развёртывание Camunda 8 на VPS через k3s |
| [docs/statuses.md](docs/statuses.md) | Справочник статусов заказа Галактика |

---

## Требования

| Компонент | Версия |
|-----------|--------|
| Python | ≥ 3.11 |
| uv | любая |
| Docker | ≥ 24 |
| Docker Compose | v2 |
