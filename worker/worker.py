#!/usr/bin/env python3
"""Zeebe job worker — publishes Avro-encoded orders to Kafka."""

import asyncio
import io
import json
import logging
import signal
import struct
import urllib.request
from decimal import Decimal
from typing import Any

import fastavro
from aiokafka import AIOKafkaProducer
from pyzeebe import ZeebeWorker, create_insecure_channel

ZEEBE_ADDRESS = "localhost:26500"
BOOTSTRAP_SERVERS = "localhost:9092"
TOPIC_NAME = "all_orders"
SCHEMA_REGISTRY_URL = "http://localhost:8082"
SCHEMA_SUBJECT = f"{TOPIC_NAME}-value"

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger(__name__)


def _fetch_schema() -> tuple[int, dict, Any]:
    with urllib.request.urlopen(f"{SCHEMA_REGISTRY_URL}/subjects/{SCHEMA_SUBJECT}/versions/latest") as r:
        data = json.loads(r.read())
    s = data["schema"]
    return data["id"], json.loads(s), fastavro.parse_schema(json.loads(s))


def _coerce(value: Any, schema: Any) -> Any:
    """Coerce Python values to Avro types — handles decimal and enum, which Camunda connectors can't produce."""
    if isinstance(schema, list):
        if value is None:
            return None
        for s in schema:
            if s != "null":
                return _coerce(value, s)
        return value

    if not isinstance(schema, dict):
        return value

    kind, logical = schema.get("type"), schema.get("logicalType")

    if logical == "decimal":
        if value is None:
            return None
        d = value if isinstance(value, Decimal) else Decimal(str(value))
        return d.quantize(Decimal(10) ** -schema.get("scale", 0))

    if kind == "enum":
        return str(value) if value is not None else schema.get("default")

    if kind == "record":
        if value is None:
            return None
        out = {}
        for f in schema.get("fields", []):
            v = value.get(f["name"]) if isinstance(value, dict) else None
            out[f["name"]] = _coerce(v if v is not None else f.get("default"), f["type"])
        return out

    if kind == "array":
        return [_coerce(item, schema["items"]) for item in (value or [])]

    if kind == "map":
        return {k: _coerce(v, schema["values"]) for k, v in (value or {}).items()}

    return value


def _serialize(record: dict, schema_id: int, parsed: Any) -> bytes:
    buf = io.BytesIO()
    buf.write(b"\x00" + struct.pack(">I", schema_id))
    fastavro.schemaless_writer(buf, parsed, record)
    return buf.getvalue()


async def main() -> None:
    schema_id, raw_schema, parsed_schema = _fetch_schema()
    log.info("Schema loaded id=%d", schema_id)

    producer = AIOKafkaProducer(
        bootstrap_servers=BOOTSTRAP_SERVERS,
        acks=1,
        compression_type="gzip",
        linger_ms=5,
        max_batch_size=65_536,
        max_request_size=5_242_880,
        enable_idempotence=False,
    )
    await producer.start()
    log.info("Kafka producer started")

    channel = create_insecure_channel(grpc_address=ZEEBE_ADDRESS)
    worker = ZeebeWorker(channel)

    @worker.task(
        task_type="write-order-to-kafka",
        timeout_ms=10_000,
        max_jobs_to_activate=32,
        variables_to_fetch=["orderPayload", "newStatusId"],
    )
    async def write_order(orderPayload: dict, newStatusId: str, **_: Any) -> dict:
        msg_obj = {**orderPayload["messageObject"], "statusId": newStatusId, "stateName": "Updated"}
        record = {**orderPayload, "messageObject": msg_obj}
        await producer.send(TOPIC_NAME, value=_serialize(_coerce(record, raw_schema), schema_id, parsed_schema))
        return {}

    loop = asyncio.get_running_loop()
    stop = loop.create_future()
    loop.add_signal_handler(signal.SIGINT, stop.set_result, None)
    loop.add_signal_handler(signal.SIGTERM, stop.set_result, None)

    log.info("Worker started, listening for write-order-to-kafka jobs...")
    worker_task = asyncio.create_task(worker.work())

    await stop
    log.info("Shutting down...")
    await worker.stop()
    worker_task.cancel()
    await producer.stop()
    try:
        await worker_task
    except asyncio.CancelledError:
        pass
    log.info("Worker stopped.")


if __name__ == "__main__":
    asyncio.run(main())
