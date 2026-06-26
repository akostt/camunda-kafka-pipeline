using Avro.Generic;
using OrderManager.Models;
using System.Numerics;

namespace OrderManager;

public static class AvroMapper
{
    public static OrderMessage? ToOrderMessage(GenericRecord root)
    {
        try
        {
            var meta = Get<GenericRecord>(root, "metadata");
            var obj  = Get<GenericRecord>(root, "messageObject");
            if (obj == null) return null;

            return new OrderMessage
            {
                Metadata = meta == null ? new() : new MessageInfo
                {
                    IntegrationTimestamp = Get<long>(meta, "integrationTimestamp"),
                    CorrelationId = GetStr(meta, "correlationId"),
                },
                MessageObject = MapOrder(obj)
            };
        }
        catch { return null; }
    }

    private static Order MapOrder(GenericRecord r) => new()
    {
        Id            = GetStr(r, "id") ?? "",
        Number        = GetStr(r, "number") ?? "",
        CreatedAt     = GetTimestampMs(r, "createdAt"),
        ShopId        = GetStr(r, "shopId") ?? "",
        ManagerId     = GetStr(r, "managerId") ?? "",
        Manager2Id    = GetStr(r, "manager2Id"),
        PaymentTypeId = GetStr(r, "paymentTypeId") ?? "",
        Comment       = GetStr(r, "comment") ?? "",
        StateName     = GetEnum(r, "stateName") ?? "Updated",
        StatusId      = GetStr(r, "statusId"),
        ClientStatusId= GetStr(r, "clientStatusId"),
        FilialId      = GetStr(r, "filialId"),
        SourceId      = GetStr(r, "sourceId"),
        CategoryId    = GetStr(r, "categoryId"),
        ContractId    = GetStr(r, "contractId"),
        IsTaxIncluded = GetBool(r, "isTaxIncluded"),
        CurrencyCode  = GetStr(r, "currencyCode"),
        AppointmentId = GetStr(r, "appointmentId"),
        ClaimNumber   = GetStr(r, "claimNumber"),
        DescriptorGroupCode = GetStr(r, "descriptorGroupCode"),
        AdditionalPaymentAmount = GetDecimal(r, "additionalPaymentAmount", 2),
        RequestedBonusAmount    = GetDecimal(r, "requestedBonusAmount", 2),
        Order3PLId    = GetStr(r, "order3PLId"),
        PriceZoneId   = GetStr(r, "priceZoneId"),
        Organization  = MapOrg(Get<GenericRecord>(r, "organization")),
        Planning      = MapPlanning(Get<GenericRecord>(r, "planning")),
        Delivery      = MapDelivery(Get<GenericRecord>(r, "delivery")),
        Address       = MapAddress(Get<GenericRecord>(r, "address")),
        Specification = MapArray(r, "specification", MapSpec),
        Coupon        = MapArray(r, "coupon", rc => new OrderCoupon { Number = GetStr(rc, "number") ?? "" }),
        StatusHistory = MapArray(r, "statusHistory", MapStatusHistory),
        PdHistory     = MapArray(r, "pdHistory", MapPDHistory),
    };

    private static OrderOrganization MapOrg(GenericRecord? r) => r == null ? new() : new()
    {
        ClientId   = GetStr(r, "clientId") ?? "",
        ShipperId  = GetStr(r, "shipperId"),
        AgentId    = GetStr(r, "agentId"),
        ReceiverId = GetStr(r, "receiverId"),
    };

    private static OrderPlanning? MapPlanning(GenericRecord? r) => r == null ? null : new()
    {
        PlannedDate     = GetDate(r, "plannedDate"),
        ProtocolId      = GetStr(r, "protocolId"),
        ManufactureDate = GetDate(r, "manufactureDate"),
    };

    private static OrderDelivery? MapDelivery(GenericRecord? r) => r == null ? null : new()
    {
        PlannedDate         = GetDate(r, "plannedDate"),
        PlannedTimeBegin    = GetInt(r, "plannedTimeBegin"),
        PlannedTimeEnd      = GetInt(r, "plannedTimeEnd"),
        Comment             = GetStr(r, "comment"),
        PvzCode             = GetStr(r, "pvzCode"),
        CourierTrackNumber  = GetStr(r, "courierTrackNumber"),
        CourierTrackLink    = GetStr(r, "courierTrackLink"),
        CourierDocumentLink = GetStr(r, "courierDocumentLink"),
        CityId              = GetStr(r, "cityId"),
        LandingLink         = GetStr(r, "landingLink"),
        PickupDate          = GetDate(r, "pickupDate"),
        AssemblyDate        = GetDate(r, "assemblyDate"),
        CallDate            = GetDate(r, "callDate"),
        PostIndex           = GetStr(r, "postIndex"),
    };

    private static OrderSpecification MapSpec(GenericRecord r) => new()
    {
        Id                 = GetStr(r, "id") ?? "",
        ItemNumber         = Get<int>(r, "itemNumber"),
        NomenclatureId     = GetStr(r, "nomenclatureId") ?? "",
        IsService          = Get<bool>(r, "isService"),
        PriceListId        = GetStr(r, "priceListId") ?? "",
        Quantity           = GetDecimalRequired(r, "quantity", 4),
        TotalAmount        = GetDecimalRequired(r, "totalAmount", 2),
        Price              = GetDecimalRequired(r, "price", 2),
        TaxAmount          = GetDecimal(r, "taxAmount", 2),
        LinkedSpecId       = GetStr(r, "linkedSpecId"),
        SubdivisionId      = GetStr(r, "subdivisionId"),
        RejectionReasonId  = GetStr(r, "rejectionReasonId"),
        SkuId              = GetStr(r, "skuId"),
        OrderSpec3PLId     = GetStr(r, "orderSpec3PLId"),
        StatusId           = GetStr(r, "statusId"),
        ClientStatusId     = GetStr(r, "clientStatusId"),
        GiftCertificateNumber = GetStr(r, "giftCertificateNumber"),
        IsAvailableOnCredit   = GetBool(r, "isAvailableOnCredit"),
        Planning           = MapSpecPlanning(Get<GenericRecord>(r, "planning")),
        Delivery           = MapSpecDelivery(Get<GenericRecord>(r, "delivery")),
        Promotion          = MapArray(r, "promotion", MapPromotion),
        Reserve            = MapArray(r, "reserve", MapReserve),
    };

    private static OrderSpecPlanning? MapSpecPlanning(GenericRecord? r) => r == null ? null : new()
    {
        PlannedDate          = GetDate(r, "plannedDate"),
        PlannedSupplyDays    = GetInt(r, "plannedSupplyDays"),
        ManufactureDatePlan  = GetDate(r, "manufactureDatePlan"),
        ManufactureDateFact  = GetDate(r, "manufactureDateFact"),
        PurchaseDate         = GetDate(r, "purchaseDate"),
    };

    private static OrderSpecDelivery? MapSpecDelivery(GenericRecord? r) => r == null ? null : new()
    {
        MethodId                 = GetStr(r, "methodId"),
        PlannedDate              = GetDate(r, "plannedDate"),
        PlannedTimeBegin         = GetInt(r, "plannedTimeBegin"),
        PlannedTimeEnd           = GetInt(r, "plannedTimeEnd"),
        TemporaryIntervalId      = GetStr(r, "temporaryIntervalId"),
        TemporaryRejectionReason = GetStr(r, "temporaryRejectionReason"),
        IntervalId               = GetStr(r, "intervalId"),
    };

    private static OrderSpecPromotion MapPromotion(GenericRecord r) => new()
    {
        Id             = GetStr(r, "id") ?? "",
        DiscountAmount = GetDecimalRequired(r, "discountAmount", 2),
        Type           = GetStr(r, "type"),
        PromoSource    = GetStr(r, "promoSource"),
    };

    private static OrderSpecReserve MapReserve(GenericRecord r) => new()
    {
        MOLId    = GetStr(r, "MOLId") ?? "",
        Quantity = GetDecimalRequired(r, "quantity", 2),
    };

    private static OrderAddress? MapAddress(GenericRecord? r) => r == null ? null : new()
    {
        Name            = GetStr(r, "name"),
        StreetKLADR     = GetStr(r, "streetKLADR") ?? "",
        HouseNumber     = GetStr(r, "houseNumber"),
        BlockType       = GetStr(r, "blockType"),
        BlockNumber     = GetStr(r, "blockNumber"),
        ApartmentType   = GetStr(r, "apartmentType"),
        ApartmentNumber = GetStr(r, "apartmentNumber"),
        PostalCode      = GetStr(r, "postalCode"),
        Latitude        = GetDecimal(r, "latitude", 7),
        Longitude       = GetDecimal(r, "longitude", 7),
        FloorNumber     = GetInt(r, "floorNumber"),
        Hardlinks       = GetStringArray(r, "hardlinks"),
    };

    private static OrderStatusHistory MapStatusHistory(GenericRecord r) => new()
    {
        Id          = GetStr(r, "id") ?? "",
        OldStatusId = GetStr(r, "oldStatusId") ?? "",
        NewStatusId = GetStr(r, "newStatusId") ?? "",
        ChangeDate  = GetTimestampMs(r, "changeDate"),
    };

    private static OrderPDHistory MapPDHistory(GenericRecord r) => new()
    {
        ChangeDate = GetTimestampMs(r, "changeDate"),
        OldPD      = GetDate(r, "oldPD"),
        NewPD      = GetDate(r, "newPD"),
        ReasonId   = GetStr(r, "reasonId") ?? "",
    };

    // ── helpers ──────────────────────────────────────────────

    private static T? Get<T>(GenericRecord r, string f)
    {
        if (!r.TryGetValue(f, out var v)) return default;
        if (v is T t) return t;
        return default;
    }

    private static string? GetStr(GenericRecord r, string f)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return null;
        return v.ToString();
    }

    private static string? GetEnum(GenericRecord r, string f)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return null;
        return v is Avro.Generic.GenericEnum ge ? ge.Value : v.ToString();
    }

    private static bool? GetBool(GenericRecord r, string f)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return null;
        return v is bool b ? b : null;
    }

    private static int? GetInt(GenericRecord r, string f)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return null;
        return v is int i ? i : null;
    }

    // Avro timestamp-millis: deserializer returns DateTime, not long
    private static DateTime GetTimestampMs(GenericRecord r, string f)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return DateTime.UtcNow;
        if (v is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        if (v is long ms) return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return DateTime.UtcNow;
    }

    private static DateOnly? GetDate(GenericRecord r, string f)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return null;
        if (v is DateTime dt) return DateOnly.FromDateTime(dt);
        if (v is int days)    return DateOnly.FromDateTime(new DateTime(1970, 1, 1).AddDays(days));
        return null;
    }

    private static decimal? GetDecimal(GenericRecord r, string f, int scale)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return null;
        if (v is Avro.AvroDecimal ad) return (decimal)ad;
        return BytesToDecimal(v as byte[], scale);
    }

    private static decimal GetDecimalRequired(GenericRecord r, string f, int scale)
        => GetDecimal(r, f, scale) ?? 0m;

    private static decimal? BytesToDecimal(byte[]? bytes, int scale)
    {
        if (bytes == null || bytes.Length == 0) return null;
        // Avro: big-endian two's complement → BigInteger (little-endian)
        var le = bytes.Reverse().ToArray();
        bool negative = (bytes[0] & 0x80) != 0;
        Array.Resize(ref le, le.Length + 1);
        le[^1] = negative ? (byte)0xFF : (byte)0x00;
        var big = new BigInteger(le);
        decimal div = 1m;
        for (int i = 0; i < scale; i++) div *= 10m;
        return (decimal)big / div;
    }

    private static List<T> MapArray<T>(GenericRecord r, string f, Func<GenericRecord, T> map)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return [];
        if (v is not object[] arr) return [];
        var result = new List<T>();
        foreach (var item in arr)
            if (item is GenericRecord gr)
                result.Add(map(gr));
        return result;
    }

    private static List<string> GetStringArray(GenericRecord r, string f)
    {
        if (!r.TryGetValue(f, out var v) || v == null) return [];
        if (v is not object[] arr) return [];
        return arr.OfType<string>().ToList();
    }
}
