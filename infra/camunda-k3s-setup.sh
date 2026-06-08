#!/usr/bin/env bash
set -euo pipefail

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'
info()  { echo -e "${GREEN}[✓]${NC} $*"; }
step()  { echo -e "\n${YELLOW}━━━ $* ━━━${NC}"; }
error() { echo -e "${RED}[✗] $*${NC}" >&2; exit 1; }

spin() {
    local pid=$1 msg=$2
    local frames=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
    local i=0
    while kill -0 "$pid" 2>/dev/null; do
        printf "\r${YELLOW}[${frames[$i]}]${NC} %s" "$msg"
        i=$(( (i+1) % ${#frames[@]} ))
        sleep 0.1
    done
    printf "\r\033[K"
}

DEPLOY_DIR="$HOME/camunda-deploy"

# ── Passwords ──────────────────────────────────────────────────────────────────
step "Camunda 8 Production Setup"
echo "Passwords: only letters and digits, no special chars (@, \$, \", etc.)"
echo ""

read -rsp "PostgreSQL superuser password: " PG_SUPERUSER_PASS; echo
read -rsp "Camunda app DB password:        " PG_APP_PASS;       echo
read -rsp "Camunda admin username:         " CAMUNDA_ADMIN_USER; echo
read -rsp "Camunda admin password:         " CAMUNDA_ADMIN_PASS; echo

[[ -z "$PG_SUPERUSER_PASS" || -z "$PG_APP_PASS" ]] && error "Passwords cannot be empty"
[[ -z "$CAMUNDA_ADMIN_USER" || -z "$CAMUNDA_ADMIN_PASS" ]] && error "Camunda admin credentials cannot be empty"
[[ "$PG_SUPERUSER_PASS" =~ [@\$\"\'\`\\] ]]         && error "Superuser password has invalid chars"
[[ "$PG_APP_PASS"       =~ [@\$\"\'\`\\] ]]         && error "App password has invalid chars"
[[ "$CAMUNDA_ADMIN_USER" =~ [@\$\"\'\`\\] ]]         && error "Admin username has invalid chars"
[[ "$CAMUNDA_ADMIN_PASS" =~ [@\$\"\'\`\\] ]]         && error "Admin password has invalid chars"

SERVER_IP=$(hostname -I | awk '{print $1}')

# ── System update ──────────────────────────────────────────────────────────────
step "System update"
( sudo apt-get update -qq && sudo apt-get upgrade -y -qq ) &
spin $! "Updating system packages..."
wait $! || error "System update failed"
info "Done"

# ── k3s ───────────────────────────────────────────────────────────────────────
step "k3s"
if command -v k3s &>/dev/null; then
    info "Already installed, skipping"
else
    ( curl -sfL https://get.k3s.io | sh - ) &
    spin $! "Downloading and installing k3s (~70 MB)..."
    wait $! || error "k3s install failed"
    info "Installed"
fi

mkdir -p ~/.kube
sudo cp /etc/rancher/k3s/k3s.yaml ~/.kube/config
sudo chown "$(id -u):$(id -g)" ~/.kube/config
chmod 600 ~/.kube/config
export KUBECONFIG=~/.kube/config
grep -q 'KUBECONFIG' ~/.bashrc || echo 'export KUBECONFIG=~/.kube/config' >> ~/.bashrc

info "Waiting for node Ready..."
until kubectl get nodes 2>/dev/null | grep -qE '\bReady\b|NotReady'; do sleep 2; done
kubectl wait --for=condition=Ready node --all --timeout=120s
info "Node Ready"

# ── Helm ──────────────────────────────────────────────────────────────────────
step "Helm"
if command -v helm &>/dev/null; then
    info "Already installed, skipping"
else
    ( curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash ) >/dev/null 2>&1 &
    spin $! "Downloading and installing Helm..."
    wait $! || error "Helm install failed"
    info "Installed"
fi

# ── CloudNativePG ─────────────────────────────────────────────────────────────
step "CloudNativePG operator"
kubectl apply --server-side \
    -f https://raw.githubusercontent.com/cloudnative-pg/cloudnative-pg/release-1.28/releases/cnpg-1.28.0.yaml
kubectl wait --for=condition=available deployment/cnpg-controller-manager \
    -n cnpg-system --timeout=120s
info "Ready"

# ── Namespace + secrets ────────────────────────────────────────────────────────
step "Namespace and secrets"
kubectl create namespace camunda --dry-run=client -o yaml | kubectl apply -f -

mkdir -p "$DEPLOY_DIR"
cat > "$DEPLOY_DIR/passwords.txt" <<EOF
PG_SUPERUSER_PASS=$PG_SUPERUSER_PASS
PG_APP_PASS=$PG_APP_PASS
CAMUNDA_ADMIN_USER=$CAMUNDA_ADMIN_USER
EOF
chmod 600 "$DEPLOY_DIR/passwords.txt"

kubectl create secret generic camunda-admin-secret \
    --from-literal=username="$CAMUNDA_ADMIN_USER" \
    --from-literal=password="$CAMUNDA_ADMIN_PASS" \
    -n camunda --dry-run=client -o yaml | kubectl apply -f -

kubectl create secret generic pg-superuser-secret \
    --from-literal=username=postgres \
    --from-literal=password="$PG_SUPERUSER_PASS" \
    -n camunda --dry-run=client -o yaml | kubectl apply -f -

kubectl create secret generic pg-camunda-secret \
    --from-literal=username=camundauser \
    --from-literal=password="$PG_APP_PASS" \
    -n camunda --dry-run=client -o yaml | kubectl apply -f -

info "Done"

# ── PostgreSQL cluster ────────────────────────────────────────────────────────
step "PostgreSQL cluster"
cat > "$DEPLOY_DIR/postgresql-cluster.yaml" <<'EOF'
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
EOF

kubectl apply -f "$DEPLOY_DIR/postgresql-cluster.yaml"
info "Waiting for cluster Ready (~2 min)..."
kubectl wait --for=condition=Ready cluster/pg-camunda -n camunda --timeout=180s
info "PostgreSQL Ready"

# ── Helm repos ────────────────────────────────────────────────────────────────
step "Helm repos"
helm repo add camunda   https://helm.camunda.io 2>/dev/null || true
helm repo add portainer https://portainer.github.io/k8s/ 2>/dev/null || true
( helm repo update ) &
spin $! "Updating Helm repos..."
wait $! || error "Helm repo update failed"
info "Done"

# ── Camunda values ────────────────────────────────────────────────────────────
step "Camunda Helm values"
cat > "$DEPLOY_DIR/values-camunda.yaml" <<'EOF'
global:
  identity:
    auth:
      enabled: false

orchestration:
  enabled: true
  replicaCount: 1
  clusterSize: "1"
  partitionCount: "1"
  replicationFactor: "1"
  affinity:
    podAntiAffinity: {}

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
    type: LoadBalancer
    headless:
      type: ClusterIP

  env:
    - name: CAMUNDA_SECURITY_INITIALIZATION_USERS_0_USERNAME
      valueFrom:
        secretKeyRef:
          name: camunda-admin-secret
          key: username
    - name: CAMUNDA_SECURITY_INITIALIZATION_USERS_0_PASSWORD
      valueFrom:
        secretKeyRef:
          name: camunda-admin-secret
          key: password
    - name: CAMUNDA_SECURITY_INITIALIZATION_USERS_0_NAME
      valueFrom:
        secretKeyRef:
          name: camunda-admin-secret
          key: username
    - name: CAMUNDA_SECURITY_INITIALIZATION_USERS_0_EMAIL
      value: admin@localhost

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
  env: []

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
EOF
info "Written to $DEPLOY_DIR/values-camunda.yaml"

# ── Install Camunda ───────────────────────────────────────────────────────────
step "Camunda install (~5-10 min)"
helm install camunda camunda/camunda-platform \
    -f "$DEPLOY_DIR/values-camunda.yaml" \
    -n camunda \
    --timeout 15m

info "Waiting for all pods Running (~5-10 min, JVM is slow to start)..."
kubectl wait --for=condition=Ready pod \
    -l app.kubernetes.io/instance=camunda \
    -n camunda \
    --timeout=600s

# ── Portainer CE (Kubernetes UI) ──────────────────────────────────────────────
step "Portainer CE — Kubernetes UI"
kubectl create namespace portainer --dry-run=client -o yaml | kubectl apply -f -

( helm install portainer portainer/portainer \
    -n portainer \
    --set service.type=LoadBalancer \
    --set service.httpPort=9000 \
    --set service.httpsPort=9443 \
    --timeout 5m ) >"$DEPLOY_DIR/portainer-install.log" 2>&1 &
spin $! "Installing Portainer CE..."
wait $! || error "Portainer install failed (см. $DEPLOY_DIR/portainer-install.log)"

kubectl wait --for=condition=Available deployment/portainer -n portainer --timeout=120s
info "Ready — первый вход: http://${SERVER_IP}:9000  (создай admin-пользователя)"

# ── Done ──────────────────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${GREEN}  Camunda 8 is live!${NC}"
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""
echo "  Operate:  http://${SERVER_IP}:8080/operate"
echo "  Tasklist: http://${SERVER_IP}:8080/tasklist"
echo "  Identity: http://${SERVER_IP}:8080/identity"
echo "  Zeebe:    ${SERVER_IP}:26500"
echo ""
echo "  Login: ${CAMUNDA_ADMIN_USER} / <your password>"
echo ""
echo "  Portainer (K8s UI): http://${SERVER_IP}:9000"
echo "  (при первом входе создай admin — есть ~5 минут)"
echo ""
echo "  Конфиги: $DEPLOY_DIR/"
