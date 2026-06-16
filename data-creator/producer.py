#!/usr/bin/env python3
"""Kafka Avro Producer — CLI с управлением Schema Registry."""

import datetime
import decimal
import json
import os
import random
import re
import string
import sys
import time
import uuid
from pathlib import Path

from generators import all_order_v2 as _gen_all_order_v2

from confluent_kafka import Producer
from confluent_kafka.schema_registry import SchemaRegistryClient
from confluent_kafka.schema_registry.avro import AvroSerializer
from confluent_kafka.serialization import MessageField, SerializationContext
from rich.console import Console
from rich.markup import escape as markup_escape
from rich.panel import Panel
from rich.prompt import Prompt
from rich.table import Table

_KAFKA_SSL    = os.getenv("KAFKA_SSL", "0") == "1"
_PFX_PATH     = os.getenv("KAFKA_PFX_PATH", "")
_PFX_PASSWORD = os.getenv("KAFKA_PFX_PASSWORD", "")

KAFKA_BOOTSTRAP     = os.getenv("KAFKA_BOOTSTRAP", "localhost:9093" if _KAFKA_SSL else "localhost:9092")
SCHEMA_REGISTRY_URL = os.getenv("SCHEMA_REGISTRY_URL", "http://localhost:8082")


def _kafka_ssl_config() -> dict:
    if not _KAFKA_SSL:
        return {}
    if not _PFX_PATH or not _PFX_PASSWORD:
        raise RuntimeError("KAFKA_SSL=1 requires KAFKA_PFX_PATH and KAFKA_PFX_PASSWORD")
    from cryptography.hazmat.primitives.serialization import pkcs12, Encoding, PrivateFormat, NoEncryption
    with open(_PFX_PATH, "rb") as f:
        pfx_data = f.read()
    private_key, cert, ca_certs = pkcs12.load_key_and_certificates(pfx_data, _PFX_PASSWORD.encode())
    key_pem = private_key.private_bytes(Encoding.PEM, PrivateFormat.PKCS8, NoEncryption()).decode()
    cert_pem = cert.public_bytes(Encoding.PEM).decode()
    ca_pem = "".join(c.public_bytes(Encoding.PEM).decode() for c in (ca_certs or []))
    cfg: dict = {
        "security.protocol": "SSL",
        "ssl.key.pem": key_pem,
        "ssl.certificate.pem": cert_pem,
        "ssl.endpoint.identification.algorithm": "none",
    }
    if ca_pem:
        cfg["ssl.ca.pem"] = ca_pem
    return cfg
SCHEMAS_DIR = Path(__file__).parent.parent / "schemas"

console = Console()

BUILTIN_SCHEMAS: dict[str, dict] = {}


# ── schema file loading ───────────────────────────────────────────────────────

def load_file_schemas() -> dict[str, dict]:
    schemas: dict[str, dict] = {}
    if not SCHEMAS_DIR.exists():
        return schemas
    paths = sorted(SCHEMAS_DIR.glob("*.json")) + sorted(SCHEMAS_DIR.glob("*.avsc"))
    for path in paths:
        try:
            data = json.loads(path.read_text())
        except (json.JSONDecodeError, OSError) as e:
            console.print(f"[yellow]⚠ Не удалось загрузить {path.name}: {e}[/]")
            continue
        if "topic" in data and "schema" in data:
            schemas[path.stem] = data
        elif isinstance(data, dict) and data.get("type") == "record":
            schemas[path.stem] = {"topic": path.stem, "schema": data}
        else:
            console.print(f"[yellow]⚠ {path.name}: неизвестный формат (пропущено)[/]")
    return schemas


# ── input helpers ─────────────────────────────────────────────────────────────

def confirm(question: str, default: bool = False) -> bool:
    hint = "[да/нет, Enter=да]" if default else "[да/нет, Enter=нет]"
    while True:
        raw = Prompt.ask(f"  {question} {hint}", default="").strip().lower()
        if raw in ("", ):
            return default
        if raw in ("y", "д", "да", "yes"):
            return True
        if raw in ("n", "н", "нет", "no"):
            return False
        console.print("  [red]Введите да или нет[/]")


def pick(n: int) -> int:
    while True:
        try:
            raw = Prompt.ask(f"  Выбор", default="0")
            v = int(raw)
            if 0 <= v <= n:
                return v
        except (ValueError, KeyboardInterrupt):
            return 0
        console.print(f"  [red]Введите число от 0 до {n}[/]")


def cast(value: str, avro_type) -> object:
    if isinstance(avro_type, list):
        avro_type = next((t for t in avro_type if t != "null"), "string")
    if isinstance(avro_type, dict):
        avro_type = avro_type.get("type", "string")
    match avro_type:
        case "int" | "long":
            return int(value)
        case "float" | "double":
            return float(value)
        case "boolean":
            return value.lower() in ("true", "1", "yes", "y", "да")
        case _:
            return value


def _is_timestamp_field(name: str) -> bool:
    low = name.lower()
    if any(kw in low for kw in ("timestamp", "time_ms", "created_at")):
        return True
    # Match "ts" only as a whole word (camelCase or snake_case boundary)
    words = re.sub(r"([A-Z])", r"_\1", name).lower().split("_")
    return "ts" in words


def _is_complex_avro_type(avro_type) -> bool:
    """Возвращает True для record/array/map и union-ов содержащих эти типы."""
    if isinstance(avro_type, dict):
        return avro_type.get("type") in ("record", "array", "map")
    if isinstance(avro_type, list):
        return any(isinstance(t, dict) and t.get("type") in ("record", "array", "map")
                   for t in avro_type if t != "null")
    return False


def input_record(fields: list[dict], registry: dict | None = None) -> dict:
    if registry is None:
        registry = {}
    record: dict = {}
    for field in fields:
        name: str = field["name"]
        avro_type = field["type"]
        default = field.get("default")

        if default is None and _is_timestamp_field(name):
            default = int(time.time() * 1000)

        # Для вложенных record/array предлагаем авто-генерацию или ввод JSON
        if _is_complex_avro_type(avro_type):
            auto_val = random_value(name, avro_type, registry)
            hint = markup_escape(f"[Enter = авто-генерация | JSON]")
            raw = Prompt.ask(f"  [bold]{markup_escape(name)}[/] [dim]{hint}[/dim]", default="")
            if raw.strip() == "":
                record[name] = auto_val
            else:
                try:
                    record[name] = json.loads(raw)
                except json.JSONDecodeError:
                    console.print("  [yellow]Неверный JSON, использую авто-генерацию[/]")
                    record[name] = auto_val
            continue

        type_hint = avro_type if isinstance(avro_type, str) else json.dumps(avro_type)
        prompt_str = f"  [bold]{markup_escape(name)}[/] [dim]\\[{markup_escape(type_hint)}][/dim]"
        if default is not None:
            prompt_str += f" [dim](Enter = {markup_escape(str(default))})[/dim]"

        while True:
            raw = Prompt.ask(prompt_str, default=str(default) if default is not None else ...)
            if raw is ...:
                console.print(f"  [red]Поле '{markup_escape(name)}' обязательно[/]")
                continue
            try:
                record[name] = cast(raw, avro_type)
                break
            except (ValueError, TypeError):
                console.print(f"  [red]Ожидался тип {markup_escape(type_hint)}, попробуйте ещё раз[/]")
    return record


# ── random fill ──────────────────────────────────────────────────────────────

_FIRST_NAMES = [
    "Александр", "Дмитрий", "Максим", "Сергей", "Андрей", "Алексей", "Артём", "Илья",
    "Кирилл", "Михаил", "Никита", "Роман", "Иван", "Фёдор", "Егор", "Владимир",
    "Анастасия", "Екатерина", "Наталья", "Ольга", "Юлия", "Елена", "Татьяна", "Мария",
    "Ирина", "Светлана", "Дарья", "Полина", "Виктория", "Алина",
]
_LAST_NAMES = [
    "Иванов", "Смирнов", "Кузнецов", "Попов", "Васильев", "Петров", "Соколов",
    "Михайлов", "Новиков", "Фёдоров", "Морозов", "Волков", "Алексеев", "Лебедев",
    "Семёнов", "Егоров", "Павлов", "Козлов", "Степанов", "Николаев",
]
_DOMAINS = ["gmail.com", "mail.ru", "yandex.ru", "outlook.com", "rambler.ru", "bk.ru", "inbox.ru"]
_ORDER_STATUSES = ["СОЗДАН", "В_ОБРАБОТКЕ", "ПОДТВЕРЖДЁН", "ОТПРАВЛЕН", "ДОСТАВЛЕН", "ОТМЕНЁН", "ВОЗВРАТ", "ОЖИДАНИЕ", "ПРИОСТАНОВЛЕН"]
_WORDS = [
    "матрас", "подушка", "кровать", "основание", "изголовье", "наматрасник", "топпер",
    "наволочка", "простыня", "одеяло", "чехол", "каркас", "ламели", "пружины",
    "пенополиуретан", "латекс", "меморифоам", "кокосовое волокно", "холлофайбер",
    "двуспальный", "полуторный", "односпальный", "ортопедический", "анатомический",
    "жёсткий", "средний", "мягкий", "беспружинный", "пружинный", "независимые пружины",
    "бамбук", "хлопок", "микрофибра", "трикотаж", "жаккард",
]

_PRODUCTS = [
    "Матрас Comfort Sleep", "Матрас OrtoFlex Pro", "Матрас Nature Latex",
    "Матрас Memory Foam Elite", "Матрас Spring Classic", "Матрас Kids Soft",
    "Подушка анатомическая", "Подушка Memory Classic", "Подушка бамбуковая",
    "Подушка ортопедическая шейная", "Подушка холлофайбер 70x70",
    "Топпер латексный 5см", "Топпер меморифоам 8см", "Наматрасник защитный",
    "Кровать Scandic 160x200", "Кровать Loft Oak 180x200", "Основание ламельное 140x200",
    "Изголовье мягкое Velvet", "Одеяло всесезонное", "Одеяло летнее бамбук",
]

_CATEGORIES = [
    "Матрасы", "Подушки", "Кровати", "Основания", "Топперы",
    "Наматрасники", "Постельное бельё", "Одеяла", "Изголовья", "Аксессуары",
]

_MATERIALS = [
    "латекс", "меморифоам", "пенополиуретан", "независимые пружины",
    "кокосовое волокно", "холлофайбер", "бамбук", "хлопок", "жаккард",
]

_SIZES = ["80x200", "90x200", "120x200", "140x200", "160x200", "180x200", "200x200"]

_CITIES = [
    "Москва", "Санкт-Петербург", "Екатеринбург", "Новосибирск", "Казань",
    "Нижний Новгород", "Челябинск", "Самара", "Уфа", "Краснодар",
    "Ростов-на-Дону", "Омск", "Красноярск", "Воронеж", "Пермь",
]
_STREETS = [
    "Ленина", "Мира", "Советская", "Пушкина", "Гагарина", "Садовая",
    "Лесная", "Молодёжная", "Центральная", "Победы", "Строителей", "Заводская",
]
_PROMO_PREFIXES = ["SLEEP", "МАТРАС", "COMFORT", "BED", "DREAM", "LATEX", "ORTHO", "FOAM", "RELAX"]


_DATE_ON_SUFFIXES = (
    "createdon", "updatedon", "modifiedon", "orderedon", "ordercreatedon",
    "shippedon", "deliveredon", "completedon", "cancelledon", "processedon",
)


def _is_date_field(name: str) -> bool:
    low = name.lower().replace("_", "")
    return "date" in low or any(low.endswith(s) for s in _DATE_ON_SUFFIXES)


def _is_uuid_field(name: str) -> bool:
    low = name.lower()
    return low.endswith("id") or any(kw in low for kw in ("uuid", "nrec", "fnrec", "number"))


def _random_date_str() -> str:
    days_ago = random.randint(0, 730)
    dt = datetime.datetime.now() - datetime.timedelta(
        days=days_ago,
        hours=random.randint(0, 23),
        minutes=random.randint(0, 59),
        seconds=random.randint(0, 59),
    )
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")


_TRANSLIT_MAP = {
    "а":"a","б":"b","в":"v","г":"g","д":"d","е":"e","ё":"yo","ж":"zh","з":"z",
    "и":"i","й":"j","к":"k","л":"l","м":"m","н":"n","о":"o","п":"p","р":"r",
    "с":"s","т":"t","у":"u","ф":"f","х":"h","ц":"ts","ч":"ch","ш":"sh","щ":"sch",
    "ъ":"","ы":"y","ь":"","э":"e","ю":"yu","я":"ya",
}
_TRANSLIT_MAP.update({k.upper(): v.capitalize() for k, v in _TRANSLIT_MAP.items()})


def _translit(s: str) -> str:
    return "".join(_TRANSLIT_MAP.get(c, c) for c in s).lower().replace(" ", ".")


def _random_phone() -> str:
    return f"+7 9{random.randint(10, 99)} {random.randint(100, 999)}-{random.randint(10, 99)}-{random.randint(10, 99)}"


def _random_address() -> str:
    city = random.choice(_CITIES)
    street = random.choice(_STREETS)
    house = random.randint(1, 150)
    apt = random.randint(1, 300)
    return f"г. {city}, ул. {street}, д. {house}, кв. {apt}"


def _random_ad_login() -> str:
    last = _translit(random.choice(_LAST_NAMES))
    initials = _translit(random.choice(_FIRST_NAMES))[:2]
    return f"{last}.{initials}"


def _random_string(name: str) -> str:
    low = name.lower()
    if any(k in low for k in ("email", "mail")):
        user = f"{_translit(random.choice(_FIRST_NAMES))}.{_translit(random.choice(_LAST_NAMES))}{random.randint(1, 99)}"
        return f"{user}@{random.choice(_DOMAINS)}"
    if any(k in low for k in ("phone", "телефон", "mobile", "cel")):
        return _random_phone()
    if any(k in low for k in ("status", "state")):
        return random.choice(_ORDER_STATUSES)
    if _is_date_field(low):
        return _random_date_str()
    if _is_uuid_field(low):
        return str(uuid.uuid4())
    if low.endswith("ad") or "loginad" in low or "adlogin" in low:
        return _random_ad_login()
    if any(k in low for k in ("address", "addr", "адрес")):
        return _random_address()
    if any(k in low for k in ("person", "contact", "контакт")):
        return f"{random.choice(_FIRST_NAMES)} {random.choice(_LAST_NAMES)}"
    if any(k in low for k in ("name", "title", "fullname", "username")):
        return f"{random.choice(_FIRST_NAMES)} {random.choice(_LAST_NAMES)}"
    if any(k in low for k in ("promocode", "promo", "coupon", "промокод")):
        return f"{random.choice(_PROMO_PREFIXES)}{random.choice([5, 10, 15, 20, 25, 30])}"
    if any(k in low for k in ("cart", "корзина", "basket")):
        return f"CART-{str(uuid.uuid4())[:8].upper()}"
    if any(k in low for k in ("product", "item", "good", "товар", "изделие")):
        return random.choice(_PRODUCTS)
    if any(k in low for k in ("category", "категория", "раздел")):
        return random.choice(_CATEGORIES)
    if any(k in low for k in ("material", "материал", "состав")):
        return random.choice(_MATERIALS)
    if any(k in low for k in ("size", "dimension", "размер")):
        return random.choice(_SIZES)
    if any(k in low for k in ("type", "kind", "тип", "вид")):
        return random.choice(["матрас", "подушка", "кровать", "топпер", "основание"])
    if any(k in low for k in ("payload", "data", "body", "content", "message")):
        return json.dumps({"key": str(uuid.uuid4())[:8], "value": random.randint(1, 100)}, ensure_ascii=False)
    if any(k in low for k in ("description", "desc", "comment", "note", "remark", "описание", "комментарий")):
        return f"{random.choice(_PRODUCTS)}, {random.choice(_MATERIALS)}, {random.choice(_SIZES)}"
    return " ".join(random.choices(_WORDS, k=random.randint(1, 3)))


_PRIMITIVES = {"null", "boolean", "int", "long", "float", "double", "bytes", "string"}


def _collect_named_types(node, registry: dict | None = None) -> dict:
    """Рекурсивно собирает все именованные типы (record, enum) из схемы."""
    if registry is None:
        registry = {}
    if isinstance(node, list):
        for item in node:
            _collect_named_types(item, registry)
    elif isinstance(node, dict):
        t = node.get("type")
        name = node.get("name")
        ns = node.get("namespace", "")
        if t in ("record", "enum") and name:
            registry[name] = node
            if ns:
                registry[f"{ns}.{name}"] = node
        for field in node.get("fields", []):
            _collect_named_types(field.get("type"), registry)
        if "items" in node:
            _collect_named_types(node["items"], registry)
    return registry


def _random_decimal(name: str, precision: int = 18, scale: int = 2) -> decimal.Decimal:
    low = name.lower()
    if any(k in low for k in ("latitude", "широта")):
        val = random.uniform(55.0, 56.5)
    elif any(k in low for k in ("longitude", "долгота")):
        val = random.uniform(37.0, 39.5)
    elif any(k in low for k in ("percent", "процент")):
        val = random.uniform(0.0, 50.0)
    elif any(k in low for k in ("quantity", "qty", "количество")):
        val = random.uniform(1.0, 20.0)
    elif any(k in low for k in ("discount", "скидка")):
        val = random.uniform(100.0, 15_000.0)
    elif any(k in low for k in ("bonus", "бонус")):
        val = random.uniform(0.0, 5_000.0)
    else:
        val = random.uniform(1_000.0, 200_000.0)
    exp = decimal.Decimal(10) ** -scale
    return decimal.Decimal(str(round(val, scale))).quantize(exp, rounding=decimal.ROUND_HALF_UP)


def _union_wrap_key(chosen) -> str | None:
    """Возвращает ключ для fastavro-обёртки только для именованных типов по строковой ссылке.
    Для inline record/enum/fixed — возвращает None: fastavro сам определяет ветку по структуре."""
    if isinstance(chosen, str) and chosen not in _PRIMITIVES:
        return chosen
    return None


def random_value(name: str, avro_type, registry: dict) -> object:
    """Рекурсивно генерирует значение для любого Avro-типа."""
    # Union: берём случайный не-null вариант
    if isinstance(avro_type, list):
        non_null = [t for t in avro_type if t != "null"]
        if not non_null:
            return None
        chosen = random.choice(non_null)
        value = random_value(name, chosen, registry)
        # fastavro требует {FullTypeName: value} для именованных типов в union
        wrap_key = _union_wrap_key(chosen)
        return {wrap_key: value} if wrap_key else value

    # Сложный тип (dict)
    if isinstance(avro_type, dict):
        t = avro_type.get("type")
        logical = avro_type.get("logicalType")
        if t == "record":
            sub_registry = _collect_named_types(avro_type, dict(registry))
            return {f["name"]: random_value(f["name"], f["type"], sub_registry)
                    for f in avro_type.get("fields", [])}
        if t == "array":
            return [random_value(name, avro_type["items"], registry)
                    for _ in range(random.randint(1, 2))]
        if t == "enum":
            return random.choice(avro_type.get("symbols", ["unknown"]))
        if t == "bytes" and logical == "decimal":
            return _random_decimal(name, avro_type.get("precision", 18), avro_type.get("scale", 2))
        if t == "bytes":
            return _random_decimal(name)
        if logical in ("timestamp-millis", "timestamp-micros"):
            now_ms = int(time.time() * 1000)
            offset_ms = random.randint(-730 * 86_400_000, 0)
            val = now_ms + offset_ms
            return val if logical == "timestamp-millis" else val * 1000
        if logical == "date":
            today = int(time.time() // 86_400)
            return today - random.randint(0, 730)
        if logical == "time-millis":
            return random.randint(0, 86_400_000 - 1)
        if logical == "time-micros":
            return random.randint(0, 86_400_000_000 - 1)
        # map и прочие — просто делегируем на примитивный тип
        avro_type = t or "string"

    # Строковый тип
    if isinstance(avro_type, str):
        # Именованный тип — ищем в реестре
        if avro_type not in _PRIMITIVES:
            resolved = registry.get(avro_type)
            if resolved:
                return random_value(name, resolved, registry)
            return {}

        if _is_timestamp_field(name):
            return int(time.time()) if avro_type == "int" else int(time.time() * 1000)

        match avro_type:
            case "int":     return random.randint(1, 10_000)
            case "long":    return random.randint(1, 1_000_000)
            case "float" | "double": return round(random.uniform(0.1, 10_000.0), 2)
            case "boolean": return random.choice([True, False])
            case "bytes":   return _random_decimal(name)
            case "null":    return None
            case _:         return _random_string(name)

    return _random_string(name)


def random_record(fields: list[dict], registry: dict | None = None, schema_name: str | None = None) -> dict:
    if registry is None:
        registry = {}
    if schema_name == "all_order_v2":
        return _gen_all_order_v2.generate()
    return {f["name"]: random_value(f["name"], f["type"], registry) for f in fields}


# ── delivery ──────────────────────────────────────────────────────────────────

def on_delivery(err, msg) -> None:
    if err:
        console.print(f"  [red][!] Ошибка доставки: {err}[/]")
    else:
        console.print(
            f"  [green][✓] Доставлено[/] → "
            f"topic=[cyan]{msg.topic()}[/]  "
            f"partition={msg.partition()}  offset={msg.offset()}"
        )


# ── produce ───────────────────────────────────────────────────────────────────

def produce(schema_def: dict, topic: str, count: int = 1, random_fill: bool = False, schema_name: str | None = None) -> None:
    schema_str = json.dumps(schema_def["schema"])
    fields = schema_def["schema"]["fields"]

    sr = SchemaRegistryClient({"url": SCHEMA_REGISTRY_URL})
    serializer = AvroSerializer(sr, schema_str)
    producer = Producer({"bootstrap.servers": KAFKA_BOOTSTRAP, **_kafka_ssl_config()})

    for i in range(count):
        if count > 1:
            console.rule(f"Сообщение {i + 1}/{count}")

        if random_fill:
            registry = _collect_named_types(schema_def["schema"])
            record = random_record(fields, registry, schema_name=schema_name)
            _print_record(record)
        else:
            try:
                registry = _collect_named_types(schema_def["schema"])
                record = input_record(fields, registry)
            except KeyboardInterrupt:
                console.print("\n  [yellow]Прервано[/]")
                break

        ctx = SerializationContext(topic, MessageField.VALUE)
        producer.produce(topic=topic, value=serializer(record, ctx), on_delivery=on_delivery)
        producer.flush()

    console.print()


def _print_record(record: dict) -> None:
    table = Table(show_lines=False, box=None, padding=(0, 2))
    table.add_column("Поле", style="dim")
    table.add_column("Значение", style="cyan")
    for k, v in record.items():
        table.add_row(k, str(v))
    console.print(table)


def menu_produce() -> None:
    builtin = list(BUILTIN_SCHEMAS.items())
    file_schemas = list(load_file_schemas().items())

    table = Table(show_lines=False, box=None, padding=(0, 2))
    table.add_column("#", style="dim", width=4)
    table.add_column("Источник", style="dim", width=10)
    table.add_column("Имя", style="bold")
    table.add_column("Topic", style="cyan")

    idx = 1
    for name, sd in builtin:
        table.add_row(str(idx), "built-in", name, sd["topic"])
        idx += 1
    for name, sd in file_schemas:
        table.add_row(str(idx), "[green]файл[/]", name, sd["topic"])
        idx += 1
    custom_idx = idx
    table.add_row(str(idx), "—", "Своя схема (JSON)", "—")

    console.print()
    console.print(Panel(table, title="Выберите схему", border_style="cyan"))
    console.print("  [dim]0. Назад[/]\n")
    choice = pick(custom_idx)

    if choice == 0:
        return

    all_schemas = builtin + file_schemas
    if choice < custom_idx:
        chosen_name, schema_def = all_schemas[choice - 1]
        topic = Prompt.ask("  Topic", default=schema_def["topic"])
    else:
        chosen_name = None
        schema_def, topic = _input_custom_schema()
        if schema_def is None:
            return

    raw_count = Prompt.ask("  Количество сообщений", default="1")
    count = int(raw_count) if raw_count.isdigit() and int(raw_count) > 0 else 1

    random_fill = confirm("Заполнить случайными данными?", default=False)
    produce(schema_def, topic, count, random_fill=random_fill, schema_name=chosen_name)


def _input_custom_schema() -> tuple[dict | None, str]:
    console.print("\n  [dim]Вставьте JSON Avro-схему (пустая строка = конец ввода):[/]")
    lines: list[str] = []
    while True:
        try:
            line = input()
        except EOFError:
            break
        if not line:
            break
        lines.append(line)

    try:
        schema_json = json.loads("\n".join(lines))
    except json.JSONDecodeError as e:
        console.print(f"\n  [red]Ошибка парсинга JSON: {e}[/]")
        return None, ""

    topic = Prompt.ask("  Topic")
    if not topic:
        console.print("  [red]Topic не указан[/]")
        return None, ""

    return {"schema": schema_json, "topic": topic}, topic


# ── schema registry ───────────────────────────────────────────────────────────

def get_sr() -> SchemaRegistryClient | None:
    try:
        sr = SchemaRegistryClient({"url": SCHEMA_REGISTRY_URL})
        sr.get_subjects()
        return sr
    except Exception as e:
        console.print(f"\n  [red]Нет подключения к Schema Registry ({SCHEMA_REGISTRY_URL}): {e}[/]\n")
        return None


def _pick_subject(sr: SchemaRegistryClient, title: str = "Выберите subject") -> str | None:
    try:
        subjects = sorted(sr.get_subjects())
    except Exception as e:
        console.print(f"  [red]{e}[/]")
        return None

    if not subjects:
        console.print("  [dim]Нет зарегистрированных subjects[/]")
        return None

    table = Table(show_lines=False, box=None, padding=(0, 2))
    table.add_column("#", style="dim", width=4)
    table.add_column("Subject", style="bold")
    for i, s in enumerate(subjects, 1):
        table.add_row(str(i), s)

    console.print(Panel(table, title=title, border_style="cyan"))
    console.print("  [dim]0. Назад[/]\n")
    choice = pick(len(subjects))
    if choice == 0:
        return None
    return subjects[choice - 1]


def _registry_list(sr: SchemaRegistryClient) -> None:
    try:
        subjects = sorted(sr.get_subjects())
    except Exception as e:
        console.print(f"  [red]{e}[/]")
        return

    if not subjects:
        console.print("\n  [dim]Нет зарегистрированных subjects[/]\n")
        return

    table = Table(show_lines=True)
    table.add_column("#", style="dim", width=4)
    table.add_column("Subject", style="bold")
    table.add_column("Версии", style="cyan")

    for i, subject in enumerate(subjects, 1):
        try:
            versions = sr.get_versions(subject)
            versions_str = ", ".join(str(v) for v in sorted(versions))
        except Exception:
            versions_str = "[dim]—[/]"
        table.add_row(str(i), subject, versions_str)

    console.print()
    console.print(Panel(table, title=f"Schema Registry — {len(subjects)} subjects"))
    console.print()


def _registry_view(sr: SchemaRegistryClient) -> None:
    console.print()
    subject = _pick_subject(sr, "Просмотр схемы")
    if not subject:
        return

    try:
        versions = sorted(sr.get_versions(subject))
    except Exception as e:
        console.print(f"  [red]{e}[/]")
        return

    console.print(f"\n  Доступные версии для [cyan]{subject}[/]: [bold]{', '.join(str(v) for v in versions)}[/]")
    ver_input = Prompt.ask("  Версия", default="latest")

    try:
        if ver_input == "latest":
            reg = sr.get_latest_version(subject)
        else:
            reg = sr.get_version(subject, int(ver_input))
    except Exception as e:
        console.print(f"  [red]{e}[/]")
        return

    parsed = json.loads(reg.schema.schema_str)
    console.print(Panel(
        json.dumps(parsed, indent=2, ensure_ascii=False),
        title=f"[bold]{subject}[/]  [dim]v{reg.version}  ID={reg.schema_id}[/]",
        border_style="cyan",
    ))
    console.print()


def _registry_delete_subject(sr: SchemaRegistryClient) -> None:
    console.print()
    subject = _pick_subject(sr, "Удалить subject")
    if not subject:
        return

    try:
        versions = sorted(sr.get_versions(subject))
    except Exception:
        versions = []

    console.print(f"\n  Subject: [bold red]{subject}[/]  версии: {', '.join(str(v) for v in versions)}")
    permanent = confirm("Удалить навсегда (permanent delete)?", default=False)

    if not confirm(f"Подтвердить удаление subject [bold red]{subject}[/]?", default=False):
        console.print("  [dim]Отменено[/]\n")
        return

    try:
        deleted = sr.delete_subject(subject)
        if permanent:
            sr.delete_subject(subject, permanent=True)
            console.print(f"  [green]✓ Удалено навсегда {len(deleted)} версий subject '{subject}'[/]\n")
        else:
            console.print(f"  [green]✓ Soft-удалено {len(deleted)} версий subject '{subject}'[/]\n")
    except Exception as e:
        console.print(f"  [red]Ошибка: {e}[/]\n")


def _registry_delete_version(sr: SchemaRegistryClient) -> None:
    console.print()
    subject = _pick_subject(sr, "Удалить версию")
    if not subject:
        return

    try:
        versions = sorted(sr.get_versions(subject))
    except Exception as e:
        console.print(f"  [red]{e}[/]")
        return

    console.print(f"\n  Subject: [cyan]{subject}[/]  версии: [bold]{', '.join(str(v) for v in versions)}[/]")
    ver_input = Prompt.ask("  Версия для удаления")

    try:
        version = int(ver_input)
    except ValueError:
        console.print("  [red]Неверная версия[/]\n")
        return

    permanent = confirm("Удалить навсегда (permanent delete)?", default=False)

    if not confirm(f"Удалить версию [bold red]{version}[/] у [bold red]{subject}[/]?", default=False):
        console.print("  [dim]Отменено[/]\n")
        return

    try:
        sr.delete_version(subject, version)
        if permanent:
            sr.delete_version(subject, version, permanent=True)
            console.print(f"  [green]✓ Версия {version} удалена навсегда[/]\n")
        else:
            console.print(f"  [green]✓ Версия {version} soft-удалена[/]\n")
    except Exception as e:
        console.print(f"  [red]Ошибка: {e}[/]\n")


def menu_registry() -> None:
    sr = get_sr()
    if sr is None:
        return

    while True:
        console.print(Panel(
            "  [cyan]1[/]. Список subjects\n"
            "  [cyan]2[/]. Просмотр схемы\n"
            "  [cyan]3[/]. Удалить subject\n"
            "  [cyan]4[/]. Удалить версию\n\n"
            "  [dim]0. Назад[/]",
            title="Schema Registry",
            border_style="cyan",
        ))
        choice = pick(4)

        if choice == 0:
            return
        elif choice == 1:
            _registry_list(sr)
        elif choice == 2:
            _registry_view(sr)
        elif choice == 3:
            _registry_delete_subject(sr)
        elif choice == 4:
            _registry_delete_version(sr)


# ── main ──────────────────────────────────────────────────────────────────────

def main() -> None:
    file_count = len(load_file_schemas())
    file_hint = f"  Схем из файлов : [green]{file_count}[/] [dim]({SCHEMAS_DIR}/)[/]" if file_count else \
                f"  Схем из файлов : [dim]0 (положите .json/.avsc в {SCHEMAS_DIR}/)[/]"

    console.print(Panel.fit(
        f"[bold]Kafka Avro Producer[/]\n\n"
        f"  Kafka           : [cyan]{KAFKA_BOOTSTRAP}[/]\n"
        f"  Schema Registry : [cyan]{SCHEMA_REGISTRY_URL}[/]\n"
        f"{file_hint}",
        border_style="bold",
    ))

    while True:
        console.print(Panel(
            "  [cyan]1[/]. Отправить сообщение\n"
            "  [cyan]2[/]. Управление Schema Registry\n\n"
            "  [dim]0. Выход[/]",
            title="Главное меню",
        ))
        choice = pick(2)

        if choice == 0:
            console.print("\n  [dim]Выход.[/]\n")
            sys.exit(0)
        elif choice == 1:
            try:
                menu_produce()
            except KeyboardInterrupt:
                console.print("\n  [yellow]Отменено[/]\n")
        elif choice == 2:
            try:
                menu_registry()
            except KeyboardInterrupt:
                console.print("\n  [yellow]Отменено[/]\n")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        console.print("\n\n  [dim]Выход.[/]\n")
