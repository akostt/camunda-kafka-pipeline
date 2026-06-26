# Подключение Camunda к Kafka по SSL/TLS с PFX-сертификатом

Руководство для подключения Camunda 8 Connectors к Kafka с mTLS-аутентификацией через PFX/PKCS12 сертификаты.

## Различия по окружениям

| | c8run (локально) | k3s |
|---|---|---|
| bootstrap.servers | `localhost:9093` | `<server-ip>:9093` |
| Путь к PFX | `/path/to/kafka-ssl/client-full.pfx` | `/mnt/kafka-ssl/client-full.pfx` |
| Хранение пароля | `.env` файл | k8s Secret |
| Монтирование PFX | не нужно (локальный файл) | k8s Secret + volumeMount |

---

## Генерация сертификатов

Скрипт `infra/camunda-k3s-setup.sh` генерирует все сертификаты автоматически.  
Для ручной генерации:

```bash
# Корневой CA
openssl genrsa -out ca.key 4096
openssl req -new -x509 -days 3650 -key ca.key -out ca.crt -subj "/CN=KafkaCA/O=Project"

# Брокер (CN = IP-адрес сервера)
keytool -genkeypair -alias broker -keyalg RSA -keysize 2048 \
  -dname "CN=<SERVER_IP>,O=Project" \
  -keystore kafka.broker.keystore.p12 -storetype PKCS12 \
  -storepass <PASS> -keypass <PASS> -validity 3650

keytool -certreq -alias broker -keystore kafka.broker.keystore.p12 \
  -storetype PKCS12 -storepass <PASS> -file broker.csr

openssl x509 -req -days 3650 -in broker.csr -CA ca.crt -CAkey ca.key \
  -CAcreateserial -out broker-signed.crt

keytool -import -alias CARoot -noprompt -file ca.crt \
  -keystore kafka.broker.keystore.p12 -storetype PKCS12 -storepass <PASS>
keytool -import -alias broker -noprompt -file broker-signed.crt \
  -keystore kafka.broker.keystore.p12 -storetype PKCS12 -storepass <PASS>
keytool -import -alias CARoot -noprompt -file ca.crt \
  -keystore kafka.broker.truststore.p12 -storetype PKCS12 -storepass <PASS>

# Клиент (для Connectors)
keytool -genkeypair -alias client -keyalg RSA -keysize 2048 \
  -dname "CN=camunda-client,O=Project" \
  -keystore client-full.pfx -storetype PKCS12 \
  -storepass <PASS> -keypass <PASS> -validity 3650

keytool -certreq -alias client -keystore client-full.pfx \
  -storetype PKCS12 -storepass <PASS> -file client.csr

openssl x509 -req -days 3650 -in client.csr -CA ca.crt -CAkey ca.key \
  -CAcreateserial -out client-signed.crt

keytool -import -alias CARoot -noprompt -file ca.crt \
  -keystore client-full.pfx -storetype PKCS12 -storepass <PASS>
keytool -import -alias client -noprompt -file client-signed.crt \
  -keystore client-full.pfx -storetype PKCS12 -storepass <PASS>
keytool -import -alias CARoot -noprompt -file ca.crt \
  -keystore client.truststore.p12 -storetype PKCS12 -storepass <PASS>
```

---

## Kafka — конфигурация брокера

### Docker Compose (KRaft mode)

```yaml
services:
  kafka:
    image: confluentinc/cp-kafka:7.7.1
    container_name: kafka
    restart: unless-stopped
    ports:
      - "9092:9092"    # plaintext (для управления и Schema Registry)
      - "9093:9093"    # SSL (для Connectors)
    environment:
      CLUSTER_ID: "<uuid-base64>"
      KAFKA_NODE_ID: 1
      KAFKA_PROCESS_ROLES: "broker,controller"
      KAFKA_LISTENERS: "INTERNAL://0.0.0.0:29092,EXTERNAL://0.0.0.0:9092,SSL://0.0.0.0:9093,CONTROLLER://0.0.0.0:9094"
      KAFKA_ADVERTISED_LISTENERS: "INTERNAL://kafka:29092,EXTERNAL://${SERVER_IP}:9092,SSL://${SERVER_IP}:9093"
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: "INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT,SSL:SSL,CONTROLLER:PLAINTEXT"
      KAFKA_CONTROLLER_LISTENER_NAMES: "CONTROLLER"
      KAFKA_INTER_BROKER_LISTENER_NAME: "INTERNAL"
      KAFKA_CONTROLLER_QUORUM_VOTERS: "1@kafka:9094"
      KAFKA_SSL_KEYSTORE_FILENAME: "kafka.broker.keystore.p12"
      KAFKA_SSL_KEYSTORE_TYPE: "PKCS12"
      KAFKA_SSL_KEYSTORE_CREDENTIALS: "keystore_creds"
      KAFKA_SSL_KEY_CREDENTIALS: "keystore_creds"
      KAFKA_SSL_TRUSTSTORE_FILENAME: "kafka.broker.truststore.p12"
      KAFKA_SSL_TRUSTSTORE_TYPE: "PKCS12"
      KAFKA_SSL_TRUSTSTORE_CREDENTIALS: "truststore_creds"
      KAFKA_SSL_CLIENT_AUTH: "required"         # mTLS: клиент обязан предоставить сертификат
      KAFKA_SSL_ENDPOINT_IDENTIFICATION_ALGORITHM: ""   # отключает hostname verification (self-signed)
    volumes:
      - ./kafka-ssl:/etc/kafka/secrets:ro
      - kafka-data:/var/lib/kafka/data

  schema-registry:
    image: confluentinc/cp-schema-registry:7.7.1
    container_name: schema-registry
    restart: unless-stopped
    ports:
      - "8081:8081"
    environment:
      SCHEMA_REGISTRY_HOST_NAME: schema-registry
      SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS: "kafka:29092"
      SCHEMA_REGISTRY_LISTENERS: "http://0.0.0.0:8081"
    depends_on:
      - kafka

volumes:
  kafka-data:
```

> Файлы в `/etc/kafka/secrets`: `kafka.broker.keystore.p12`, `kafka.broker.truststore.p12`, `keystore_creds`, `truststore_creds`.

---

## Хранение пароля от PFX

### c8run (локально)

```bash
# .env в корне проекта (добавить в .gitignore)
SECRET_KAFKA_PFX_PATH=/path/to/kafka-ssl/client-full.pfx
SECRET_KAFKA_TRUSTSTORE_PATH=/path/to/kafka-ssl/client.truststore.p12
SECRET_KAFKA_PFX_PASSWORD=ваш_пароль
```

Загрузить перед запуском:
```bash
source .env && c8run start
```

### k3s / Kubernetes

```bash
kubectl create secret generic kafka-ssl-certs \
  --from-literal=KAFKA_PFX_PASSWORD=ваш_пароль \
  --from-file=client-full.pfx=./kafka-ssl/client-full.pfx \
  --from-file=client-truststore.p12=./kafka-ssl/client.truststore.p12 \
  -n camunda
```

В `values-camunda.yaml`:

```yaml
connectors:
  env:
    - name: KAFKA_PFX_PASSWORD
      valueFrom:
        secretKeyRef:
          name: kafka-ssl-certs
          key: KAFKA_PFX_PASSWORD
    - name: KAFKA_PFX_PATH
      value: "/mnt/kafka-ssl/client-full.pfx"
    - name: KAFKA_TRUSTSTORE_PATH
      value: "/mnt/kafka-ssl/client.truststore.p12"
  extraVolumes:
    - name: kafka-ssl
      secret:
        secretName: kafka-ssl-certs
  extraVolumeMounts:
    - name: kafka-ssl
      mountPath: /mnt/kafka-ssl
      readOnly: true
```

---

## Настройка BPMN-коннектора

В Camunda Modeler в поле `additionalProperties` (FEEL-выражение):

### Описание SSL-параметров

| Параметр | Назначение |
|---|---|
| `security.protocol` | `SSL` — шифрование + mTLS аутентификация |
| `ssl.keystore.location` | Путь к клиентскому keystore (PFX с ключом и сертификатом) |
| `ssl.keystore.type` | `PKCS12` для `.pfx` файлов |
| `ssl.keystore.password` | Пароль keystore |
| `ssl.key.password` | Пароль приватного ключа (обычно совпадает с keystore.password) |
| `ssl.truststore.location` | Путь к truststore с CA-сертификатом брокера |
| `ssl.truststore.type` | `PKCS12` |
| `ssl.truststore.password` | Пароль truststore |
| `ssl.endpoint.identification.algorithm` | `""` — отключает проверку hostname (для self-signed) |

### Полный конфиг mTLS (keystore + truststore)

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

### Остальные параметры коннектора

| Поле | c8run / локально | k3s |
|---|---|---|
| `authenticationType` | `custom` | `custom` |
| `topic.bootstrapServers` | `localhost:9093` | `<SERVER_IP>:9093` |
| `topic.topicName` | `all_orders` | `all_orders` |
| `groupId` | `camunda-lc-start` | `camunda-lc-start` |
| `schemaRegistryUrl` | `http://localhost:8081` | `http://<SERVER_IP>:8081` |
| `autoOffsetReset` | `latest` | `latest` |

---

## Почему `additionalProperties`, а не отдельные свойства

`KafkaConnectorProperties` в `connector-kafka-8.9.x.jar` не имеет полей `securityProtocol`, `ssl.*`. Все SSL-параметры передаются только через `additionalProperties` (`@FEEL Map`), который применяется через `props.putAll()` и перекрывает дефолтный `security.protocol=PLAINTEXT`.

---

## Проверка

```bash
# Логи Connectors в k3s — ждём "Consumer status changed to UP"
kubectl -n camunda logs deployment/camunda-connectors --tail=50 | grep -E "SSL|Consumer status|Kafka"

# Consumer-группы на брокере
docker exec kafka kafka-consumer-groups.sh \
  --bootstrap-server localhost:9092 \
  --describe --group camunda-lc-start
# STATE = Stable → подключено
```

---

## Частые ошибки

| Симптом | Причина | Решение |
|---|---|---|
| `SSL handshake failed` | SSL-параметры не в `additionalProperties` | Перенести все `ssl.*` в FEEL Map |
| `Connection refused :9093` | Kafka advertises неверный адрес | Проверить `KAFKA_ADVERTISED_LISTENERS` |
| `PKCS12 keystore not found` | Файл не смонтирован или путь неверный | Проверить mount и абсолютный путь |
| `PKIX path building failed` | CA не в truststore | PFX должен содержать CA-сертификат |
| `secrets.KAFKA_PFX_PASSWORD is null` | Env var не установлена | Проверить k8s secret и connectors.env в values |
| Hostname mismatch | CN сертификата ≠ hostname | Добавить `"ssl.endpoint.identification.algorithm": ""` |
| `UNAUTHENTICATED` при gRPC к Zeebe | Connectors не получают JWT от Keycloak | Добавить `CAMUNDA_CLIENT_ID/SECRET/AUTH_SERVER_URL` в connectors.env |
