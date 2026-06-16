#!/usr/bin/env bash
# Генерация self-signed сертификатов для локальной Kafka SSL/mTLS.
# Результат: kafka/kafka-ssl/ — файлы для брокера и client-full.pfx для клиентов.
set -euo pipefail

PASSWORD="${KAFKA_PFX_PASSWORD:-KafkaSsl2024}"
DIR="$(cd "$(dirname "$0")" && pwd)/kafka-ssl"
DAYS=3650

mkdir -p "$DIR"
cd "$DIR"

echo "Generating certificates in $DIR ..."

# ── CA ────────────────────────────────────────────────────────────────────────
openssl req -new -x509 -keyout ca.key -out ca.crt -days "$DAYS" -nodes \
  -subj "/CN=LocalKafkaCA/O=Local/C=RU" 2>/dev/null
echo "  [ok] CA"

# ── Broker ────────────────────────────────────────────────────────────────────
openssl req -new -keyout broker.key -out broker.csr -nodes \
  -subj "/CN=localhost/O=Local/C=RU" 2>/dev/null
openssl x509 -req -CA ca.crt -CAkey ca.key -in broker.csr -out broker.crt \
  -days "$DAYS" -CAcreateserial 2>/dev/null

openssl pkcs12 -export \
  -in broker.crt -inkey broker.key -certfile ca.crt \
  -name broker -passout "pass:$PASSWORD" \
  -out kafka.broker.keystore.p12
echo "  [ok] broker keystore"

rm -f kafka.broker.truststore.p12
keytool -import -trustcacerts -alias CARoot \
  -file ca.crt \
  -keystore kafka.broker.truststore.p12 \
  -storetype PKCS12 \
  -storepass "$PASSWORD" \
  -noprompt
echo "  [ok] broker truststore"

printf '%s' "$PASSWORD" > keystore_creds
printf '%s' "$PASSWORD" > truststore_creds

# ── Client ────────────────────────────────────────────────────────────────────
openssl req -new -keyout client.key -out client.csr -nodes \
  -subj "/CN=camunda-client/O=Local/C=RU" 2>/dev/null
openssl x509 -req -CA ca.crt -CAkey ca.key -in client.csr -out client.crt \
  -days "$DAYS" -CAcreateserial 2>/dev/null

openssl pkcs12 -export \
  -in client.crt -inkey client.key -certfile ca.crt \
  -name client -passout "pass:$PASSWORD" \
  -out client-full.pfx
echo "  [ok] client-full.pfx"

echo ""
echo "Done. Password: $PASSWORD"
echo "PFX for Camunda/worker/data-creator: $DIR/client-full.pfx"
