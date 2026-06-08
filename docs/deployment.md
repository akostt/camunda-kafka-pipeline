# Camunda 8 — Production Deployment Guide
## Kubernetes (k3s) + Helm + PostgreSQL (без Elasticsearch)

---

## Архитектура

```
┌────────────────────────────────────────────────────┐
│  Ubuntu 22.04 (k3s single-node Kubernetes)        │
│                                                    │
│  ┌──────────────────────┐  ┌──────────────────┐  │
│  │  Orchestration Pod   │  │  Connectors Pod  │  │
│  │  Zeebe + Operate +   │  │  (Kafka и др.)   │  │
│  │  Tasklist + Admin    │  │                  │  │
│  └──────────┬───────────┘  └──────────────────┘  │
│             │ PostgreSQL JDBC                      │
│  ┌──────────▼───────────┐                         │
│  │  PostgreSQL (CNPG)   │                         │
│  └──────────────────────┘                         │
└────────────────────────────────────────────────────┘
```

**Доступ после установки:**

| Сервис | URL |
|--------|-----|
| Operate (мониторинг) | http://<SERVER_IP>:8080/operate |
| Tasklist (задачи) | http://<SERVER_IP>:8080/tasklist |
| Admin (пользователи) | http://<SERVER_IP>:8080/identity |
| Zeebe gRPC (деплой) | <SERVER_IP>:26500 |
| Zeebe REST API | http://<SERVER_IP>:8080 |

**Учётные данные по умолчанию — сменить после первого входа:**
- Основной пользователь: `demo` / `demo`

---

## Шаг 1 — Подготовка сервера

```bash
ssh <SERVER_IP> -l user1
sudo apt-get update && sudo apt-get upgrade -y
```

---

## Шаг 2 — Установка k3s (Kubernetes)

```bash
curl -sfL https://get.k3s.io | sh -
```

Настройка доступа для текущего пользователя:
```bash
mkdir -p ~/.kube
sudo cp /etc/rancher/k3s/k3s.yaml ~/.kube/config
sudo chown $(id -u):$(id -g) ~/.kube/config
chmod 600 ~/.kube/config
echo 'export KUBECONFIG=~/.kube/config' >> ~/.bashrc
export KUBECONFIG=~/.kube/config
```

Проверка (нода должна быть `Ready`):
```bash
kubectl get nodes
```

---

## Шаг 3 — Установка Helm

```bash
curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
```

---

## Шаг 4 — Установка оператора CloudNativePG (PostgreSQL)

Актуальный URL манифеста — на странице [релизов CloudNativePG](https://github.com/cloudnative-pg/cloudnative-pg/releases/latest).
Установка последнего стабильного релиза (release-1.28 на момент написания):

```bash
kubectl apply --server-side \
  -f https://raw.githubusercontent.com/cloudnative-pg/cloudnative-pg/release-1.28/releases/cnpg-1.28.0.yaml

kubectl wait --for=condition=available deployment/cnpg-controller-manager \
  -n cnpg-system --timeout=120s
```

---

## Шаг 5 — Namespace и секреты паролей

```bash
kubectl create namespace camunda
mkdir -p ~/camunda-deploy
```

Придумай два пароля — для суперпользователя PostgreSQL и для пользователя приложения.  
Требование: только буквы и цифры, без спецсимволов (`@`, `$`, `"` и т.п.) — они ломают bash-команды.

Например: `MyPostgresAdmin2024` и `CamundaAppPass2024`.

Сохрани пароли в файл:
```bash
cat > ~/camunda-deploy/passwords.txt << 'EOF'
PG_SUPERUSER_PASS=MyPostgresAdmin2024
PG_APP_PASS=CamundaAppPass2024
EOF
chmod 600 ~/camunda-deploy/passwords.txt
```

Создай Kubernetes secrets — подставь **свои** пароли:
```bash
kubectl create secret generic pg-superuser-secret \
  --from-literal=username=postgres \
  --from-literal=password="MyPostgresAdmin2024" \
  -n camunda

kubectl create secret generic pg-camunda-secret \
  --from-literal=username=camundauser \
  --from-literal=password="CamundaAppPass2024" \
  -n camunda
```

> Файл `passwords.txt` не коммить в git.

---

## Шаг 6 — Развёртывание PostgreSQL

Сохрани как `~/camunda-deploy/postgresql-cluster.yaml`:

```yaml
apiVersion: postgresql.cnpg.io/v1
kind: Cluster
metadata:
  name: pg-camunda
  namespace: camunda
spec:
  instances: 1
  imageName: ghcr.io/cloudnative-pg/postgresql:17.9
  storage:
    size: 8Gi
  superuserSecret:
    name: pg-superuser-secret
  bootstrap:
    initdb:
      database: camundadb
      owner: camundauser
      secret:
        name: pg-camunda-secret
  resources:
    requests:
      memory: 256Mi
      cpu: 100m
    limits:
      memory: 512Mi
      cpu: 500m
```

Применяем и ждём (~2 мин):
```bash
kubectl apply -f ~/camunda-deploy/postgresql-cluster.yaml
kubectl wait --for=condition=Ready cluster/pg-camunda -n camunda --timeout=180s
```

Готов, когда `kubectl get cluster pg-camunda -n camunda` показывает `Cluster in healthy state`.

---

## Шаг 7 — Добавление Helm-репозитория Camunda

```bash
helm repo add camunda https://helm.camunda.io
helm repo update
```

---

## Шаг 8 — Helm values-файл

Сохрани как `~/camunda-deploy/values-camunda.yaml`:

```yaml
global:
  identity:
    auth:
      enabled: false   # Basic auth — Keycloak не нужен

orchestration:
  enabled: true
  replicaCount: 1
  clusterSize: "1"
  partitionCount: "1"
  replicationFactor: "1"
  affinity:
    podAntiAffinity: {}   # Нужно для single-node Kubernetes

  exporters:
    camunda:
      enabled: false
    rdbms:
      enabled: true

  data:
    secondaryStorage:
      type: rdbms
      rdbms:
        url: "jdbc:postgresql://pg-camunda-rw.camunda.svc.cluster.local:5432/camundadb"
        username: camundauser
        secret:
          existingSecret: pg-camunda-secret
          existingSecretKey: password

  resources:
    requests:
      memory: "512Mi"
      cpu: "250m"
    limits:
      memory: "1.5Gi"
      cpu: "1500m"

  service:
    type: LoadBalancer   # k3s автоматически назначит внешний IP сервера
    headless:
      type: ClusterIP

connectors:
  enabled: true
  inbound:
    mode: disabled
  resources:
    requests:
      memory: "256Mi"
      cpu: "100m"
    limits:
      memory: "512Mi"
      cpu: "500m"
  env: []   # Connector secrets добавляются сюда, см. раздел ниже

elasticsearch:
  enabled: false
identity:
  enabled: false
optimize:
  enabled: false
webModeler:
  enabled: false
console:
  enabled: false
```

---

## Шаг 9 — Установка Camunda

```bash
helm install camunda camunda/camunda-platform \
  -f ~/camunda-deploy/values-camunda.yaml \
  -n camunda \
  --timeout 15m
```

Ждём запуска подов (~5–10 мин — JVM стартует медленно):
```bash
kubectl get pods -n camunda -w
```

Всё готово, когда все поды показывают `1/1 Running`:
```
camunda-connectors-xxx   1/1   Running
camunda-zeebe-0          1/1   Running
pg-camunda-1             1/1   Running
```

Открой в браузере: http://<SERVER_IP>:8080/operate  
Логин: `demo` / `demo`

---

## Настройка Connector Secrets

Connector secrets — секретные значения (пароли, токены, пути к файлам), которые задаются один раз на сервере и используются в BPMN-диаграммах через `{{secrets.ИМЯ}}` — без хранения значений в самих диаграммах.

### Как добавить secret

**Шаг 1.** Создай Kubernetes secret с нужными значениями:

```bash
# Пример: пароли и файлы сертификатов для Kafka SSL
kubectl create secret generic kafka-ssl-certs \
  --from-literal=KAFKA_TRUSTSTORE_PASS="your-truststore-password" \
  --from-literal=KAFKA_KEYSTORE_PASS="your-keystore-password" \
  --from-literal=KAFKA_KEY_PASS="your-key-password" \
  --from-file=truststore.jks=/путь/к/truststore.jks \
  --from-file=keystore.jks=/путь/к/keystore.jks \
  -n camunda
```

**Шаг 2.** Добавь в `values-camunda.yaml` в секцию `connectors`:

```yaml
connectors:
  env:
    # Пароли из Kubernetes secret
    - name: KAFKA_TRUSTSTORE_PASS
      valueFrom:
        secretKeyRef:
          name: kafka-ssl-certs
          key: KAFKA_TRUSTSTORE_PASS
    - name: KAFKA_KEYSTORE_PASS
      valueFrom:
        secretKeyRef:
          name: kafka-ssl-certs
          key: KAFKA_KEYSTORE_PASS
    - name: KAFKA_KEY_PASS
      valueFrom:
        secretKeyRef:
          name: kafka-ssl-certs
          key: KAFKA_KEY_PASS
    # Пути к файлам сертификатов внутри контейнера
    - name: KAFKA_SSL_TRUSTSTORE_LOCATION
      value: "/etc/kafka/ssl/truststore.jks"
    - name: KAFKA_SSL_KEYSTORE_LOCATION
      value: "/etc/kafka/ssl/keystore.jks"

  # Монтирует файлы из secret в контейнер
  extraVolumes:
    - name: kafka-ssl
      secret:
        secretName: kafka-ssl-certs
  extraVolumeMounts:
    - name: kafka-ssl
      mountPath: /etc/kafka/ssl
      readOnly: true
```

**Шаг 3.** Применяем:

```bash
helm upgrade camunda camunda/camunda-platform \
  -f ~/camunda-deploy/values-camunda.yaml \
  -n camunda
```

### Использование в BPMN-диаграмме

В Camunda Modeler в полях Kafka-коннектора указывай:
```
{{secrets.KAFKA_TRUSTSTORE_PASS}}
{{secrets.KAFKA_SSL_TRUSTSTORE_LOCATION}}
```

Переменная окружения `KAFKA_TRUSTSTORE_PASS` в контейнере автоматически становится доступна как `secrets.KAFKA_TRUSTSTORE_PASS` в диаграммах.

### Простые значения без файлов

Для паролей и токенов без файлов — можно не создавать K8s secret, а написать прямо в values:

```yaml
connectors:
  env:
    - name: KAFKA_BOOTSTRAP_SERVERS
      value: "kafka.example.com:9093"
    - name: MY_API_TOKEN
      value: "actual-token-value"
```

---

## Полезные команды

```bash
# Статус системы
kubectl get pods,svc -n camunda

# Логи Zeebe (Operate/Tasklist/Admin)
kubectl logs -n camunda camunda-zeebe-0 -f

# Логи Connectors
kubectl logs -n camunda deployment/camunda-connectors -f

# Перезапуск после изменения secrets
kubectl rollout restart deployment/camunda-connectors -n camunda

# Обновление Camunda
helm repo update
helm upgrade camunda camunda/camunda-platform \
  -f ~/camunda-deploy/values-camunda.yaml \
  -n camunda
```

---

## Устранение неполадок

**Pod завис в Pending:**
```bash
kubectl describe pod <имя-пода> -n camunda
```
Чаще всего: нехватка памяти или anti-affinity (добавь `affinity.podAntiAffinity: {}` в values).

**Ошибка подключения к PostgreSQL:**
```bash
kubectl logs -n camunda camunda-zeebe-0 2>&1 | grep -i 'error\|postgres'
```

**Полная переустановка:**
```bash
helm uninstall camunda -n camunda
kubectl delete pvc -n camunda --all
# PostgreSQL cluster останется — пересоздавать не нужно
helm install camunda camunda/camunda-platform \
  -f ~/camunda-deploy/values-camunda.yaml \
  -n camunda --timeout 15m
```

---

## Структура файлов

```
~/camunda-deploy/
├── passwords.txt            # Пароли БД (не коммитить!)
├── postgresql-cluster.yaml  # PostgreSQL cluster
└── values-camunda.yaml      # Helm values для Camunda
```

---

## Следующие шаги (опционально)

**OIDC-аутентификация (Keycloak)** — если нужна SSO вместо basic auth:  
Установи `global.identity.auth.enabled: true` в values и добавь Keycloak. Требует домен + TLS.

**HTTPS** — Traefik уже встроен в k3s. Добавь cert-manager + Let's Encrypt.

**Масштабирование** — увеличь `clusterSize` и `partitionCount` при росте нагрузки.
