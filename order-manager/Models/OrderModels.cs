using System.ComponentModel.DataAnnotations;

namespace OrderManager.Models;

public class OrderMessage
{
    public MessageInfo Metadata { get; set; } = new();
    public Order MessageObject { get; set; } = new();
}

public class MessageInfo
{
    public long IntegrationTimestamp { get; set; }
    public string? CorrelationId { get; set; }
}

public class Order
{
    [StringLength(100)]
    public string Id { get; set; } = "";

    [StringLength(50)]
    public string Number { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    [Required(ErrorMessage = "Магазин (shopId) обязателен")]
    [StringLength(100)]
    public string ShopId { get; set; } = "";

    [Required(ErrorMessage = "Менеджер (managerId) обязателен")]
    [StringLength(100)]
    public string ManagerId { get; set; } = "";

    [StringLength(100)]
    public string? Manager2Id { get; set; }

    [StringLength(50)]
    public string PaymentTypeId { get; set; } = "";

    [StringLength(500, ErrorMessage = "Комментарий не может превышать 500 символов")]
    public string Comment { get; set; } = "";

    public string StateName { get; set; } = "Updated";
    public string? StatusId { get; set; }
    public string? ClientStatusId { get; set; }
    public string? FilialId { get; set; }
    public string? SourceId { get; set; }
    public string? CategoryId { get; set; }
    public string? ContractId { get; set; }
    public bool? IsTaxIncluded { get; set; }
    public string? CurrencyCode { get; set; }
    public string? AppointmentId { get; set; }
    public string? ClaimNumber { get; set; }
    public string? DescriptorGroupCode { get; set; }
    public decimal? AdditionalPaymentAmount { get; set; }
    public decimal? RequestedBonusAmount { get; set; }
    public string? Order3PLId { get; set; }
    public string? PriceZoneId { get; set; }
    public OrderOrganization Organization { get; set; } = new();
    public OrderPlanning? Planning { get; set; }
    public OrderDelivery? Delivery { get; set; }
    public OrderAddress? Address { get; set; }
    public List<OrderSpecification> Specification { get; set; } = [];
    public List<OrderCoupon> Coupon { get; set; } = [];
    public List<OrderStatusHistory> StatusHistory { get; set; } = [];
    public List<OrderPDHistory> PdHistory { get; set; } = [];
}

public class OrderOrganization
{
    public string ClientId { get; set; } = "";
    public string? ShipperId { get; set; }
    public string? AgentId { get; set; }
    public string? ReceiverId { get; set; }
}

public class OrderPlanning
{
    public DateOnly? PlannedDate { get; set; }
    public string? ProtocolId { get; set; }
    public DateOnly? ManufactureDate { get; set; }
}

public class OrderDelivery
{
    public DateOnly? PlannedDate { get; set; }
    public int? PlannedTimeBegin { get; set; }
    public int? PlannedTimeEnd { get; set; }
    public string? Comment { get; set; }
    public string? PvzCode { get; set; }
    public string? CourierTrackNumber { get; set; }
    public string? CourierTrackLink { get; set; }
    public string? CourierDocumentLink { get; set; }
    public string? CityId { get; set; }
    public string? LandingLink { get; set; }
    public DateOnly? PickupDate { get; set; }
    public DateOnly? AssemblyDate { get; set; }
    public DateOnly? CallDate { get; set; }
    public string? PostIndex { get; set; }
}

public class OrderSpecification
{
    public string Id { get; set; } = "";
    public int ItemNumber { get; set; }
    public string NomenclatureId { get; set; } = "";
    public bool IsService { get; set; }
    public string PriceListId { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal Price { get; set; }
    public decimal? TaxAmount { get; set; }
    public string? LinkedSpecId { get; set; }
    public string? SubdivisionId { get; set; }
    public string? RejectionReasonId { get; set; }
    public string? SkuId { get; set; }
    public string? OrderSpec3PLId { get; set; }
    public string? StatusId { get; set; }
    public string? ClientStatusId { get; set; }
    public string? GiftCertificateNumber { get; set; }
    public bool? IsAvailableOnCredit { get; set; }
    public OrderSpecPlanning? Planning { get; set; }
    public OrderSpecDelivery? Delivery { get; set; }
    public List<OrderSpecPromotion> Promotion { get; set; } = [];
    public List<OrderSpecReserve> Reserve { get; set; } = [];
}

public class OrderSpecPlanning
{
    public DateOnly? PlannedDate { get; set; }
    public int? PlannedSupplyDays { get; set; }
    public DateOnly? ManufactureDatePlan { get; set; }
    public DateOnly? ManufactureDateFact { get; set; }
    public DateOnly? PurchaseDate { get; set; }
}

public class OrderSpecDelivery
{
    public string? MethodId { get; set; }
    public DateOnly? PlannedDate { get; set; }
    public int? PlannedTimeBegin { get; set; }
    public int? PlannedTimeEnd { get; set; }
    public string? TemporaryIntervalId { get; set; }
    public string? TemporaryRejectionReason { get; set; }
    public string? IntervalId { get; set; }
}

public class OrderSpecPromotion
{
    public string Id { get; set; } = "";
    public decimal DiscountAmount { get; set; }
    public string? Type { get; set; }
    public string? PromoSource { get; set; }
}

public class OrderSpecReserve
{
    public string MOLId { get; set; } = "";
    public decimal Quantity { get; set; }
}

public class OrderAddress
{
    public string? Name { get; set; }
    public string StreetKLADR { get; set; } = "";
    public string? HouseNumber { get; set; }
    public string? BlockType { get; set; }
    public string? BlockNumber { get; set; }
    public string? ApartmentType { get; set; }
    public string? ApartmentNumber { get; set; }
    public string? PostalCode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? FloorNumber { get; set; }
    public List<string> Hardlinks { get; set; } = [];
}

public class OrderCoupon
{
    public string Number { get; set; } = "";
}

public class OrderStatusHistory
{
    public string Id { get; set; } = "";
    public string OldStatusId { get; set; } = "";
    public string NewStatusId { get; set; } = "";
    public DateTime ChangeDate { get; set; }
}

public class OrderPDHistory
{
    public DateTime ChangeDate { get; set; }
    public DateOnly? OldPD { get; set; }
    public DateOnly? NewPD { get; set; }
    public string ReasonId { get; set; } = "";
}

public class OrderListItem
{
    public string Id { get; set; } = "";
    public string Number { get; set; } = "";
    public string StateName { get; set; } = "";
    public string? StatusId { get; set; }
    public string StatusName { get; set; } = "";
    public string StatusColor { get; set; } = "";
    public string StatusGroup { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string ShopId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public int SpecCount { get; set; }
    public DateOnly? PlannedDeliveryDate { get; set; }
}

public class UpdateStatusRequest
{
    [Required(ErrorMessage = "Новый статус (statusId) обязателен")]
    [StringLength(50, ErrorMessage = "statusId не может быть длиннее 50 символов")]
    public string StatusId { get; set; } = "";

    [StringLength(500, ErrorMessage = "Комментарий не может превышать 500 символов")]
    public string? Comment { get; set; }
}

public class GenerateOrdersRequest
{
    [Range(1, 100, ErrorMessage = "Количество заказов должно быть от 1 до 100")]
    public int Count { get; set; } = 1;

    [StringLength(50)]
    public string? StatusId { get; set; }
}
