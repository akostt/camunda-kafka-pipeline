using Avro;
using Avro.Generic;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using OrderManager.Models;

namespace OrderManager.Services;

public class KafkaProducerService : IDisposable
{
    private readonly string _topic;
    private readonly IProducer<string?, GenericRecord> _producer;
    private readonly ISchemaRegistryClient _registry;
    private RecordSchema? _schema;

    public KafkaProducerService(IConfiguration cfg, ILogger<KafkaProducerService> log)
    {
        _topic = cfg["Kafka:Topic"]!;
        var schemaUrl = cfg["SchemaRegistry:Url"]!;

        _registry = new CachedSchemaRegistryClient(
            new SchemaRegistryConfig { Url = schemaUrl });

        _producer = new ProducerBuilder<string?, GenericRecord>(
            new ProducerConfig { BootstrapServers = cfg["Kafka:BootstrapServers"]! })
            .SetValueSerializer(new AvroSerializer<GenericRecord>(_registry))
            .SetErrorHandler((_, e) => log.LogError("Producer error: {Reason}", e.Reason))
            .Build();
    }

    public async Task<RecordSchema> GetSchemaAsync()
    {
        if (_schema != null) return _schema;
        var registered = await _registry.GetLatestSchemaAsync($"{_topic}-value");
        _schema = (RecordSchema)Avro.Schema.Parse(registered.SchemaString);
        return _schema;
    }

    public async Task ProduceAsync(Order order)
    {
        var rootSchema = await GetSchemaAsync();
        var record = BuildRecord(order, rootSchema);
        await _producer.ProduceAsync(_topic, new Message<string?, GenericRecord>
        {
            Key   = order.Id,
            Value = record
        });
    }

    private GenericRecord BuildRecord(Order order, RecordSchema rootSchema)
    {
        var root = new GenericRecord(rootSchema);

        // metadata
        var metaSchema = (RecordSchema)rootSchema["metadata"].Schema;
        var meta = new GenericRecord(metaSchema);
        meta.Add("integrationTimestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        meta.Add("correlationId", null); // uuid-logicalType in union — Avro C# can't match string to it
        root.Add("metadata", meta);

        // messageObject
        var orderSchema = (RecordSchema)rootSchema["messageObject"].Schema;
        root.Add("messageObject", BuildOrderRecord(order, orderSchema, rootSchema));
        return root;
    }

    private GenericRecord BuildOrderRecord(Order o, RecordSchema schema, RecordSchema rootSchema)
    {
        var r = new GenericRecord(schema);
        r.Add("id", o.Id);
        r.Add("number", o.Number);
        r.Add("createdAt", DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc));
        r.Add("shopId", o.ShopId);
        r.Add("managerId", o.ManagerId);
        r.Add("manager2Id", null); // uuid-union
        r.Add("paymentTypeId", o.PaymentTypeId);
        r.Add("comment", o.Comment);
        r.Add("stateName", new GenericEnum(
            (EnumSchema)schema["stateName"].Schema, o.StateName));
        r.Add("statusId", (object?)o.StatusId);
        r.Add("clientStatusId", null); // uuid-union
        r.Add("filialId", (object?)o.FilialId);
        r.Add("sourceId", (object?)o.SourceId);
        r.Add("categoryId", (object?)o.CategoryId);
        r.Add("contractId", (object?)o.ContractId);
        r.Add("isTaxIncluded", (object?)o.IsTaxIncluded);
        r.Add("currencyCode", (object?)o.CurrencyCode);
        r.Add("appointmentId", (object?)o.AppointmentId);
        r.Add("claimNumber", (object?)o.ClaimNumber);
        r.Add("descriptorGroupCode", (object?)o.DescriptorGroupCode);
        r.Add("additionalPaymentAmount",
            o.AdditionalPaymentAmount.HasValue
                ? (object)D(o.AdditionalPaymentAmount.Value, 2)
                : null);
        r.Add("requestedBonusAmount",
            o.RequestedBonusAmount.HasValue
                ? (object)D(o.RequestedBonusAmount.Value, 2)
                : null);
        r.Add("order3PLId", null); // uuid-union
        r.Add("priceZoneId", null); // uuid-union

        // organization
        var orgSchema = (RecordSchema)schema["organization"].Schema;
        var org = new GenericRecord(orgSchema);
        org.Add("clientId",   o.Organization.ClientId);
        org.Add("shipperId",  null); // uuid-union
        org.Add("agentId",    null); // uuid-union
        org.Add("receiverId", null); // uuid-union
        r.Add("organization", org);

        // planning
        r.Add("planning", BuildPlanning(o.Planning, schema));

        // delivery
        r.Add("delivery", BuildDelivery(o.Delivery, schema));

        // specification
        var specArraySchema = (ArraySchema)schema["specification"].Schema;
        r.Add("specification", o.Specification
            .Select(s => BuildSpec(s, (RecordSchema)specArraySchema.ItemSchema))
            .ToArray());

        // address
        r.Add("address", BuildAddress(o.Address, schema));

        // coupon
        var couponSchema = (RecordSchema)((ArraySchema)schema["coupon"].Schema).ItemSchema;
        r.Add("coupon", o.Coupon.Select(c =>
        {
            var cr = new GenericRecord(couponSchema);
            cr.Add("number", c.Number);
            return (object)cr;
        }).ToArray());

        // statusHistory
        var shSchema = (RecordSchema)((ArraySchema)schema["statusHistory"].Schema).ItemSchema;
        r.Add("statusHistory", o.StatusHistory.Select(h =>
        {
            var hr = new GenericRecord(shSchema);
            hr.Add("id", h.Id);
            hr.Add("oldStatusId", h.OldStatusId);
            hr.Add("newStatusId", h.NewStatusId);
            hr.Add("changeDate", DateTime.SpecifyKind(h.ChangeDate, DateTimeKind.Utc));
            return (object)hr;
        }).ToArray());

        // pdHistory
        var pdSchema = (RecordSchema)((ArraySchema)schema["pdHistory"].Schema).ItemSchema;
        r.Add("pdHistory", o.PdHistory.Select(h =>
        {
            var hr = new GenericRecord(pdSchema);
            hr.Add("changeDate", DateTime.SpecifyKind(h.ChangeDate, DateTimeKind.Utc));
            hr.Add("oldPD", h.OldPD.HasValue ? (object)h.OldPD.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : null);
            hr.Add("newPD", h.NewPD.HasValue ? (object)h.NewPD.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : null);
            hr.Add("reasonId", h.ReasonId);
            return (object)hr;
        }).ToArray());

        return r;
    }

    private static object? BuildPlanning(OrderPlanning? p, RecordSchema orderSchema)
    {
        if (p == null) return null;
        var schema = GetUnionRecord(orderSchema, "planning");
        if (schema == null) return null;
        var r = new GenericRecord(schema);
        r.Add("plannedDate", DateToAvro(p.PlannedDate));
        r.Add("protocolId", null); // uuid-union
        r.Add("manufactureDate", DateToAvro(p.ManufactureDate));
        return r;
    }

    private static object? BuildSpecPlanning(OrderSpecPlanning? p, RecordSchema specSchema)
    {
        if (p == null) return null;
        var schema = GetUnionRecord(specSchema, "planning");
        if (schema == null) return null;
        var r = new GenericRecord(schema);
        r.Add("plannedDate", DateToAvro(p.PlannedDate));
        r.Add("plannedSupplyDays", (object?)p.PlannedSupplyDays);
        r.Add("manufactureDatePlan", DateToAvro(p.ManufactureDatePlan));
        r.Add("manufactureDateFact", DateToAvro(p.ManufactureDateFact));
        r.Add("purchaseDate", DateToAvro(p.PurchaseDate));
        return r;
    }

    private static object? BuildDelivery(OrderDelivery? d, RecordSchema orderSchema)
    {
        if (d == null) return null;
        var schema = GetUnionRecord(orderSchema, "delivery");
        if (schema == null) return null;
        var r = new GenericRecord(schema);
        r.Add("plannedDate", DateToAvro(d.PlannedDate));
        r.Add("plannedTimeBegin", (object?)d.PlannedTimeBegin);
        r.Add("plannedTimeEnd", (object?)d.PlannedTimeEnd);
        r.Add("comment", (object?)d.Comment);
        r.Add("pvzCode", (object?)d.PvzCode);
        r.Add("courierTrackNumber", (object?)d.CourierTrackNumber);
        r.Add("courierTrackLink", (object?)d.CourierTrackLink);
        r.Add("courierDocumentLink", (object?)d.CourierDocumentLink);
        r.Add("cityId", (object?)d.CityId);
        r.Add("landingLink", (object?)d.LandingLink);
        r.Add("pickupDate", DateToAvro(d.PickupDate));
        r.Add("assemblyDate", DateToAvro(d.AssemblyDate));
        r.Add("callDate", DateToAvro(d.CallDate));
        r.Add("postIndex", (object?)d.PostIndex);
        return r;
    }

    private static GenericRecord BuildSpec(OrderSpecification s, RecordSchema schema)
    {
        var r = new GenericRecord(schema);
        r.Add("id", s.Id);
        r.Add("itemNumber", s.ItemNumber);
        r.Add("nomenclatureId", s.NomenclatureId);
        r.Add("isService", s.IsService);
        r.Add("priceListId", s.PriceListId);
        r.Add("quantity", D(s.Quantity, 4));
        r.Add("totalAmount", D(s.TotalAmount, 2));
        r.Add("price", D(s.Price, 2));
        r.Add("taxAmount", s.TaxAmount.HasValue ? (object)D(s.TaxAmount.Value, 2) : null);
        r.Add("linkedSpecId", null); // uuid-union
        r.Add("subdivisionId", (object?)s.SubdivisionId);
        r.Add("rejectionReasonId", (object?)s.RejectionReasonId);
        r.Add("skuId", null); // uuid-union
        r.Add("orderSpec3PLId", null); // uuid-union
        r.Add("statusId", (object?)s.StatusId);
        r.Add("clientStatusId", null); // uuid-union
        r.Add("giftCertificateNumber", (object?)s.GiftCertificateNumber);
        r.Add("isAvailableOnCredit", (object?)s.IsAvailableOnCredit);
        r.Add("planning", BuildSpecPlanning(s.Planning, schema));
        r.Add("delivery", null);

        var promoSchema = (RecordSchema)((ArraySchema)schema["promotion"].Schema).ItemSchema;
        r.Add("promotion", s.Promotion.Select(p =>
        {
            var pr = new GenericRecord(promoSchema);
            pr.Add("id", p.Id);
            pr.Add("discountAmount", D(p.DiscountAmount, 2));
            pr.Add("type", (object?)p.Type);
            pr.Add("promoSource", (object?)p.PromoSource);
            return (object)pr;
        }).ToArray());

        var resSchema = (RecordSchema)((ArraySchema)schema["reserve"].Schema).ItemSchema;
        r.Add("reserve", s.Reserve.Select(rv =>
        {
            var rr = new GenericRecord(resSchema);
            rr.Add("MOLId", rv.MOLId);
            rr.Add("quantity", D(rv.Quantity, 2));
            return (object)rr;
        }).ToArray());

        return r;
    }

    private static object? BuildAddress(OrderAddress? a, RecordSchema orderSchema)
    {
        if (a == null) return null;
        var schema = GetUnionRecord(orderSchema, "address");
        if (schema == null) return null;
        var r = new GenericRecord(schema);
        r.Add("name", (object?)a.Name);
        r.Add("streetKLADR", a.StreetKLADR);
        r.Add("houseNumber", (object?)a.HouseNumber);
        r.Add("blockType", (object?)a.BlockType);
        r.Add("blockNumber", (object?)a.BlockNumber);
        r.Add("apartmentType", (object?)a.ApartmentType);
        r.Add("apartmentNumber", (object?)a.ApartmentNumber);
        r.Add("postalCode", (object?)a.PostalCode);
        r.Add("latitude", a.Latitude.HasValue ? (object)D(a.Latitude.Value, 7) : null);
        r.Add("longitude", a.Longitude.HasValue ? (object)D(a.Longitude.Value, 7) : null);
        r.Add("floorNumber", (object?)a.FloorNumber);
        r.Add("hardlinks", a.Hardlinks.Cast<object>().ToArray());
        return r;
    }

    private static RecordSchema? GetUnionRecord(RecordSchema schema, string field)
    {
        var f = schema[field];
        if (f?.Schema is UnionSchema us)
            return us.Schemas.OfType<RecordSchema>().FirstOrDefault();
        return f?.Schema as RecordSchema;
    }

    private static DateTime? DateToAvro(DateOnly? d) =>
        d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : null;

    private static Avro.AvroDecimal D(decimal value, int scale)
    {
        var factor = (long)Math.Pow(10, scale);
        var unscaled = new System.Numerics.BigInteger((long)Math.Round(value * factor));
        return new Avro.AvroDecimal(unscaled, scale);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        _registry.Dispose();
    }
}
