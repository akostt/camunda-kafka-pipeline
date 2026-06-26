# Camunda 8 — Руководство по развёртыванию
## k3s + Helm + Elasticsearch + Identity/Keycloak + Kafka SSL

---

## Системные требования

### Минимум для тестов (одна нода)

| Компонент | RAM | Примечание |
|---|---|---|
| Elasticsearch | 2 GB | JVM heap 1 GB |
| Zeebe + Operate + Tasklist | 1.5 GB | Один JVM-процесс |
| Identity | 512 MB | Управление пользователями |
| Keycloak | 1 GB | OIDC-провайдер |
| PostgreSQL (для Keycloak) | 256 MB | |
| Connectors | 512 MB | |
| k3s + ОС | 512 MB | |
| **Итого** | **~6.3 GB** | **Рекомендуется 8 GB** |

**Диск:** минимум 40 GB SSD.  
**CPU:** 2 vCPU минимум, 4 vCPU комфортно.  
**ОС:** Ubuntu 22.04 LTS.

### Производственная нагрузка

| Активных экземпляров | RAM | CPU | Диск ES |
|---|---|---|---|
| до 1 000 | 8 GB / 4 vCPU | умеренная | 15 GB |
| 1 000 – 10 000 | 16 GB / 8 vCPU | средняя | 50 GB |
| 10 000+ | 32 GB+ / 16+ vCPU | высокая | 100 GB+ |

---

## Архитектура

```
Browser
   │
   ▼ port 80
┌──────────────────────────────────────────────────────────────────┐
│  k3s: Traefik Ingress  ← <IP>.nip.io (бесплатный wildcard DNS)  │
│     /          → camunda-zeebe-gateway:8080 (Operate/Tasklist)  │
│     /auth/     → camunda-keycloak:80 (Keycloak login)           │
│     /identity  → camunda-identity:80 (Identity UI)              │
│                                                                  │
│  ┌──────────────────────┐  ┌─────────────────────────────────┐  │
│  │  Orchestration Pod   │  │  Identity Pod + Keycloak        │  │
│  │  Zeebe + Operate +   │  │  + PostgreSQL                   │  │
│  │  Tasklist            │  │                                 │  │
│  └──────────┬───────────┘  └─────────────────────────────────┘  │
│             │ Elasticsearch                                       │
│  ┌──────────▼───────────┐                                        │
│  │  Elasticsearch       │                                        │
│  └──────────────────────┘                                        │
└──────────────────────────────────────────────────────────────────┘
│
│  Docker: Kafka (SSL :9093) + Schema Registry (:8081)
```

**Доступ после установки** (всё через порт 80 Traefik):

| Сервис | URL |
|---|---|
| Operate | `http://<IP>.nip.io/operate` |
| Tasklist | `http://<IP>.nip.io/tasklist` |
| Identity | `http://<IP>.nip.io/identity` |
| Keycloak admin | `http://<IP>.nip.io/auth` |
| Schema Registry | `http://<IP>:8081` |
| Kafka SSL | `<IP>:9093` |
| Kafka plaintext | `<IP>:9092` |

---

## Быстрая установка

```bash
chmod +x camunda-k3s-setup.sh
./camunda-k3s-setup.sh
# Или без интерактивного ввода:
KAFKA_SSL_PASS=KafkaPass123 ./camunda-k3s-setup.sh
```

Скрипт выполняется ~20–30 минут. Логин: `demo` / `demo`.

---

## Пошаговая установка

### Шаг 1 — Подготовка сервера

```bash
sudo apt-get update && sudo apt-get upgrade -y
sudo apt-get install -y default-jre-headless python3 python3-yaml
```

### Шаг 2 — k3s (включает Traefik Ingress)

```bash
curl -sfL https://get.k3s.io | sh -
mkdir -p ~/.kube
sudo cp /etc/rancher/k3s/k3s.yaml ~/.kube/config
sudo chown $(id -u):$(id -g) ~/.kube/config
chmod 600 ~/.kube/config
echo 'export KUBECONFIG=~/.kube/config' >> ~/.bashrc
export KUBECONFIG=~/.kube/config
kubectl get nodes
```

k3s автоматически устанавливает **Traefik Ingress** на порту 80.

### Шаг 3 — Helm и Docker

```bash
curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
curl -fsSL https://get.docker.com | sh -
sudo usermod -aG docker $USER && sudo chmod 666 /var/run/docker.sock
```

### Шаг 4 — Helm values

```yaml
global:
  # Traefik Ingress на порту 80. nip.io — бесплатный wildcard DNS,
  # <IP>.nip.io автоматически резолвится в <IP> без настройки DNS.
  ingress:
    enabled: true
    className: traefik
    host: "<IP>.nip.io"
    tls:
      enabled: false

  identity:
    auth:
      enabled: true
      # Публичный URL Keycloak — браузер редиректится сюда при логине.
      publicIssuerUrl: "http://<IP>.nip.io/auth/realms/camunda-platform"
      # Внутренний URL — для сервисов внутри кластера.
      issuerBackendUrl: "http://camunda-keycloak/auth/realms/camunda-platform"
      orchestration:
        redirectUrl: "http://<IP>.nip.io/sso-callback"

  # ОБЯЗАТЕЛЬНО: без этого Zeebe стартует в Basic auth, Connectors не получают JWT.
  security:
    authentication:
      method: oidc

orchestration:
  service:
    type: ClusterIP   # Traefik маршрутизирует снаружи, ClusterIP достаточно

  # gRPC Ingress для внешних job workers
  ingress:
    grpc:
      enabled: true
      className: traefik
      host: "grpc.<IP>.nip.io"

  security:
    authentication:
      oidc:
        secret:
          inlineSecret: "<OIDC_SECRET>"

connectors:
  security:
    authentication:
      oidc:
        secret:
          inlineSecret: "<OIDC_SECRET>"
  env:
    # Helm chart устанавливает AUTH_TYPE=KEYCLOAK, но не инжектирует CLIENT_ID/SECRET.
    - name: CAMUNDA_CLIENT_ID
      value: "connectors"
    - name: CAMUNDA_CLIENT_SECRET
      value: "<OIDC_SECRET>"
    - name: CAMUNDA_AUTH_SERVER_URL
      value: "http://camunda-keycloak/auth/realms/camunda-platform"
    - name: ZEEBE_CLIENT_SECURITY_PLAINTEXT
      value: "true"

identity:
  firstUser:
    username: demo
    secret:
      inlineSecret: "demo"

identityKeycloak:
  enabled: true
  readinessProbe:
    httpGet:
      path: /auth/realms/master
      port: 8080    # Keycloak слушает на 8080; дефолтный probe шёл на неверный путь
```

### Шаг 5 — Установка

```bash
helm repo add camunda https://helm.camunda.io && helm repo update
helm install camunda camunda/camunda-platform \
  -f ~/camunda-deploy/values-camunda.yaml \
  -n camunda --timeout 25m
kubectl wait --for=condition=Ready pod -l app.kubernetes.io/instance=camunda \
  -n camunda --timeout=1200s
```

### Шаг 6 — Post-install: патч redirect URLs

Helm chart прописывает `localhost:8080` в двух местах: Zeebe ConfigMap и Keycloak client. Без патча после логина браузер будет редиректиться на `localhost`.

```bash
DOMAIN="<IP>.nip.io"
KC_IP=$(kubectl get svc camunda-keycloak -n camunda -o jsonpath="{.spec.clusterIP}")

# 1. Патч Zeebe ConfigMap
kubectl get configmap camunda-zeebe-configuration -n camunda -o yaml \
  | sed "s|redirect-uri: \"http://localhost:8080/sso-callback\"|redirect-uri: \"http://${DOMAIN}/sso-callback\"|g" \
  | kubectl apply -f -

# 2. Патч Keycloak client redirect URIs
TOKEN=$(curl -s -X POST "http://${KC_IP}/auth/realms/master/protocol/openid-connect/token" \
  -d "client_id=admin-cli&username=admin&password=<KC_ADMIN_PASS>&grant_type=password" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['access_token'])")

CLIENT_ID=$(curl -s -H "Authorization: Bearer $TOKEN" \
  "http://${KC_IP}/auth/admin/realms/camunda-platform/clients?clientId=orchestration" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)[0]['id'])")

curl -s -X PUT \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "http://${KC_IP}/auth/admin/realms/camunda-platform/clients/${CLIENT_ID}" \
  -d "{\"redirectUris\":[\"http://${DOMAIN}/sso-callback\",\"http://${DOMAIN}/*\"],\"webOrigins\":[\"http://${DOMAIN}\"]}"

# 3. Патч Ingress (chart не добавляет путь / для Operate/Tasklist)
kubectl patch ingress camunda-camunda-platform-http -n camunda --type=json -p '[
  {"op":"add","path":"/spec/rules/0/http/paths/-","value":{"path":"/","pathType":"Prefix","backend":{"service":{"name":"camunda-zeebe-gateway","port":{"number":8080}}}}},
  {"op":"remove","path":"/metadata/annotations/ingress.kubernetes.io~1rewrite-target"}
]'

# 4. Перезапуск Zeebe
kubectl rollout restart statefulset/camunda-zeebe -n camunda
kubectl rollout status statefulset/camunda-zeebe -n camunda --timeout=300s
```

> **Всё это делается автоматически скриптом `camunda-k3s-setup.sh`.**

---

## Connector Secrets для BPMN

```bash
kubectl create secret generic my-secret --from-literal=MY_KEY="value" -n camunda
# Добавить в connectors.env в values:
# - name: MY_KEY
#   valueFrom:
#     secretKeyRef:
#       name: my-secret
#       key: MY_KEY
helm upgrade camunda camunda/camunda-platform -f ~/camunda-deploy/values-camunda.yaml -n camunda
```

В BPMN: `{{secrets.MY_KEY}}`

### Kafka SSL в BPMN (additionalProperties)

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

---

## Полезные команды

```bash
kubectl get pods,svc,ingress -n camunda
kubectl logs -n camunda camunda-zeebe-0 -f
kubectl logs -n camunda deployment/camunda-connectors -f
kubectl rollout restart deployment/camunda-connectors -n camunda
helm upgrade camunda camunda/camunda-platform -f ~/camunda-deploy/values-camunda.yaml -n camunda
docker exec kafka kafka-topics --bootstrap-server localhost:9092 --list
```

---

## Устранение неполадок

**Браузер редиректит на `localhost:18080` или `localhost:8080`:**  
Не выполнен post-install патч. Запустить Шаг 6.

**404 при открытии `/operate`:**  
Ingress не содержит путь `/`. Выполнить патч из Шага 6.

**Zeebe в Basic auth вместо OIDC:**
```bash
kubectl get configmap camunda-zeebe-configuration -n camunda \
  -o jsonpath="{.data.application\.yaml}" | grep "method:"
```
Если `method: basic` — добавить `global.security.authentication.method: oidc` в values.

**Connectors — `UNAUTHENTICATED`:**  
Добавить `CAMUNDA_CLIENT_ID`, `CAMUNDA_CLIENT_SECRET`, `CAMUNDA_AUTH_SERVER_URL` в `connectors.env`.

**Zeebe падает при старте:**
```
Creation of initial users is not supported with OIDC authentication method
```
Убрать `CAMUNDA_SECURITY_INITIALIZATION_USERS_*` из `orchestration.env`. При OIDC пользователи создаются через `identity.firstUser`.

**Keycloak застрял в `0/1`:**  
Добавить в values:
```yaml
identityKeycloak:
  readinessProbe:
    httpGet:
      path: /auth/realms/master
      port: 8080
```

**Полная переустановка:**
```bash
helm uninstall camunda -n camunda
kubectl delete pvc --all -n camunda
docker compose -f ~/camunda-deploy/kafka/docker-compose.yml down -v
./camunda-k3s-setup.sh
```

---

## Структура файлов

```
~/camunda-deploy/
├── passwords.txt        # Все пароли — НЕ КОММИТИТЬ
├── values-camunda.yaml  # Helm values
├── kafka/
│   └── docker-compose.yml
└── kafka-ssl/
    ├── ca.crt / ca.key
    ├── kafka.broker.keystore.p12
    ├── kafka.broker.truststore.p12
    ├── client-full.pfx       # Для BPMN-коннекторов
    └── client.truststore.p12
```
