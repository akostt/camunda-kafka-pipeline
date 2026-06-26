namespace OrderManager.Models;

public record StatusInfo(string Id, string Name, string Short, string Group, string Color);

public static class Statuses
{
    public static readonly List<StatusInfo> All =
    [
        // Стадия заказа
        new("C0003089DD24DC9A", "Оформляемый",               "оформ.",   "Стадия",       "#4d79ff"),
        new("C0008686474408E5", "Исполняемый",                "исполн.",  "Стадия",       "#7ba0ff"),
        // Завершение
        new("8001000000000118", "Выполнен",                   "Вып.",     "Завершение",   "#00e5a0"),
        new("8010000000000108", "Отменён",                    "Отм.",     "Завершение",   "#ff4c6a"),
        // Ожидание / блокировка
        new("80010000000020AE", "Внимание",                   "Вним.",    "Ожидание",     "#f5a623"),
        new("80010000000020A4", "Нет ткани",                  "НетТк.",   "Ожидание",     "#f5a623"),
        new("8001000000001523", "Ожидание",                   "Ожид.",    "Ожидание",     "#f5a623"),
        new("8001000000001564", "Жду оплату",                 "Оплату",   "Ожидание",     "#ffb547"),
        // Производство
        new("8001000000001500", "Обрабатывается-Ковров",      "Обр.-К",   "Производство", "#a855f7"),
        new("8001000000001501", "Обрабатывается-Новосибирск", "Обр.-Н",   "Производство", "#a855f7"),
        new("800100000000011F", "Ковров-планирование",        "К-План.",  "Производство", "#c084fc"),
        new("8001000000000120", "Новосибирск-планирование",   "Н-План.",  "Производство", "#c084fc"),
        new("8001000000000121", "Поставка",                   "Постав.",  "Производство", "#c084fc"),
        // Логистика / склад
        new("80010000000020A9", "Центральный склад",          "Ц.Скл.",   "Логистика",    "#00cfff"),
        new("80010000000034F4", "Перемещение",                "Перемещ.", "Логистика",    "#00cfff"),
        new("80010000000034F5", "РС",                         "РС",       "Логистика",    "#00cfff"),
        new("800100000000195B", "В пути",                     "Путь",     "Логистика",    "#00cfff"),
        new("80010000000020B3", "Региональный склад",         "Регион.",  "Логистика",    "#00cfff"),
        new("8001000000003E0E", "Транзит",                    "транзит",  "Логистика",    "#00cfff"),
        new("80010000000020B4", "Перемещение на ТТ",          "Путь ТТ",  "Логистика",    "#00cfff"),
        new("80010000000020B5", "Торговая точка",             "ТТ",       "Логистика",    "#22d3ee"),
        new("8001000000000122", "Готов к отгрузке",           "Готов",    "Логистика",    "#00e5a0"),
        new("80010000000052A0", "Смежный склад",              "Смеж.",    "Логистика",    "#00cfff"),
        new("8001000000000154", "Отгрузка завершена",         "Отгр.",    "Логистика",    "#00b4d8"),
    ];

    private static readonly Dictionary<string, StatusInfo> _byId =
        All.ToDictionary(s => s.Id, s => s);

    public static StatusInfo? ById(string? id) =>
        id != null && _byId.TryGetValue(id, out var s) ? s : null;

    public static string NameById(string? id) =>
        ById(id)?.Name ?? id ?? "";

    public static string ColorById(string? id) =>
        ById(id)?.Color ?? "gray";

    public static string GroupById(string? id) =>
        ById(id)?.Group ?? "";

    public static string TailwindBadge(string color) => color switch
    {
        "blue"   => "bg-blue-100 text-blue-800",
        "green"  => "bg-green-100 text-green-800",
        "red"    => "bg-red-100 text-red-800",
        "yellow" => "bg-yellow-100 text-yellow-800",
        "purple" => "bg-purple-100 text-purple-800",
        "cyan"   => "bg-cyan-100 text-cyan-800",
        "teal"   => "bg-teal-100 text-teal-800",
        _        => "bg-gray-100 text-gray-700",
    };
}

// Статусы строк ДО (specification.statusId в схеме all_order_v2).
// ★ — используются в авросхеме (выделены жёлтым в источнике).
public static class LineStatuses
{
    public static readonly List<StatusInfo> All =
    [
        // Стадия строки
        new("C0003089DD24DC9A", "Оформляемый",       "оформ.",   "Стадия",       "#4d79ff"),
        new("C0008686474408E5", "Исполняемый",        "исполн.",  "Стадия",       "#7ba0ff"),
        // Ожидание / блокировка
        new("80010000000020AE", "Внимание",           "Вним.",    "Ожидание",     "#f5a623"),
        new("80010000000020A4", "Нет ткани",          "НетТк.",   "Ожидание",     "#f5a623"),
        // Планирование
        new("800100000000011F", "Планирование",       "План.",    "Планирование", "#c084fc"),
        // Производство / поставка
        new("8001000000001500", "Обрабатывается",     "Обраб.",   "Производство", "#a855f7"),
        new("8001000000000121", "Поставка",           "Постав.",  "Производство", "#c084fc"),
        // Логистика / склад
        new("80010000000020A9", "Центральный склад",  "Ц.Скл.",   "Логистика",    "#00cfff"),
        new("80010000000034F4", "Перемещение",        "Перемещ.", "Логистика",    "#00cfff"),
        new("80010000000034F5", "РС",                 "РС",       "Логистика",    "#00cfff"),
        new("800100000000195B", "В пути",             "Путь",     "Логистика",    "#00cfff"),
        new("80010000000020B3", "Региональный склад", "Регион.",  "Логистика",    "#00cfff"),
        new("C0008686474408E6", "Транзит",            "транзит",  "Логистика",    "#00cfff"),
        new("80010000000020B4", "Перемещение на ТТ",  "Путь ТТ",  "Логистика",    "#00cfff"),
        new("80010000000020B5", "Торговая точка",     "ТТ",       "Логистика",    "#22d3ee"),
        new("8001000000000122", "Хранение",           "Хран.",    "Логистика",    "#00e5a0"),
        new("8001000000000154", "Отгружен",           "Отгр.",    "Логистика",    "#00b4d8"),
        // Завершение
        new("8001000000000118", "Выполнен",           "Вып.",     "Завершение",   "#00e5a0"),
        new("8010000000000108", "Отменен",            "Отм.",     "Завершение",   "#ff4c6a"),
    ];

    private static readonly Dictionary<string, StatusInfo> _byId =
        All.ToDictionary(s => s.Id, s => s);

    public static StatusInfo? ById(string? id) =>
        id != null && _byId.TryGetValue(id, out var s) ? s : null;

    public static string NameById(string? id) =>
        ById(id)?.Name ?? id ?? "";

    // Порядок жизненного цикла строки (от раннего к позднему).
    // Используется для определения статуса заказа из набора статусов строк.
    public static readonly IReadOnlyList<string> LifecycleOrder =
    [
        "C0003089DD24DC9A", // Оформляемый
        "C0008686474408E5", // Исполняемый
        "80010000000020AE", // Внимание
        "80010000000020A4", // Нет ткани
        "8001000000001500", // Обрабатывается
        "800100000000011F", // Планирование
        "8001000000000121", // Поставка
        "800100000000195B", // В пути
        "80010000000020A9", // Центральный склад
        "80010000000034F4", // Перемещение
        "80010000000034F5", // РС
        "80010000000020B3", // Региональный склад
        "C0008686474408E6", // Транзит
        "80010000000020B4", // Перемещение на ТТ
        "80010000000020B5", // Торговая точка
        "8001000000000122", // Хранение
        "8001000000000154", // Отгружен
        "8001000000000118", // Выполнен
        "8010000000000108", // Отменён
    ];

    private static readonly Dictionary<string, int> _lifecycleIndex =
        LifecycleOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

    public static int LifecycleIndex(string? id) =>
        id != null && _lifecycleIndex.TryGetValue(id, out var i) ? i : int.MaxValue;

    // ── Статусы строк (line statusId), используемые в правилах вывода ──────────
    private const string LS_ATTENTION    = "80010000000020AE"; // Внимание
    private const string LS_NO_FABRIC    = "80010000000020A4"; // Нет ткани
    private const string LS_CANCELLED    = "8010000000000108"; // Отменен
    private const string LS_DONE         = "8001000000000118"; // Выполнен
    private const string LS_SHIPPED      = "8001000000000154"; // Отгружен
    private const string LS_STORAGE      = "8001000000000122"; // Хранение
    private const string LS_RETAIL_PT    = "80010000000020B5"; // Торговая точка
    private const string LS_MOVING_TO_RP = "80010000000020B4"; // Перемещение на ТТ
    private const string LS_REG_WH       = "80010000000020B3"; // Региональный склад
    private const string LS_TRANSIT      = "C0008686474408E6"; // Транзит (строки)
    private const string LS_EN_ROUTE     = "800100000000195B"; // В пути
    private const string LS_CENTRAL_WH   = "80010000000020A9"; // Центральный склад
    private const string LS_MOVING_RS    = "80010000000034F4"; // Перемещение (РС)
    private const string LS_RS           = "80010000000034F5"; // РС
    private const string LS_SUPPLY       = "8001000000000121"; // Поставка
    private const string LS_PLANNING     = "800100000000011F"; // Планирование
    private const string LS_PROCESSING   = "8001000000001500"; // Обрабатывается
    private const string LS_FORMING      = "C0003089DD24DC9A"; // Оформляемый
    private const string LS_EXECUTING    = "C0008686474408E5"; // Исполняемый

    // ── Статусы заказа (order statusId), возвращаемые функцией DeriveOrderStatus ─
    private const string OS_ATTENTION    = "80010000000020AE"; // Внимание
    private const string OS_NO_FABRIC    = "80010000000020A4"; // Нет ткани
    private const string OS_CANCELLED    = "8010000000000108"; // Отменен
    private const string OS_DONE         = "8001000000000118"; // Выполнен
    private const string OS_SHIPPED      = "8001000000000154"; // Отгрузка завершена
    private const string OS_RETAIL_PT    = "80010000000020B5"; // Торговая точка
    private const string OS_MOVING_TO_RP = "80010000000020B4"; // Перемещение на ТТ
    private const string OS_REG_WH       = "80010000000020B3"; // Региональный склад
    private const string OS_TRANSIT      = "8001000000003E0E"; // Транзит (заказа)
    private const string OS_EN_ROUTE     = "800100000000195B"; // В пути
    private const string OS_SUPPLY       = "8001000000000121"; // Поставка
    private const string OS_PLAN_K       = "800100000000011F"; // Ковров-планирование
    private const string OS_PLAN_N       = "8001000000000120"; // Новосибирск-планирование
    private const string OS_PROC_K       = "8001000000001500"; // Обрабатывается-Ковров
    private const string OS_PROC_N       = "8001000000001501"; // Обрабатывается-Новосибирск

    // ── Накопительные наборы допустимых статусов строк (таблица из блок-схемы) ──
    // Каждый следующий набор расширяет предыдущий; проверяются сверху вниз.
    private static readonly HashSet<string> _sCancelled  = [LS_CANCELLED];
    private static readonly HashSet<string> _sDone       = [LS_CANCELLED, LS_DONE];
    private static readonly HashSet<string> _sShipped    = [LS_CANCELLED, LS_DONE, LS_SHIPPED];
    private static readonly HashSet<string> _sRetailPt   = [LS_CANCELLED, LS_DONE, LS_STORAGE, LS_RETAIL_PT];
    private static readonly HashSet<string> _sMovingToRp = [LS_CANCELLED, LS_DONE, LS_SHIPPED, LS_STORAGE, LS_RETAIL_PT, LS_MOVING_TO_RP];
    private static readonly HashSet<string> _sRegWh      = [LS_CANCELLED, LS_DONE, LS_SHIPPED, LS_STORAGE, LS_RETAIL_PT, LS_MOVING_TO_RP, LS_REG_WH];
    private static readonly HashSet<string> _sTransit    = [LS_CANCELLED, LS_DONE, LS_SHIPPED, LS_STORAGE, LS_RETAIL_PT, LS_MOVING_TO_RP, LS_REG_WH, LS_TRANSIT];
    private static readonly HashSet<string> _sEnRoute    = [LS_CANCELLED, LS_DONE, LS_SHIPPED, LS_STORAGE, LS_RETAIL_PT, LS_MOVING_TO_RP, LS_REG_WH, LS_TRANSIT, LS_EN_ROUTE];
    private static readonly HashSet<string> _sSupply     = [LS_CANCELLED, LS_DONE, LS_SHIPPED, LS_STORAGE, LS_RETAIL_PT, LS_MOVING_TO_RP, LS_REG_WH, LS_TRANSIT, LS_EN_ROUTE, LS_CENTRAL_WH, LS_MOVING_RS, LS_RS, LS_SUPPLY];
    private static readonly HashSet<string> _sPlanning   = new(_sSupply) { LS_PLANNING };
    private static readonly HashSet<string> _sProcessing = new(_sPlanning) { LS_PROCESSING, LS_FORMING, LS_EXECUTING };

    // Допустимые статусы строк для каждого статуса ЗАКАЗА.
    // Allowed — все, что может быть у строки; Defining — хотя бы одна строка должна быть в этом диапазоне.
    private static readonly Dictionary<string, (string[] Allowed, string[] Defining)> _lineRanges = new()
    {
        ["80010000000020AE"] = ([.. _sProcessing, LS_ATTENTION],                   [LS_ATTENTION]),
        ["80010000000020A4"] = ([.. _sProcessing, LS_NO_FABRIC],                   [LS_NO_FABRIC]),
        ["8010000000000108"] = (_sCancelled.ToArray(),                             [LS_CANCELLED]),
        ["8001000000000118"] = (_sDone.ToArray(),                                  [LS_DONE]),
        ["8001000000000154"] = (_sShipped.ToArray(),                               [LS_SHIPPED]),
        ["80010000000020B5"] = (_sRetailPt.ToArray(),                              [LS_STORAGE, LS_RETAIL_PT]),
        ["80010000000020B4"] = (_sMovingToRp.ToArray(),                            [LS_MOVING_TO_RP]),
        ["80010000000020B3"] = (_sRegWh.ToArray(),                                 [LS_REG_WH]),
        ["8001000000003E0E"] = (_sTransit.ToArray(),                               [LS_TRANSIT]),
        ["800100000000195B"] = (_sEnRoute.ToArray(),                               [LS_EN_ROUTE]),
        ["8001000000000121"] = (_sSupply.ToArray(),                                [LS_CENTRAL_WH, LS_MOVING_RS, LS_RS, LS_SUPPLY]),
        ["800100000000011F"] = (_sPlanning.ToArray(),                              [LS_PLANNING]),
        ["8001000000000120"] = (_sPlanning.ToArray(),                              [LS_PLANNING]),
        ["8001000000001500"] = (_sProcessing.ToArray(),                            [LS_PROCESSING]),
        ["8001000000001501"] = (_sProcessing.ToArray(),                            [LS_PROCESSING]),
        // Статусы вне DeriveOrderStatus (выставляются вручную)
        ["C0003089DD24DC9A"] = ([LS_FORMING],                                      [LS_FORMING]),
        ["C0008686474408E5"] = ([LS_FORMING, LS_EXECUTING],                        [LS_EXECUTING]),
        ["8001000000001523"] = (_sProcessing.ToArray(),                             [LS_PLANNING]),
        ["8001000000001564"] = (_sProcessing.ToArray(),                             [LS_PLANNING]),
        ["80010000000020A9"] = (_sSupply.ToArray(),                                 [LS_CENTRAL_WH]),
        ["80010000000034F4"] = (_sSupply.ToArray(),                                 [LS_MOVING_RS]),
        ["80010000000034F5"] = (_sSupply.ToArray(),                                 [LS_RS]),
        ["8001000000000122"] = (_sRetailPt.ToArray(),                               [LS_STORAGE]),
    };

    // Возвращает (Allowed[], Defining[]) для данного статуса заказа.
    // Allowed — все допустимые статусы строк; Defining — «якорные» (хотя бы одна строка должна быть в них).
    public static (string[] Allowed, string[] Defining) LineStatusRangeFor(string orderStatusId) =>
        _lineRanges.TryGetValue(orderStatusId, out var r) ? r : ([LS_DONE, LS_CANCELLED], [LS_DONE]);

    /// <summary>
    /// Выводит статус заказа из статусов его строк по правилам блок-схемы.
    /// Возвращает null, если правило определяет «статус не меняется».
    /// preferKovrov используется для выбора площадки при Планировании/Обработке.
    /// </summary>
    public static string? DeriveOrderStatus(IEnumerable<string?> lineStatusIds,
        bool preferKovrov = true)
    {
        var s = lineStatusIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToHashSet();

        if (s.Count == 0) return null;

        // 1. Приоритетные триггеры (достаточно одной строки)
        if (s.Contains(LS_ATTENTION)) return OS_ATTENTION;
        if (s.Contains(LS_NO_FABRIC)) return OS_NO_FABRIC;

        // 2. Линейная зависимость: проверяем сверху вниз (первое совпадение)
        if (s.IsSubsetOf(_sCancelled))  return OS_CANCELLED;
        if (s.IsSubsetOf(_sDone))       return OS_DONE;
        if (s.IsSubsetOf(_sShipped))    return OS_SHIPPED;
        if (s.IsSubsetOf(_sRetailPt))   return OS_RETAIL_PT;
        if (s.IsSubsetOf(_sMovingToRp)) return OS_MOVING_TO_RP;
        if (s.IsSubsetOf(_sRegWh))      return OS_REG_WH;
        if (s.IsSubsetOf(_sTransit))    return OS_TRANSIT;
        if (s.IsSubsetOf(_sEnRoute))    return OS_EN_ROUTE;
        if (s.IsSubsetOf(_sSupply))     return OS_SUPPLY;
        if (s.IsSubsetOf(_sPlanning))   return preferKovrov ? OS_PLAN_K : OS_PLAN_N;
        if (s.IsSubsetOf(_sProcessing)) return preferKovrov ? OS_PROC_K : OS_PROC_N;

        // 3. Отрицательные условия
        bool hasPlanning = s.Contains(LS_PLANNING);
        bool hasSupply   = s.Contains(LS_SUPPLY);

        if (!hasPlanning && !hasSupply) return null; // статус не меняется
        if (!hasPlanning)               return OS_SUPPLY;

        return null; // fallback
    }

    // Маппинг ORDER statusId → LINE statusId (для синхронизации строк при смене статуса заказа).
    public static string ToLineStatusId(string orderStatusId) =>
        orderStatusId switch
        {
            "8001000000003E0E" => "C0008686474408E6", // Транзит заказа → строки
            "8001000000001501" => "8001000000001500", // Обр.-Новосибирск → Обрабатывается
            "8001000000000120" => "800100000000011F", // Н-планирование → Планирование
            _                  => orderStatusId,
        };

    // ── Дополнительные константы статусов заказа ────────────────────────────────
    private const string OS_READY        = "8001000000000122"; // Готов к отгрузке
    private const string OS_WAIT_PAYMENT = "8001000000001564"; // Жду оплату
    private const string OS_EXECUTING    = "C0008686474408E5"; // Исполняемый
    private const string OS_FORMING      = "C0003089DD24DC9A"; // Оформляемый

    // Заглушки бизнес-правил — возвращают случайный boolean для тестовой генерации.
    private static class MockRules
    {
        public static bool Has3PL(Order order) =>
            !string.IsNullOrEmpty(order.Order3PLId) || Random.Shared.NextDouble() > 0.5;

        public static bool HasAlmaz()  => Random.Shared.NextDouble() > 0.5;
        public static bool IsPaid()    => Random.Shared.NextDouble() > 0.5;

    }

    /// <summary>
    /// Вычисляет статус заказа (ДО) на основе статусов его строк (спецификаций).
    /// Реализует паттерн «Каскадный Аккумулятор» (Waterfall Accumulator).
    /// Возвращает null, если статус заказа не должен меняться.
    /// </summary>
    public static string? ComputeOrderStatus(Order order, bool preferKovrov = true)
    {
        var specs = order.Specification;

        // Шаг 0: черновик — не обрабатываем.
        if (order.StatusId == OS_FORMING)
            return order.StatusId;

        // Шаг 1: восстановление из блокировки «Нет ткани».
        // Если последняя запись истории была переходом ИЗ «Нет ткани» — возвращаем «Исполняемый».
        var latest = order.StatusHistory
            .OrderByDescending(h => h.ChangeDate)
            .FirstOrDefault();
        if (latest?.OldStatusId == LS_NO_FABRIC)
            return OS_EXECUTING;

        // Шаг 2: абсолютные приоритеты (достаточно одной строки).
        if (specs.Any(s => s.StatusId == LS_ATTENTION)) return OS_ATTENTION;
        if (specs.Any(s => s.StatusId == LS_NO_FABRIC)) return OS_NO_FABRIC;

        // Шаг 3: Waterfall Accumulator.
        // Набор допустимых статусов строк накапливается на каждом этапе.
        // Условие перехода: ВСЕ строки имеют статус из allowed.
        var allowed = new HashSet<string>();
        bool EveryIn() => specs.Count > 0 && specs.All(s => s.StatusId != null && allowed.Contains(s.StatusId));

        allowed.Add(LS_CANCELLED);
        if (EveryIn()) return OS_CANCELLED;

        allowed.Add(LS_DONE);
        if (EveryIn()) return OS_DONE;

        allowed.Add(LS_SHIPPED);
        if (EveryIn()) return OS_SHIPPED;

        allowed.Add(LS_STORAGE);
        if (EveryIn())
        {
            bool has3PL = MockRules.Has3PL(order);
            bool allStorageOrService = specs.All(s => s.StatusId == LS_STORAGE || s.IsService);

            if (has3PL)
            {
                if (!allStorageOrService) return null;
                return MockRules.IsPaid() ? OS_READY : OS_WAIT_PAYMENT;
            }
            else
            {
                if (MockRules.HasAlmaz())
                    return MockRules.IsPaid() ? null : OS_WAIT_PAYMENT;
                return OS_READY;
            }
        }

        allowed.Add(LS_RETAIL_PT);
        if (EveryIn()) return OS_RETAIL_PT;

        allowed.Add(LS_MOVING_TO_RP);
        if (EveryIn()) return OS_MOVING_TO_RP;

        allowed.Add(LS_REG_WH);
        if (EveryIn()) return OS_REG_WH;

        allowed.Add(LS_TRANSIT); // C0008686474408E6 (строки) → OS_TRANSIT = 8001000000003E0E (заказ)
        if (EveryIn()) return OS_TRANSIT;

        allowed.Add(LS_EN_ROUTE);
        if (EveryIn()) return OS_EN_ROUTE;

        allowed.Add(LS_SUPPLY);
        allowed.Add(LS_CENTRAL_WH);
        allowed.Add(LS_MOVING_RS);
        allowed.Add(LS_RS);
        if (EveryIn()) return OS_SUPPLY;

        allowed.Add(LS_EXECUTING);
        if (EveryIn()) return OS_EXECUTING;

        // Планирование / Обработка — строки имеют эти статусы, но не прошли через каскад выше.
        bool hasPlanning   = specs.Any(s => s.StatusId == LS_PLANNING || s.StatusId == "8001000000001501");
        bool hasProcessing = specs.Any(s => s.StatusId == LS_PROCESSING);
        if (hasPlanning || hasProcessing)
            return preferKovrov
                ? (hasProcessing ? OS_PROC_K : OS_PLAN_K)
                : (hasProcessing ? OS_PROC_N : OS_PLAN_N);

        // Шаг 4: Fallback.
        bool hasPlanningSp = specs.Any(s => s.StatusId == LS_PLANNING);
        bool hasSupplySp   = specs.Any(s => s.StatusId == LS_SUPPLY);
        if (!hasPlanningSp && !hasSupplySp)
            return OS_PLAN_K;

        return null;
    }

}
