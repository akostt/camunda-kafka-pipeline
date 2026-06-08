"""Generator for all_order_v3 schema (OrderMessage) — domain-aware random data."""

import decimal
import random
import time
import uuid
from typing import Any


# ── reference data ────────────────────────────────────────────────────────────

_NREC_POOL = [
    "800B000000000014", "8001000000000A94", "80010000002BE26D",
    "8006000000000061", "8006000000000062", "8006000000000093",
    "80010000000008F9", "800100000009FFD7", "8001000000008959",
    "80010000000A003B", "800100000009FA15", "800100000009F8A0",
    "8001000000000601", "83E70000000000C7", "8001000000000014",
]

_PAYMENT_TYPES = _NREC_POOL[:5]
_SHOP_IDS = [
    "800B000000000001", "800B000000000002", "800B000000000003",
    "800B000000000004", "800B000000000005", "800B000000000006",
]
_CATEGORY_IDS = ["800B000000000014"] + _NREC_POOL[2:6]
_SOURCE_IDS = _NREC_POOL[:4]
_GALAKTIKA_STATUSES = {
    "Оформляемый":                "C0003089DD24DC9A",
    "Исполняемый":                "C0008686474408E5",
    "Внимание":                   "80010000000020AE",
    "Нет ткани":                  "80010000000020A4",
    "Ожидание":                   "8001000000001523",
    "Жду оплату":                 "8001000000001564",
    "Региональный склад":         "80010000000020B3",
    "Обрабатывается Ковров":      "8001000000001500",
    "Обрабатывается Новосибирск": "8001000000001501",
    "Ковров планирование":        "800100000000011F",
    "Новосибирск планирование":   "8001000000000120",
    "Поставка":                   "8001000000000121",
    "Центральный склад":          "80010000000020A9",
    "Перемещение":                "80010000000034F4",
    "РС":                         "80010000000034F5",
    "В пути":                     "800100000000195B",
    "Транзит":                    "8001000000003E0E",
    "Перемещение на ТТ":          "80010000000020B4",
    "Торговая точка":             "80010000000020B5",
    "Готов к отгрузке":           "8001000000000122",
    "Смежный склад":              "80010000000052A0",
    "Отгрузка завершена":         "8001000000000154",
    "Выполнен":                   "8001000000000118",
    "Отменен":                    "8010000000000108",
}
_STATUS_IDS = list(_GALAKTIKA_STATUSES.values())
_FILIAL_IDS = _NREC_POOL[:4]

_DELIVERY_METHOD_IDS = _NREC_POOL[:5]
_CITY_IDS = _NREC_POOL[:8]
_SUBDIVISION_IDS = _NREC_POOL[:4]
_REJECTION_REASON_IDS = _NREC_POOL[:3]
_INTERVAL_IDS = _NREC_POOL[:4]
_STATUS_HISTORY_IDS = _NREC_POOL[:6]
_REASON_IDS = _NREC_POOL[:4]
_PRICE_LIST_IDS = _NREC_POOL[:5]
_MOL_IDS = _NREC_POOL[:4]

_BLOCK_TYPES = ["Корпус", "Строение", "Владение", "Другое", None]
_APARTMENT_TYPES = ["Квартира", "Офис", "Помещение", "Другое", None]

_HARDLINK_POOL = [
    "800100000014E1BD", "800100000014E1C0", "800100000014E1BC",
    "80010000001028F1", "80010000001028F4", "80010000001028F5",
]

_STREET_KLADR = [
    "7700000000000", "7800000000000", "6600000000000",
    "5400000000000", "1600000000000", "5200000000000",
]

_DESCRIPTOR_GROUPS = ["A001", "B002", "C003", "D004", "E005"]
_CURRENCY_CODES = ["RUB", "USD", "EUR", "KZT", "BYN"]

_PROMO_TYPES = ["discount", "spentBonusPoints", "gift", None]
_PROMO_SOURCES = ["frontend", "crm", "erp", "mobile", None]

_DELIVERY_MODES = ["courier", "pvz", "pickup", None]


# ── helpers ───────────────────────────────────────────────────────────────────

def _uuid() -> str:
    return str(uuid.uuid4())


def _nrec() -> str:
    return random.choice(_NREC_POOL)


def _ts_now() -> int:
    now_ms = int(time.time() * 1000)
    return now_ms - random.randint(0, 365 * 86_400_000)


def _day_offset(base_day: int, lo: int, hi: int) -> int:
    return base_day + random.randint(lo, hi)


def _maybe(value, null_prob: float = 0.5):
    return None if random.random() < null_prob else value


def _time_millis() -> int:
    return random.randint(8 * 3_600_000, 22 * 3_600_000)


def _time_interval() -> tuple[int, int]:
    begin = _time_millis()
    return begin, begin + random.randint(3_600_000, 7_200_000)


def _decimal(lo: float, hi: float, scale: int = 2) -> decimal.Decimal:
    exp = decimal.Decimal(10) ** -scale
    val = round(random.uniform(lo, hi), scale)
    return decimal.Decimal(str(val)).quantize(exp, rounding=decimal.ROUND_HALF_UP)


def _order_number() -> str:
    prefix = random.choice(["ДО", "ЗК", "КЗ", "ERP"])
    return f"{prefix}-{random.randint(100_000, 9_999_999)}"


def _coupon_number() -> str:
    return "".join(random.choices("ABCDEFGHJKLMNPQRSTUVWXYZ0123456789", k=12))


def _planned_time(planned_date: int | None) -> tuple[int | None, int | None]:
    if planned_date is None:
        return None, None
    begin, end = random.choice([(None, None), _time_interval()])
    return begin, end


# ── sub-record generators ─────────────────────────────────────────────────────

def _gen_metadata(created_ts: int) -> dict:
    return {
        "integrationTimestamp": created_ts,
        "correlationId": _maybe(_uuid(), null_prob=0.2),
    }


def _gen_organization() -> dict:
    return {
        "clientId": _uuid(),
        "shipperId": _maybe(_uuid()),
        "agentId": _maybe(_uuid()),
        "receiverId": _maybe(_uuid()),
    }


def _gen_planning(created_day: int) -> dict | None:
    if random.random() < 0.3:
        return None

    planned = _maybe(_day_offset(created_day, 7, 60), null_prob=0.2)

    if planned is not None:
        max_off = max(1, planned - created_day - 1)
        mfg = _maybe(_day_offset(created_day, 1, min(max_off, 45)), null_prob=0.3)
    else:
        mfg = _maybe(_day_offset(created_day, 3, 30), null_prob=0.3)

    return {
        "plannedDate": planned,
        "protocolId": _maybe(_uuid()),
        "manufactureDate": mfg,
    }


def _gen_delivery(mode: str, created_day: int) -> dict:
    if mode == "courier":
        assembly_off = random.randint(2, 10)
        assembly = _maybe(_day_offset(created_day, assembly_off, assembly_off), null_prob=0.3)

        delivery_lo = (assembly_off + 1) if assembly is not None else 3
        delivery_hi = delivery_lo + random.randint(1, 7)
        planned = _maybe(_day_offset(created_day, delivery_lo, delivery_hi), null_prob=0.2)

        if planned is not None:
            call_off = max(0, planned - created_day - random.randint(0, 3))
            call = _maybe(_day_offset(created_day, call_off, call_off), null_prob=0.4)
        else:
            call = None

        begin, end = _planned_time(planned)
        track = _maybe(f"SDEK{random.randint(10_000_000, 99_999_999)}", null_prob=0.4)
        return {
            "plannedDate": planned,
            "plannedTimeBegin": begin,
            "plannedTimeEnd": end,
            "comment": _maybe(f"Позвонить за {random.randint(1, 3)} часа"),
            "pvzCode": None,
            "courierTrackNumber": track,
            "courierTrackLink": (
                f"https://www.cdek.ru/ru/tracking/?order_id={track}" if track else None
            ),
            "courierDocumentLink": _maybe(
                f"https://cdn.example.com/docs/{_uuid()}.pdf", null_prob=0.7
            ),
            "cityId": _maybe(random.choice(_CITY_IDS)),
            "landingLink": None,
            "pickupDate": None,
            "assemblyDate": assembly,
            "callDate": call,
            "postIndex": _maybe(f"{random.randint(100_000, 999_999)}"),
        }

    elif mode == "pvz":
        pvz_off = random.randint(3, 14)
        planned = _maybe(_day_offset(created_day, pvz_off, pvz_off), null_prob=0.2)

        if planned is not None:
            asm_hi = max(1, pvz_off - 1)
            assembly = _maybe(_day_offset(created_day, 1, asm_hi), null_prob=0.3)
        else:
            assembly = _maybe(_day_offset(created_day, 1, 7), null_prob=0.3)

        begin, end = _planned_time(planned)
        track = _maybe(f"SDEK{random.randint(10_000_000, 99_999_999)}", null_prob=0.4)
        return {
            "plannedDate": planned,
            "plannedTimeBegin": begin,
            "plannedTimeEnd": end,
            "comment": _maybe(f"Позвонить за {random.randint(1, 3)} часа"),
            "pvzCode": f"PVZ-{random.randint(1000, 9999)}",
            "courierTrackNumber": track,
            "courierTrackLink": (
                f"https://www.cdek.ru/ru/tracking/?order_id={track}" if track else None
            ),
            "courierDocumentLink": None,
            "cityId": _maybe(random.choice(_CITY_IDS)),
            "landingLink": None,
            "pickupDate": None,
            "assemblyDate": assembly,
            "callDate": None,
            "postIndex": None,
        }

    else:  # pickup
        pickup_off = random.randint(3, 21)
        pickup = _day_offset(created_day, pickup_off, pickup_off)

        asm_hi = max(1, pickup_off - 1)
        assembly = _maybe(_day_offset(created_day, 1, asm_hi), null_prob=0.3)

        call_off = max(0, pickup_off - random.randint(0, 2))
        call = _maybe(_day_offset(created_day, call_off, call_off), null_prob=0.5)

        return {
            "plannedDate": None,
            "plannedTimeBegin": None,
            "plannedTimeEnd": None,
            "comment": _maybe(f"Позвонить за {random.randint(1, 3)} часа"),
            "pvzCode": None,
            "courierTrackNumber": None,
            "courierTrackLink": None,
            "courierDocumentLink": None,
            "cityId": _maybe(random.choice(_CITY_IDS)),
            "landingLink": _maybe(f"https://example.com/self-plan/{_uuid()}", null_prob=0.7),
            "pickupDate": pickup,
            "assemblyDate": assembly,
            "callDate": call,
            "postIndex": None,
        }


def _gen_spec_planning(created_day: int) -> dict | None:
    if random.random() < 0.4:
        return None

    planned_off = random.randint(5, 45)
    planned = _maybe(_day_offset(created_day, planned_off, planned_off), null_prob=0.2)

    if planned is not None:
        mfg_plan_hi = max(1, planned_off - 1)
        mfg_plan = _maybe(_day_offset(created_day, 1, mfg_plan_hi), null_prob=0.3)
    else:
        mfg_plan = _maybe(_day_offset(created_day, 3, 30), null_prob=0.3)

    today_day = int(time.time() // 86_400)
    if mfg_plan is not None and mfg_plan <= today_day:
        fact_off = mfg_plan - created_day + random.randint(-3, 5)
        fact_off = max(0, fact_off)
        mfg_fact = _maybe(_day_offset(created_day, fact_off, fact_off), null_prob=0.3)
    else:
        mfg_fact = None

    if mfg_plan is not None:
        pur_hi = max(1, (mfg_plan - created_day) - 1) if mfg_plan > created_day else 1
        purchase = _maybe(_day_offset(created_day, 0, pur_hi), null_prob=0.4)
    else:
        purchase = _maybe(_day_offset(created_day, 1, 14), null_prob=0.4)

    return {
        "plannedDate": planned,
        "plannedSupplyDays": _maybe(random.randint(1, 30)),
        "manufactureDatePlan": mfg_plan,
        "manufactureDateFact": mfg_fact,
        "purchaseDate": purchase,
    }


def _gen_spec_delivery(created_day: int) -> dict | None:
    if random.random() < 0.4:
        return None

    planned_off = random.randint(3, 30)
    planned = _maybe(_day_offset(created_day, planned_off, planned_off), null_prob=0.2)
    begin, end = _planned_time(planned)

    tmp_interval = _maybe(_nrec())
    tmp_rejection = (
        _maybe(random.choice(["Клиент перенёс", "Нет в наличии", "Сборка не готова"]))
        if tmp_interval is None else None
    )

    return {
        "methodId": _maybe(random.choice(_DELIVERY_METHOD_IDS)),
        "plannedDate": planned,
        "plannedTimeBegin": begin,
        "plannedTimeEnd": end,
        "temporaryIntervalId": tmp_interval,
        "temporaryRejectionReason": tmp_rejection,
        "intervalId": _maybe(random.choice(_INTERVAL_IDS)),
    }


def _gen_promotions() -> list[dict]:
    if random.random() < 0.5:
        return []
    result = []
    for _ in range(random.randint(1, 3)):
        promo_type = random.choice(_PROMO_TYPES)
        if promo_type not in (None, "discount", "spentBonusPoints"):
            continue
        result.append({
            "id": _uuid(),
            "discountAmount": _decimal(100, 15_000),
            "type": promo_type,
            "promoSource": random.choice(_PROMO_SOURCES),
        })
    return result


def _gen_reserves() -> list[dict]:
    if random.random() < 0.5:
        return []
    return [
        {"MOLId": random.choice(_MOL_IDS), "quantity": _decimal(1, 10, scale=2)}
        for _ in range(random.randint(1, 2))
    ]


def _gen_specification_item(item_number: int, created_day: int) -> dict:
    qty_scale = 4
    qty_exp = decimal.Decimal(10) ** -qty_scale
    qty = decimal.Decimal(str(random.randint(1, 10))).quantize(qty_exp, rounding=decimal.ROUND_HALF_UP)

    price = _decimal(1_000, 150_000)
    total = decimal.Decimal(
        str(round(float(price) * float(qty) * random.uniform(0.7, 1.0), 2))
    ).quantize(decimal.Decimal("0.01"), rounding=decimal.ROUND_HALF_UP)

    return {
        "id": _uuid(),
        "itemNumber": item_number,
        "nomenclatureId": _uuid(),
        "isService": random.random() < 0.1,
        "priceListId": random.choice(_PRICE_LIST_IDS),
        "quantity": qty,
        "totalAmount": total,
        "price": price,
        "taxAmount": _maybe(_decimal(0, float(total) * 0.2)),
        "linkedSpecId": _maybe(_uuid(), null_prob=0.75),
        "subdivisionId": _maybe(random.choice(_SUBDIVISION_IDS)),
        "rejectionReasonId": _maybe(random.choice(_REJECTION_REASON_IDS), null_prob=0.75),
        "planning": _gen_spec_planning(created_day),
        "delivery": _gen_spec_delivery(created_day),
        "skuId": _maybe(_uuid()),
        "orderSpec3PLId": _maybe(_uuid(), null_prob=0.75),
        "statusId": _maybe(random.choice(_STATUS_IDS)),
        "clientStatusId": _maybe(_uuid()),
        "giftCertificateNumber": _maybe(
            f"CERT{random.randint(100_000_000_000, 999_999_999_999)}", null_prob=0.75
        ),
        "isAvailableOnCredit": _maybe(random.choice([True, False])),
        "promotion": _gen_promotions(),
        "reserve": _gen_reserves(),
    }


def _gen_address() -> dict:
    block_type = random.choice(_BLOCK_TYPES)
    apt_type = random.choice(_APARTMENT_TYPES)

    if random.random() < 0.6:
        lat: decimal.Decimal | None = _decimal(55.0, 56.5, scale=7)
        lon: decimal.Decimal | None = _decimal(37.0, 39.5, scale=7)
    else:
        lat = lon = None

    return {
        "name": _maybe(f"Адрес доставки {random.randint(1, 99)}"),
        "streetKLADR": random.choice(_STREET_KLADR),
        "houseNumber": _maybe(str(random.randint(1, 200))),
        "blockType": block_type,
        "blockNumber": _maybe(str(random.randint(1, 20))) if block_type else None,
        "apartmentType": apt_type,
        "apartmentNumber": _maybe(str(random.randint(1, 500))) if apt_type else None,
        "postalCode": _maybe(f"{random.randint(100_000, 199_999)}"),
        "latitude": lat,
        "longitude": lon,
        "floorNumber": _maybe(random.randint(1, 25)),
        "hardlinks": random.sample(_HARDLINK_POOL, random.randint(0, 3)),
    }


def _gen_coupons() -> list[dict]:
    if random.random() < 0.7:
        return []
    return [{"number": _coupon_number()} for _ in range(random.randint(1, 2))]


def _gen_status_history(created_ts: int) -> list[dict]:
    if random.random() < 0.3:
        return []
    result = []
    statuses = random.sample(_STATUS_HISTORY_IDS, min(len(_STATUS_HISTORY_IDS), random.randint(2, 5)))
    ts = created_ts
    for i in range(len(statuses) - 1):
        ts += random.randint(3_600_000, 86_400_000)
        result.append({
            "id": random.choice(_STATUS_HISTORY_IDS),
            "oldStatusId": statuses[i],
            "newStatusId": statuses[i + 1],
            "changeDate": ts,
        })
    return result


def _gen_pd_history(created_ts: int, created_day: int) -> list[dict]:
    if random.random() < 0.6:
        return []
    ts = created_ts
    entries = []
    for _ in range(random.randint(1, 3)):
        ts += random.randint(86_400_000, 7 * 86_400_000)
        entries.append({
            "changeDate": ts,
            "oldPD": _maybe(_day_offset(created_day, 5, 40), null_prob=0.2),
            "newPD": _maybe(_day_offset(created_day, 5, 60), null_prob=0.2),
            "reasonId": random.choice(_REASON_IDS),
        })
    return entries


# ── main generator ────────────────────────────────────────────────────────────

def generate() -> dict[str, Any]:
    """Generate one valid OrderMessage (v3) record."""
    created_ts = _ts_now()
    created_day = created_ts // 86_400_000

    spec_count = random.randint(1, 5)

    mode = random.choice(_DELIVERY_MODES)
    if mode == "courier":
        delivery = _gen_delivery("courier", created_day)
        address = _gen_address()
    elif mode == "pvz":
        delivery = _gen_delivery("pvz", created_day)
        address = None
    elif mode == "pickup":
        delivery = _gen_delivery("pickup", created_day)
        address = None
    else:
        delivery = None
        address = _maybe(_gen_address(), null_prob=0.3)

    order = {
        "id": _uuid(),
        "number": _order_number(),
        "createdAt": created_ts,
        "shopId": random.choice(_SHOP_IDS),
        "managerId": _uuid(),
        "manager2Id": _maybe(_uuid()),
        "paymentTypeId": random.choice(_PAYMENT_TYPES),
        "comment": random.choice([
            "", "Позвонить перед доставкой", "Доставить до подъезда",
            "Собрать на складе", "Срочный заказ", "Клиент - постоянный",
        ]),
        "stateName": random.choice(["Updated", "Deleted", "PendingProcessing", "ProcessingFailed"]),
        "organization": _gen_organization(),
        "sourceId": _maybe(random.choice(_SOURCE_IDS)),
        "categoryId": _maybe(random.choice(_CATEGORY_IDS)),
        "contractId": _maybe(_nrec()),
        "isTaxIncluded": _maybe(random.choice([True, False])),
        "currencyCode": "RUB",
        "appointmentId": _maybe(_nrec()),
        "claimNumber": _maybe(f"РЕК-{random.randint(10_000, 99_999)}", null_prob=0.75),
        "statusId": _maybe(random.choice(_STATUS_IDS)),
        "clientStatusId": _maybe(_uuid()),
        "filialId": _maybe(random.choice(_FILIAL_IDS)),
        "descriptorGroupCode": _maybe(random.choice(_DESCRIPTOR_GROUPS)),
        "additionalPaymentAmount": _maybe(_decimal(100, 50_000)),
        "order3PLId": _maybe(_uuid(), null_prob=0.75),
        "priceZoneId": _maybe(_uuid()),
        "requestedBonusAmount": _maybe(_decimal(0, 5_000)),
        "planning": _gen_planning(created_day),
        "delivery": delivery,
        "specification": [_gen_specification_item(i + 1, created_day) for i in range(spec_count)],
        "address": address,
        "coupon": _gen_coupons(),
        "statusHistory": _gen_status_history(created_ts),
        "pdHistory": _gen_pd_history(created_ts, created_day),
    }

    return {
        "metadata": _gen_metadata(created_ts),
        "messageObject": order,
    }
