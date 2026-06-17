# Подключение Camunda к Kafka по SSL/TLS с PFX-сертификатом

Kafka уже запущена с TLS. Есть один PKCS12/PFX-файл сертификата.

## Различия по окружениям

| | c8run (локально) | Docker Compose | k3s |
|---|---|---|---|
| bootstrap.servers | `localhost:9093` | `kafka:9093` | `<server-ip>:9093` |
| Путь к PFX | `/path/to/kafka-ssl/client-full.pfx` | `/mnt/kafka-ssl/client-full.pfx` | `/mnt/kafka-ssl/client-full.pfx` |
| Хранение пароля | `export` в шелле / `.env` | `.env` файл | k8s Secret |
| Монтирование PFX | не нужно (локальный файл) | bind mount | k8s Secret + volumeMount |

---

## Шаг 1. Локальная Kafka в Docker — настроить `localhost`

Если Kafka поднята в Docker и нужен доступ с хоста (`localhost:9093`), advertised listener должен быть `localhost`, а не IP сервера.

```yaml
# docker-compose.yml (фрагмент)
services:
  kafka:
    image: confluentinc/cp-kafka:7.7.1
    ports:
      - "9092:9092"
      - "9093:9093"
    environment:
      KAFKA_LISTENERS: INTERNAL://0.0.0.0:29092,EXTERNAL://0.0.0.0:9092,SSL://0.0.0.0:9093,CONTROLLER://0.0.0.0:9094
      KAFKA_ADVERTISED_LISTENERS: INTERNAL://kafka:29092,EXTERNAL://localhost:9092,SSL://localhost:9093
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT,SSL:SSL,CONTROLLER:PLAINTEXT
      # SSL-сертификаты брокера (не клиентские)
      KAFKA_SSL_KEYSTORE_FILENAME: kafka.broker.keystore.p12
      KAFKA_SSL_KEYSTORE_TYPE: PKCS12
      KAFKA_SSL_KEYSTORE_CREDENTIALS: keystore_creds
      KAFKA_SSL_KEY_CREDENTIALS: keystore_creds
      KAFKA_SSL_TRUSTSTORE_FILENAME: kafka.broker.truststore.p12
      KAFKA_SSL_TRUSTSTORE_TYPE: PKCS12
      KAFKA_SSL_TRUSTSTORE_CREDENTIALS: truststore_creds
      KAFKA_SSL_CLIENT_AUTH: required        # mTLS; при TLS-only: none
      KAFKA_SSL_ENDPOINT_IDENTIFICATION_ALGORITHM: ""
    volumes:
      - ./kafka-ssl:/etc/kafka/secrets:ro    # папка с broker keystore/truststore
```

> Если используется один и тот же `docker-compose.yml` и для сервера, и локально — выносите `KAFKA_ADVERTISED_LISTENERS` в `.env` файл и переопределяйте по окружению.

---

## Шаг 2. Сохранить пароль от PFX

### c8run (локально)

Создать файл `.env` в директории проекта (добавить в `.gitignore`):

```bash
# kafka-ssl/.env  или в корне проекта
KAFKA_PFX_PASSWORD=ваш_пароль
```

Загрузить перед запуском c8run:

```bash
source kafka-ssl/.env
/path/to/c8run-8.9.8/c8run start
```

Или передать напрямую при запуске:

```bash
KAFKA_PFX_PASSWORD=ваш_пароль /path/to/c8run-8.9.8/c8run start
```

### Docker Compose

Создать `.env` рядом с `docker-compose.yml` (добавить в `.gitignore`):

```bash
# .env
KAFKA_PFX_PASSWORD=ваш_пароль
```

В `docker-compose.yml` для connectors:

```yaml
services:
  connectors:
    image: camunda/connectors-bundle:8.9.5
    env_file: .env
    volumes:
      - ./kafka-ssl/client-full.pfx:/mnt/kafka-ssl/client-full.pfx:ro
```

### k3s / Kubernetes

```bash
kubectl create secret generic kafka-ssl-passwords \
  --from-literal=KAFKA_PFX_PASSWORD=ваш_пароль \
  -n camunda
```

В Helm values (`values-connectors-patch.yaml`):

```yaml
connectors:
  extraVolumes:
    - name: kafka-ssl
      secret:
        secretName: kafka-ssl-pfx        # отдельный secret с самим PFX-файлом
  extraVolumeMounts:
    - name: kafka-ssl
      mountPath: /mnt/kafka-ssl
      readOnly: true
  env:
    - name: KAFKA_PFX_PASSWORD
      valueFrom:
        secretKeyRef:
          name: kafka-ssl-passwords
          key: KAFKA_PFX_PASSWORD
```

Создать secret с PFX-файлом:

```bash
kubectl create secret generic kafka-ssl-pfx \
  --from-file=client-full.pfx=./kafka-ssl/client-full.pfx \
  -n camunda

helm upgrade camunda camunda/camunda-platform -n camunda -f values-connectors-patch.yaml
```

---

## Шаг 3. Настроить BPMN-коннектор

В Camunda Modeler: Kafka Inbound Connector → поле `additionalProperties` → FEEL-выражение.

Секреты подхватываются из env vars c8run автоматически (переменная с префиксом `SECRET_` становится `{{secrets.ИМЯ}}`).

### Описание SSL-переменных

| Переменная | Назначение |
|---|---|
| `security.protocol` | Протокол подключения к брокеру. `SSL` — шифрование + аутентификация по сертификату. |
| `ssl.keystore.location` | Путь к **keystore** — файлу с *клиентским* сертификатом и приватным ключом. Брокер запрашивает его для проверки клиента (mTLS). |
| `ssl.keystore.type` | Формат keystore. `PKCS12` (`.pfx`) — стандартный кроссплатформенный формат. Альтернатива: `JKS`. |
| `ssl.keystore.password` | Пароль для открытия файла keystore. |
| `ssl.key.password` | Пароль к приватному ключу внутри keystore. Как правило совпадает с `ssl.keystore.password`. |
| `ssl.truststore.location` | Путь к **truststore** — файлу с CA-сертификатами, которым *доверяет клиент*. Используется для проверки сертификата брокера. Должен быть создан через `keytool` (содержит CA как `trustedCertEntry`). |
| `ssl.truststore.type` | Формат truststore. Аналогично `ssl.keystore.type`. |
| `ssl.truststore.password` | Пароль для открытия файла truststore. |
| `ssl.endpoint.identification.algorithm` | Алгоритм проверки hostname в сертификате брокера. Пустая строка `""` — отключает проверку (нужно для self-signed сертификатов, где CN не совпадает с `localhost`). |

### Полный конфиг — mTLS (keystore + отдельный truststore)

```feel
={
  "security.protocol": "SSL",
  "ssl.keystore.location": "{{secrets.KAFKA_PFX_PATH}}",
  "ssl.keystore.type": "PKCS12",
  "ssl.keystore.password": "{{secrets.KAFKA_PFX_PASSWORD}}",
  "ssl.key.password": "{{secrets.KAFKA_PFX_PASSWORD}}",
  "ssl.truststore.location": "{{secrets.KAFKA_TRUSTSTORE_PATH}}",
  "ssl.truststore.type": "PKCS12",
  "ssl.truststore.password": "{{secrets.KAFKA_PFX_PASSWORD}}",
  "ssl.endpoint.identification.algorithm": ""
}
```

Переменные в `c8run-8.9.8/.env`:

```bash
SECRET_KAFKA_PFX_PATH=/absolute/path/to/kafka-ssl/client-full.pfx
SECRET_KAFKA_TRUSTSTORE_PATH=/absolute/path/to/kafka-ssl/client.truststore.p12
SECRET_KAFKA_PFX_PASSWORD=your_password
```

**Пути к файлу по окружениям:**

| Окружение | `ssl.keystore.location` / `ssl.truststore.location` |
|-----------|-----------------------------------------------------|
| c8run (Mac) | `/path/to/kafka-ssl/client-full.pfx` |
| Docker Compose | `/mnt/kafka-ssl/client-full.pfx` |
| k3s | `/mnt/kafka-ssl/client-full.pfx` |

### Только truststore (TLS без mTLS)

Брокер не требует клиентский сертификат (`ssl.client.auth=none`). PFX содержит только CA:

```feel
={
  "security.protocol": "SSL",
  "ssl.truststore.location": "/путь/к/client-full.pfx",
  "ssl.truststore.type": "PKCS12",
  "ssl.truststore.password": secrets.KAFKA_PFX_PASSWORD,
  "ssl.endpoint.identification.algorithm": ""
}
```

### Только keystore (CA встроен в JVM / публичный CA)

```feel
={
  "security.protocol": "SSL",
  "ssl.keystore.location": "/путь/к/client-full.pfx",
  "ssl.keystore.type": "PKCS12",
  "ssl.keystore.password": secrets.KAFKA_PFX_PASSWORD,
  "ssl.key.password": secrets.KAFKA_PFX_PASSWORD,
  "ssl.endpoint.identification.algorithm": ""
}
```

### Остальные параметры коннектора

| Поле | c8run / локально | Docker Compose / k3s |
|------|------------------|----------------------|
| `authenticationType` | `custom` | `custom` |
| `topic.bootstrapServers` | `localhost:9093` | `kafka:9093` / `<ip>:9093` |
| `topic.topicName` | `all_orders` | `all_orders` |
| `groupId` | `camunda-order-processor` | `camunda-order-processor` |
| `autoOffsetReset` | `latest` | `latest` |

---

## Почему `additionalProperties`, а не отдельные свойства

`KafkaConnectorProperties` в `connector-kafka-8.9.x.jar` не имеет полей `securityProtocol`, `ssl.*`. Все SSL-параметры передаются только через `additionalProperties` (`@FEEL Map`), который применяется последним через `props.putAll()` и перекрывает дефолтный `security.protocol=PLAINTEXT`.

---

## Проверка

```bash
# c8run — логи коннектора
tail -f /path/to/c8run-8.9.8/log/connectors.log | grep -E "SSL|Consumer status|security"

# k3s
kubectl -n camunda logs -l app=camunda-connectors --tail=50 | grep -E "SSL|Consumer status"
```

Ожидаемое: `Kafka Consumer status changed to UP`

```bash
# Consumer-группы на брокере
docker exec kafka kafka-consumer-groups.sh \
  --bootstrap-server localhost:9092 \
  --describe --group camunda-order-processor
# STATE = Stable → подключено
```

---

## Частые ошибки

| Симптом | Причина | Решение |
|---------|---------|---------|
| `SSL handshake failed` | SSL-параметры не в `additionalProperties` | Перенести все `ssl.*` в FEEL Map |
| `Connection refused :9093` | Kafka advertises IP сервера вместо localhost | Проверить `KAFKA_ADVERTISED_LISTENERS` |
| `PKCS12 keystore not found` | Файл не смонтирован или путь неверный | Проверить mount / абсолютный путь |
| `PKIX path building failed` | CA не в truststore | PFX должен содержать CA-сертификат |
| `secrets.KAFKA_PFX_PASSWORD is null` | Env var не установлена до старта | `source .env` перед `c8run start` |
| Hostname mismatch | CN сертификата ≠ hostname | Добавить `"ssl.endpoint.identification.algorithm": ""` |
