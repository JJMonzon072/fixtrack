using FixTrack.Application.Security;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Demo.Scenario;

/// <summary>
/// Datos ficticios de la demostración: un taller con 4 técnicos, 10 clientes, 15 equipos y 25 órdenes de los últimos
/// 75 días, en todos los estados. Las fechas se calculan a partir de <see cref="AnchorUtc"/> (el momento de la carga),
/// así que la demostración siempre luce actual; con el mismo ancla, el resultado es idéntico.
/// <para>
/// Todo está marcado como ficticio: los nombres terminan en «(demo)», los teléfonos empiezan con 0000 (no existen en
/// Guatemala), los correos usan el dominio reservado .invalid y las series empiezan con DEMO-.
/// </para>
/// </summary>
internal sealed class DemoScenario
{
    private const string CustomerNote = "Cliente ficticio de la demostración.";

    private readonly TimeZoneInfo _zone;
    private readonly DateTime _today;
    private readonly List<DemoOrder> _orders = [];

    private DemoScenario(DateTime anchorUtc, TimeZoneInfo zone)
    {
        AnchorUtc = anchorUtc;
        _zone = zone;
        _today = TimeZoneInfo.ConvertTimeFromUtc(anchorUtc, zone).Date;
    }

    /// <summary>Momento de la carga, truncado al minuto. Ninguna operación del escenario es posterior.</summary>
    public DateTime AnchorUtc { get; }

    public DemoAccount Administrator { get; } = new("Marta Solís (demo)", "admin@fixtrack-demo.invalid", Roles.Administrator);

    public DemoAccount Operator { get; } = new("Valeria Ortiz (demo)", "operador@fixtrack-demo.invalid", Roles.Operator);

    /// <summary>Cuenta de solo lectura para quien visita la demostración. Se crea al final: su existencia indica una carga completa.</summary>
    public DemoAccount ReadOnly { get; } = new("Visitante (demo)", "consulta@fixtrack-demo.invalid", Roles.ReadOnly);

    /// <summary>Alta de las cuentas del personal, antes que todo lo demás.</summary>
    public DateTime AccountsCreatedAtUtc => Day(-80, 8, 0);

    public IReadOnlyList<DemoTechnician> Technicians { get; } =
    [
        new("carla", "Carla Ruiz (demo)", "Laptops y equipos portátiles", "0000-0201", "carla.ruiz@fixtrack-demo.invalid"),
        new("luis", "Luis Pérez (demo)", "Impresoras y multifuncionales", "0000-0202", null),
        new("andrea", "Andrea Gómez (demo)", "Computadoras de escritorio y todo en uno", "0000-0203", null),
        new("diego", "Diego Alvarado (demo)", "Monitores, tabletas y periféricos", "0000-0204", null),
    ];

    public IReadOnlyList<DemoCustomer> Customers { get; } =
    [
        new("ana", "Ana Morales (demo)", "0000-0101", "ana.morales@ejemplo.invalid", CustomerNote),
        new("carlos", "Carlos Méndez (demo)", "0000-0102", null, CustomerNote),
        new("lucia", "Lucía Herrera (demo)", "0000-0103", "lucia.herrera@ejemplo.invalid", CustomerNote),
        new("grafico", "Taller Gráfico Sánchez (demo)", "0000-0104", "taller.grafico@ejemplo.invalid", CustomerNote),
        new("jose", "José Ramírez (demo)", "0000-0105", null, CustomerNote),
        new("mafer", "María Fernanda López (demo)", "0000-0106", "mf.lopez@ejemplo.invalid", CustomerNote),
        new("pino", "Distribuidora El Pino (demo)", "0000-0107", "soporte.elpino@ejemplo.invalid", CustomerNote),
        new("pedro", "Pedro Castillo (demo)", "0000-0108", null, CustomerNote),
        new("sofia", "Sofía Juárez (demo)", "0000-0109", "sofia.juarez@ejemplo.invalid", CustomerNote),
        new("colegio", "Colegio San Andrés (demo)", "0000-0110", "tecnologia.colegio@ejemplo.invalid", CustomerNote),
    ];

    public IReadOnlyList<DemoDevice> Devices { get; } =
    [
        new("ana-latitude", "ana", DeviceType.Laptop, "Dell", "Latitude 5440", "DEMO-LT-0001", null),
        new("ana-ideapad", "ana", DeviceType.Laptop, "Lenovo", "IdeaPad 3 15", "DEMO-LT-0002", null),
        new("carlos-thinkpad", "carlos", DeviceType.Laptop, "Lenovo", "ThinkPad E14 Gen 5", "DEMO-LT-0003", null),
        new("lucia-tab", "lucia", DeviceType.Tablet, "Samsung", "Galaxy Tab S9", "DEMO-TB-0001", null),
        new("grafico-epson", "grafico", DeviceType.Printer, "Epson", "EcoTank L3250", "DEMO-PR-0001", null),
        new("grafico-thinkcentre", "grafico", DeviceType.Desktop, "Lenovo", "ThinkCentre M70q", "DEMO-PC-0001", null),
        new("jose-probook", "jose", DeviceType.Laptop, "HP", "ProBook 450 G9", "DEMO-LT-0004", null),
        new("mafer-aio", "mafer", DeviceType.AllInOne, "HP", "All-in-One 24", "DEMO-AIO-0001", null),
        new("pino-laserjet", "pino", DeviceType.Printer, "HP", "LaserJet Pro M404dw", "DEMO-PR-0002", "Impresora del área de despacho."),
        new("pino-optiplex", "pino", DeviceType.Desktop, "Dell", "OptiPlex 7010", "DEMO-PC-0002", "Computadora de facturación."),
        new("pino-monitor", "pino", DeviceType.Monitor, "Samsung", "S24R350", "DEMO-MN-0001", null),
        new("pedro-vivobook", "pedro", DeviceType.Laptop, "Asus", "VivoBook 15", "DEMO-LT-0005", null),
        new("sofia-macbook", "sofia", DeviceType.Laptop, "Apple", "MacBook Air M1", "DEMO-LT-0006", null),
        new("colegio-prodesk", "colegio", DeviceType.Desktop, "HP", "ProDesk 400 G7", "DEMO-PC-0003", "Sala de profesores."),
        new("colegio-brother", "colegio", DeviceType.Printer, "Brother", "HL-L2350DW", "DEMO-PR-0003", null),
    ];

    public IReadOnlyList<DemoOrder> Orders => _orders;

    /// <summary>Escenario anclado en <paramref name="anchorUtc"/> (en la práctica, el momento de la carga).</summary>
    public static DemoScenario Create(DateTime anchorUtc, TimeZoneInfo zone)
    {
        var anchor = DateTime.SpecifyKind(anchorUtc, DateTimeKind.Utc);
        var scenario = new DemoScenario(anchor.AddTicks(-(anchor.Ticks % TimeSpan.TicksPerMinute)), zone);
        scenario.AddOrders();
        return scenario;
    }

    /// <summary>
    /// Todas las operaciones en orden cronológico: técnicos, cada cliente y cada equipo justo antes de su primera
    /// orden, las recepciones y lo que pasa con cada orden. Así los números de orden y la auditoría siguen el tiempo.
    /// </summary>
    public IReadOnlyList<DemoEvent> Timeline()
    {
        var events = new List<DemoEvent>();
        events.AddRange(Technicians.Select((technician, index) =>
            new CreateTechnicianEvent(Day(-80, 9, 0).AddMinutes(10 * index), DemoActor.Administrator, technician)));

        foreach (var customer in Customers)
        {
            var first = _orders.Where(order => DeviceOf(order).CustomerKey == customer.Key).Min(order => order.ReceivedAtUtc);
            events.Add(new CreateCustomerEvent(first.AddMinutes(-15), DemoActor.Operator, customer));
        }

        foreach (var device in Devices)
        {
            var first = _orders.Where(order => order.DeviceKey == device.Key).Min(order => order.ReceivedAtUtc);
            events.Add(new RegisterDeviceEvent(first.AddMinutes(-8), DemoActor.Operator, device));
        }

        foreach (var order in _orders)
        {
            events.Add(new ReceiveOrderEvent(order.ReceivedAtUtc, order.ReceivedBy, order));
            events.AddRange(order.Steps.Select(step => new OrderStepEvent(order, step)));
        }

        // Orden estable: a la misma hora, primero lo que se agregó antes (el cliente antes que su equipo).
        return [.. events.Select((item, index) => (item, index)).OrderBy(pair => pair.item.AtUtc).ThenBy(pair => pair.index).Select(pair => pair.item)];
    }

    public DemoDevice DeviceOf(DemoOrder order) => Devices.Single(device => device.Key == order.DeviceKey);

    /// <summary>Hora local del taller, <paramref name="days"/> días respecto de la fecha del ancla (negativo: antes).</summary>
    private DateTime Day(int days, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(_today.AddDays(days).AddHours(hour).AddMinutes(minute), _zone);

    /// <summary>Hace un rato, respecto del ancla: para lo recibido hoy, sea cual sea la hora de la carga.</summary>
    private DateTime Ago(int hours, int minutes) => AnchorUtc.AddHours(-hours).AddMinutes(-minutes);

    private OrderScript Order(
        string device,
        DateTime receivedAt,
        string issue,
        ServiceOrderPriority priority,
        string? technician,
        DateTime? promised,
        decimal? estimate,
        string? notes = null,
        DemoActor by = DemoActor.Operator) =>
        new(this, device, receivedAt, by, issue, priority, technician, promised, estimate, notes);

    private void AddOrders()
    {
        const ServiceOrderStatus Diagnosing = ServiceOrderStatus.Diagnosing;
        const ServiceOrderStatus InRepair = ServiceOrderStatus.InRepair;
        const ServiceOrderStatus AwaitingParts = ServiceOrderStatus.AwaitingParts;
        const ServiceOrderStatus Ready = ServiceOrderStatus.ReadyForPickup;
        const ServiceOrderPriority Low = ServiceOrderPriority.Low;
        const ServiceOrderPriority Normal = ServiceOrderPriority.Normal;
        const ServiceOrderPriority High = ServiceOrderPriority.High;
        const ServiceOrderPriority Urgent = ServiceOrderPriority.Urgent;

        // Entregadas: a tiempo, con atraso, con y sin diferencia entre el costo estimado y el final.
        Order("pino-laserjet", Day(-72, 9, 20), "Atasca el papel en casi todas las impresiones y muestra el error 13.A2.",
                Normal, "luis", Day(-68, 17, 0), 350m, "Se recibe sin cartucho de tóner, a pedido del cliente.")
            .Status(Day(-72, 11, 0), Diagnosing)
            .Diagnose(Day(-71, 9, 40), "Rodillo de arrastre gastado y almohadilla separadora endurecida. Se recomienda reemplazar ambas piezas y limpiar el recorrido del papel.")
            .Status(Day(-71, 10, 0), InRepair)
            .Status(Day(-69, 15, 30), Ready, "Prueba de 50 hojas sin atascos.")
            .Deliver(Day(-68, 11, 10), 350m);

        Order("ana-latitude", Day(-70, 10, 5), "No enciende; el indicador de carga parpadea en ámbar y el equipo no responde al botón de encendido.",
                High, "carla", Day(-66, 17, 0), 600m, "Se recibe con cargador original.")
            .Status(Day(-70, 14, 0), Diagnosing)
            .Diagnose(Day(-69, 10, 30), "El conector de carga de la tarjeta madre está flojo y no entrega energía. La batería y el cargador funcionan.")
            .Status(Day(-69, 10, 45), InRepair)
            .Status(Day(-69, 11, 30), AwaitingParts, "Conector de carga pedido al proveedor; entrega estimada en 5 días.")
            .Plan(Day(-69, 11, 35), High, Day(-63, 17, 0), 600m)
            .Status(Day(-64, 9, 30), InRepair, "Llegó el repuesto.")
            .Status(Day(-62, 12, 0), Ready, "Se reemplazaron el conector y el cable interno de carga.")
            .Deliver(Day(-62, 16, 30), 680m);

        Order("colegio-prodesk", Day(-67, 8, 50), "Muy lento al iniciar y se congela al abrir el navegador.",
                Normal, "andrea", Day(-63, 17, 0), 450m)
            .Status(Day(-67, 10, 30), Diagnosing)
            .Diagnose(Day(-66, 9, 15), "Disco duro con sectores reasignados. Se recomienda cambiarlo por un SSD de 480 GB y reinstalar el sistema.")
            .Status(Day(-66, 9, 30), InRepair)
            .Status(Day(-64, 15, 0), Ready)
            .Deliver(Day(-63, 9, 40), 450m);

        Order("carlos-thinkpad", Day(-64, 11, 15), "Varias teclas no responden después de derramar café sobre el teclado.",
                Normal, "carla", Day(-59, 17, 0), 500m)
            .Status(Day(-64, 15, 20), Diagnosing)
            .Diagnose(Day(-63, 10, 0), "Teclado dañado por líquido. La tarjeta madre no presenta corrosión.")
            .Status(Day(-63, 10, 10), InRepair)
            .Status(Day(-61, 14, 0), Ready, "Teclado reemplazado y probado.")
            .Deliver(Day(-60, 12, 20), 475m);

        Order("grafico-epson", Day(-60, 9, 0), "No imprime en color: las hojas salen con franjas.",
                Low, "luis", Day(-55, 17, 0), 200m)
            .Status(Day(-59, 9, 30), Diagnosing)
            .Diagnose(Day(-59, 11, 0), "Cabezal obstruido por tinta seca. Requiere limpieza profunda y purga de conductos.")
            .Status(Day(-59, 11, 10), InRepair)
            .Status(Day(-57, 10, 0), Ready)
            .Deliver(Day(-56, 15, 45), 200m);

        Order("jose-probook", Day(-57, 14, 30), "La pantalla muestra líneas verticales y parpadea al mover la tapa.",
                High, "carla", Day(-53, 17, 0), 900m)
            .Status(Day(-56, 9, 0), Diagnosing)
            .Diagnose(Day(-56, 11, 30), "Cable flex de video dañado a la altura de la bisagra; el panel funciona con un cable de prueba.")
            .Status(Day(-56, 11, 45), InRepair)
            .Status(Day(-52, 16, 0), Ready)
            .Deliver(Day(-51, 10, 30), 950m);

        Order("pino-optiplex", Day(-54, 10, 40), "Se reinicia solo varias veces al día, sobre todo con varios programas abiertos.",
                Normal, null, Day(-48, 17, 0), 600m, by: DemoActor.Administrator)
            .Assign(Day(-54, 11, 30), "andrea", null, DemoActor.Administrator)
            .Status(Day(-54, 14, 0), Diagnosing)
            .Diagnose(Day(-53, 10, 0), "Fuente de poder inestable bajo carga; voltajes fuera de rango en la prueba.")
            .Status(Day(-53, 10, 15), InRepair)
            .Status(Day(-50, 11, 0), Ready)
            .Deliver(Day(-49, 9, 30), 650m);

        Order("mafer-aio", Day(-50, 15, 10), "No da imagen; solo se escucha el ventilador al encender.",
                Normal, "diego", Day(-45, 17, 0), 400m)
            .Assign(Day(-50, 16, 0), "andrea", "Es un equipo todo en uno: lo atiende el área de computadoras de escritorio.", DemoActor.Administrator)
            .Status(Day(-49, 9, 0), Diagnosing)
            .Diagnose(Day(-49, 11, 0), "Módulo de memoria RAM defectuoso en el banco 1; el equipo arranca con el banco 2.")
            .Status(Day(-49, 11, 10), InRepair)
            .Status(Day(-47, 14, 30), Ready)
            .Deliver(Day(-46, 10, 0), 380m);

        Order("sofia-macbook", Day(-45, 9, 30), "La batería dura menos de una hora y el sistema indica que necesita servicio.",
                Normal, "carla", Day(-40, 17, 0), 1100m)
            .Status(Day(-45, 11, 0), Diagnosing)
            .Diagnose(Day(-44, 10, 0), "Batería con 61 % de capacidad y más de 1100 ciclos. Se recomienda reemplazarla.")
            .Status(Day(-44, 10, 20), InRepair)
            .Status(Day(-44, 10, 40), AwaitingParts, "Batería pedida al importador.")
            .Status(Day(-38, 9, 0), InRepair, "Llegó la batería.")
            .Status(Day(-37, 15, 0), Ready)
            .Deliver(Day(-36, 11, 0), 1100m);

        Order("colegio-brother", Day(-40, 13, 0), "Imprime hojas en blanco o con el texto muy tenue.",
                Low, "luis", null, null, "El colegio pide la revisión sin fecha de entrega.")
            .Status(Day(-39, 9, 0), Diagnosing)
            .Diagnose(Day(-39, 10, 30), "Unidad de tambor al final de su vida útil.")
            .Status(Day(-39, 10, 45), InRepair)
            .Status(Day(-37, 11, 0), Ready)
            .Deliver(Day(-36, 14, 0), 275m);

        // Cancelada desde el diagnóstico.
        Order("grafico-thinkcentre", Day(-33, 10, 0), "No enciende después de un corte de energía.",
                Normal, "andrea", Day(-28, 17, 0), 400m)
            .Status(Day(-33, 14, 0), Diagnosing)
            .Diagnose(Day(-32, 11, 0), "Tarjeta madre dañada por sobretensión. El reemplazo cuesta más que un equipo equivalente.")
            .Status(Day(-31, 10, 0), ServiceOrderStatus.Cancelled, "El cliente decidió no reparar: el costo supera el valor del equipo.");

        Order("pedro-vivobook", Day(-30, 9, 15), "Ruido fuerte en el ventilador; el equipo se calienta y se apaga.",
                Normal, "carla", Day(-26, 17, 0), 250m)
            .Status(Day(-30, 11, 0), Diagnosing)
            .Diagnose(Day(-30, 15, 0), "Ventilador obstruido y pasta térmica seca.")
            .Status(Day(-29, 9, 0), InRepair)
            .Status(Day(-28, 12, 0), Ready, "Limpieza interna y cambio de pasta térmica; temperatura normal en la prueba de carga.")
            .Deliver(Day(-27, 10, 15), 250m);

        // Reabierta antes de entregar: la prueba de entrega falló.
        Order("pino-monitor", Day(-24, 11, 30), "El monitor se apaga a los pocos minutos de encenderlo.",
                Normal, "diego", Day(-19, 17, 0), 300m)
            .Status(Day(-23, 9, 0), Diagnosing)
            .Diagnose(Day(-23, 11, 0), "Capacitores inflados en la fuente interna.")
            .Status(Day(-23, 11, 15), InRepair)
            .Status(Day(-21, 16, 0), Ready)
            .Status(Day(-20, 10, 0), InRepair, "En la prueba de entrega volvió a apagarse; se revisa otra vez la fuente.")
            .Diagnose(Day(-20, 12, 0), "Capacitores inflados en la fuente interna. En la segunda revisión se encontró además un regulador de voltaje dañado.")
            .Status(Day(-18, 15, 0), Ready)
            .Deliver(Day(-17, 10, 0), 340m);

        // Cancelada mientras esperaba el repuesto.
        Order("sofia-macbook", Day(-22, 14, 0), "Varias teclas quedan pegadas y la barra espaciadora no responde.",
                Normal, "carla", Day(-15, 17, 0), 950m)
            .Status(Day(-21, 9, 0), Diagnosing)
            .Diagnose(Day(-21, 10, 30), "Mecanismo del teclado dañado; requiere cambiar el conjunto superior de la carcasa.")
            .Status(Day(-21, 11, 0), AwaitingParts, "Conjunto superior pedido al importador.")
            .Status(Day(-14, 9, 30), ServiceOrderStatus.Cancelled, "El proveedor no confirmó fecha de entrega; la clienta retiró el equipo sin reparar.");

        Order("lucia-tab", Day(-15, 10, 0), "El puerto de carga no reconoce el cable.",
                Normal, "diego", Day(-11, 17, 0), 450m)
            .Status(Day(-15, 14, 0), Diagnosing)
            .Diagnose(Day(-14, 10, 0), "Puerto USB-C con pines doblados.")
            .Status(Day(-14, 10, 15), InRepair)
            .Status(Day(-12, 15, 0), Ready)
            .Deliver(Day(-11, 10, 0), 450m);

        // Abiertas: en espera de repuestos con la entrega vencida tras reprogramarla.
        Order("pino-laserjet", Day(-12, 9, 10), "Muestra el error 50.2 del fusor y no termina de calentar.",
                High, "luis", Day(-6, 17, 0), 850m)
            .Status(Day(-12, 10, 30), Diagnosing)
            .Diagnose(Day(-11, 9, 30), "Unidad fusora dañada; requiere reemplazo completo.")
            .Status(Day(-11, 9, 45), InRepair)
            .Status(Day(-10, 11, 0), AwaitingParts, "Unidad fusora pedida; el proveedor confirma entrega en 5 días hábiles.")
            .Plan(Day(-10, 11, 10), High, Day(-3, 17, 0), 850m);

        Order("carlos-thinkpad", Day(-9, 15, 30), "La batería no carga más allá del 60 %.",
                Normal, "carla", Day(1, 17, 0), 650m)
            .Status(Day(-8, 9, 0), Diagnosing)
            .Diagnose(Day(-8, 11, 0), "Batería degradada: 58 % de su capacidad original.")
            .Status(Day(-5, 10, 0), InRepair, "Llegó la batería de reemplazo.")
            .Status(Day(-2, 16, 0), Ready, "Se avisó al cliente por teléfono.");

        Order("pedro-vivobook", Day(-8, 10, 20), "La bisagra izquierda se rompió y la tapa no cierra.",
                Normal, "carla", Day(4, 17, 0), 700m)
            .Assign(Day(-8, 12, 0), "diego", "Carla Ruiz tiene la mayor carga de la semana; se reasigna para cumplir la fecha.", DemoActor.Administrator)
            .Status(Day(-7, 9, 30), Diagnosing)
            .Diagnose(Day(-7, 11, 0), "Bisagra izquierda rota y anclaje de la cubierta inferior quebrado.")
            .Status(Day(-7, 11, 20), AwaitingParts, "Bisagra y cubierta pedidas al proveedor.");

        Order("colegio-prodesk", Day(-6, 10, 0), "No reconoce el teclado ni el mouse USB.",
                Low, "andrea", Day(2, 17, 0), 150m)
            .Status(Day(-6, 14, 0), Diagnosing)
            .Diagnose(Day(-5, 9, 0), "Puertos USB frontales desconectados de la tarjeta madre; los traseros funcionan.")
            .Status(Day(-1, 11, 0), Ready, "Se reconectó el cable de los puertos frontales.");

        Order("jose-probook", Day(-5, 9, 40), "El touchpad no responde y el cursor se mueve solo.",
                Normal, "carla", Day(1, 17, 0), 300m)
            .Assign(Day(-5, 10, 30), "diego", "Diego Alvarado tiene disponibilidad esta semana.", DemoActor.Administrator)
            .Status(Day(-4, 9, 0), Diagnosing)
            .Diagnose(Day(-4, 10, 30), "Cable del touchpad mal conectado y controlador desactualizado.")
            .Status(Day(-4, 10, 45), InRepair);

        // Urgente y vencida: encabeza la cola de trabajo.
        Order("pino-optiplex", Day(-3, 8, 45), "No arranca el sistema: pantalla azul con el error INACCESSIBLE_BOOT_DEVICE.",
                High, "andrea", Day(-1, 17, 0), 700m, "El cliente no puede facturar sin este equipo.")
            .Plan(Day(-3, 9, 30), Urgent, Day(-1, 17, 0), 700m)
            .Status(Day(-3, 10, 0), Diagnosing)
            .Diagnose(Day(-3, 13, 0), "Unidad SSD sin respuesta. Se recupera la información y se instala un SSD nuevo.")
            .Status(Day(-2, 9, 0), InRepair);

        Order("grafico-epson", Day(-2, 11, 20), "Atasca el papel y hace un ruido metálico al tomar las hojas.",
                Normal, "luis", Day(3, 17, 0), 250m)
            .Status(Day(-2, 15, 0), Diagnosing);

        Order("colegio-brother", Day(-1, 9, 50), "Hace ruido y no toma las hojas de la bandeja.",
                High, "luis", Day(1, 17, 0), 300m)
            .Status(Day(-1, 11, 0), Diagnosing)
            .Diagnose(Day(-1, 15, 30), "Rodillo de alimentación desgastado; se confirma con la prueba de arrastre.");

        // Recibidas hoy, todavía sin técnico.
        Order("ana-ideapad", Ago(3, 10), "No se conecta a redes Wi-Fi; el adaptador no aparece en el sistema.",
            Normal, null, Day(3, 17, 0), null, "Se recibe con cargador.");

        Order("lucia-tab", Ago(1, 5), "La pantalla no responde al tacto después de una caída.",
            High, null, null, null);
    }

    /// <summary>Arma una orden y sus pasos; cada paso queda en la lista al llamarlo.</summary>
    private sealed class OrderScript
    {
        private readonly List<DemoOrderStep> _steps = [];

        public OrderScript(
            DemoScenario scenario,
            string device,
            DateTime receivedAt,
            DemoActor by,
            string issue,
            ServiceOrderPriority priority,
            string? technician,
            DateTime? promised,
            decimal? estimate,
            string? notes)
        {
            var key = $"orden-{scenario._orders.Count + 1:D2}";
            scenario._orders.Add(new DemoOrder(key, device, receivedAt, by, issue, priority, technician, promised, estimate, notes, _steps));
        }

        public OrderScript Status(DateTime at, ServiceOrderStatus status, string? note = null) =>
            Add(new StatusStep(at, DemoActor.Operator, status, note, null));

        public OrderScript Deliver(DateTime at, decimal finalCost) =>
            Add(new StatusStep(at, DemoActor.Operator, ServiceOrderStatus.Delivered, null, finalCost));

        public OrderScript Diagnose(DateTime at, string text) => Add(new DiagnosisStep(at, DemoActor.Operator, text));

        public OrderScript Plan(DateTime at, ServiceOrderPriority priority, DateTime? promised, decimal? estimate) =>
            Add(new PlanStep(at, DemoActor.Operator, priority, promised, estimate));

        public OrderScript Assign(DateTime at, string technician, string? note, DemoActor by) =>
            Add(new AssignStep(at, by, technician, note));

        private OrderScript Add(DemoOrderStep step)
        {
            _steps.Add(step);
            return this;
        }
    }
}
