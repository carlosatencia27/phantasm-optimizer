using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using PC_Optimizer.Models;

namespace PC_Optimizer.Services;

/// <summary>
/// Fuente de las medidas reales de «Antes y después». Detrás de una interfaz para que las pruebas
/// usen proveedores simulados y nunca lean el equipo ni hagan consultas de red.
/// </summary>
public interface IMeasurementProvider
{
    SystemMetrics GetMetrics();
    int GetProcessCount();
    long? GetFreeSpaceOnSystemDrive();
    HiberfilInfo GetHiberfil();
    int? GetEnabledStartupPrograms();
    double? GetLastBootTimeSeconds();

    /// <summary>
    /// Último arranque con el motivo cuando no se pudo medir. Por defecto usa el método anterior
    /// (los proveedores simulados existentes siguen funcionando).
    /// </summary>
    BootTimeReading GetLastBootTime()
    {
        var seconds = GetLastBootTimeSeconds();
        return seconds.HasValue ? new BootTimeReading(seconds, null) : new BootTimeReading(null, "no disponible");
    }

    string[] GetDnsServers();
    DnsProbeResult ProbeDns(string[] domains, int queriesPerDomain);
    long? PingMs(string host, int timeoutMilliseconds);
}

/// <summary>Estado de hiberfil.sys: si existe y cuánto ocupa. Available=false → no se pudo leer.</summary>
public sealed record HiberfilInfo(bool Available, bool Exists, long Bytes);

/// <summary>Mediana del tiempo de resolución DNS y servidores usados. Available=false → no disponible.</summary>
public sealed record DnsProbeResult(bool Available, double MedianMs, string ServersUsed, int SuccessfulQueries,
    string? Reason = null, int DiscardedQueries = 0, DnsMeasureMethod Method = DnsMeasureMethod.KnownNames);

/// <summary>Cómo se midió el DNS: solo se pueden comparar dos medidas hechas de la misma forma.</summary>
public enum DnsMeasureMethod
{
    None,
    /// <summary>Dominios conocidos (el servidor puede contestar desde su caché).</summary>
    KnownNames,
    /// <summary>Nombres inventados y distintos en cada consulta (el servidor tiene que consultar hacia afuera).</summary>
    UniqueNames
}

/// <summary>
/// Decide si una tanda de consultas con nombres únicos es fiable. Cualquier respuesta alterada (una
/// dirección para un nombre que no existe) o un servidor que contesta sin consultar hacia afuera hace
/// que la medida sea «no medible» con el motivo; nunca se inventa ni se muestra «0 ms».
/// </summary>
public static class UniqueNameAnalyzer
{
    public const int MinValidSamples = 3;

    public static DnsProbeResult Summarize(IReadOnlyList<UniqueNameSample> samples, string servers, string server)
    {
        const DnsMeasureMethod method = DnsMeasureMethod.UniqueNames;
        samples ??= Array.Empty<UniqueNameSample>();
        var total = samples.Count;
        var timeouts = samples.Count(s => s.Kind == DnsReplyKind.Timeout);
        var rewritten = samples.Count(s => s.Kind == DnsReplyKind.Rewritten);
        var others = samples.Count(s => s.Kind == DnsReplyKind.OtherCode);
        var valid = samples.Where(s => s.Kind == DnsReplyKind.NameNotFound).Select(s => s.Ms).OrderBy(v => v).ToList();

        if (total == 0 || timeouts == total)
            return new DnsProbeResult(false, -1, servers, 0,
                $"el servidor DNS {server} no respondió a las consultas (sin conexión, bloqueo de la red o servidor que no responde).",
                0, method);

        if (rewritten > 0)
            return new DnsProbeResult(false, -1, servers, valid.Count,
                $"el servidor DNS {server} (o tu operador de internet) devuelve una dirección para nombres que no existen en lugar de «no existe». " +
                "Esa respuesta alterada hace que la medición no sea fiable.", rewritten, method);

        if (others * 2 >= total)
            return new DnsProbeResult(false, -1, servers, valid.Count,
                $"el servidor DNS {server} dio respuestas no esperadas (errores, rechazos o respuestas vacías) a los nombres de prueba, así que no se puede medir su tiempo de resolución.",
                others, method);

        if (valid.Count < MinValidSamples)
            return new DnsProbeResult(false, -1, servers, valid.Count,
                $"solo {valid.Count} de {total} consultas dieron una medida fiable (las demás no respondieron o fallaron).",
                total - valid.Count, method);

        var middle = valid.Count / 2;
        var median = valid.Count % 2 == 1 ? valid[middle] : (valid[middle - 1] + valid[middle]) / 2.0;
        if (median < DnsProbeAnalyzer.MinPlausibleNetworkMs)
            return new DnsProbeResult(false, -1, servers, valid.Count,
                $"el servidor DNS {server} contestó en menos de 1 ms a nombres inventados: parece que no consulta hacia afuera (lo normal de un router o un filtro local que responde por su cuenta), así que no mide la resolución real.",
                0, method);

        return new DnsProbeResult(true, median, servers, valid.Count, null, total - valid.Count, method);
    }
}

/// <summary>Último arranque en segundos o, si no hay dato, el motivo en lenguaje simple.</summary>
public sealed record BootTimeReading(double? Seconds, string? Reason);

/// <summary>
/// Decide si las consultas DNS medidas son fiables. Una respuesta de red real tarda al menos
/// ~1 ms; por debajo es casi seguro la caché local y se descarta. Nunca devuelve «0 ms».
/// </summary>
public static class DnsProbeAnalyzer
{
    public const double MinPlausibleNetworkMs = 1.0;
    public const int MinValidQueries = 3;

    public static DnsProbeResult Summarize(IReadOnlyList<double> times, string servers, int attempted)
    {
        times ??= Array.Empty<double>();
        if (times.Count == 0)
            return new DnsProbeResult(false, -1, servers, 0,
                "ninguna consulta DNS obtuvo respuesta (sin conexión, servidor DNS que no responde o red bloqueada).");

        var valid = times.Where(t => t >= MinPlausibleNetworkMs).ToList();
        var discarded = times.Count - valid.Count;

        if (valid.Count == 0)
            return new DnsProbeResult(false, -1, servers, 0,
                "todas las respuestas llegaron en menos de 1 ms, lo normal de una respuesta guardada en memoria (la caché local de Windows o la de un router o servidor DNS de tu red) y no de una consulta a Internet. Con un DNS muy cercano puede ser legítimo, pero la app no puede distinguirlo.",
                discarded);

        if (valid.Count < MinValidQueries)
            return new DnsProbeResult(false, -1, servers, valid.Count,
                $"solo {valid.Count} de {Math.Max(attempted, times.Count)} consultas dieron una medida fiable (las demás parecían servidas desde la caché local o fallaron).",
                discarded);

        var ordered = valid.OrderBy(v => v).ToList();
        var middle = ordered.Count / 2;
        var median = ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2.0;
        return new DnsProbeResult(true, median, servers, valid.Count, null, discarded);
    }

    /// <summary>
    /// Resultado de consultar DIRECTAMENTE al servidor DNS (sin pasar por el resolver de Windows, que
    /// tiene caché). null = ninguna consulta obtuvo respuesta. Nunca devuelve «0 ms».
    /// </summary>
    public static DnsProbeResult SummarizeDirect(double? medianMs, string servers, string server, int attempted)
    {
        if (!medianMs.HasValue)
            return new DnsProbeResult(false, -1, servers, 0,
                $"el servidor DNS {server} no respondió a las consultas directas (sin conexión, bloqueo de la red o servidor que no responde).");

        if (medianMs.Value < MinPlausibleNetworkMs)
            return new DnsProbeResult(false, -1, servers, attempted,
                $"el servidor DNS {server} respondió en menos de 1 ms: es un servidor dentro de tu propia red (por ejemplo el router) y la app no puede separar su velocidad de la de una respuesta guardada en memoria.");

        return new DnsProbeResult(true, medianMs.Value, servers, attempted);
    }

    /// <summary>Elige el servidor DNS IPv4 que se medirá: primero los configurados a mano, si no los del adaptador activo.</summary>
    public static string? PickServer(IEnumerable<string>? configured, IEnumerable<string>? fromAdapter)
    {
        // Solo direcciones IPv4 que pueden ser un servidor DNS real: no 0.x.x.x, loopback, multicast ni broadcast.
        static bool IsIpv4(string s)
        {
            if (!System.Net.IPAddress.TryParse(s, out var ip)
                || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] != 0 && b[0] != 127 && b[0] < 224;
        }

        return (configured ?? Array.Empty<string>()).FirstOrDefault(IsIpv4)
            ?? (fromAdapter ?? Array.Empty<string>()).FirstOrDefault(IsIpv4);
    }
}

/// <summary>Implementación real: contadores, unidades, registro de eventos y consultas DNS sin caché.</summary>
public sealed class SystemMeasurementProvider : IMeasurementProvider
{
    public const string BootLogName = "Microsoft-Windows-Diagnostics-Performance/Operational";
    public const int BootTimeEventId = 100;

    /// <summary>Dominios conocidos para medir la resolución (2 consultas por dominio = 10).</summary>
    public static readonly string[] DefaultDnsDomains =
    {
        "google.com", "microsoft.com", "wikipedia.org", "cloudflare.com", "github.com"
    };

    // DNS_QUERY_BYPASS_CACHE = 0x8 (tabla «DNS constants» de Microsoft Learn). 0x2 es DNS_QUERY_USE_TCP_ONLY:
    // con ese valor las consultas salían de la caché local (~0 ms).
    private const uint DnsQueryBypassCache = 0x00000008;
    private const ushort DnsTypeA = 1;
    private const int DnsFreeRecordList = 1;

    private readonly ISystemInfoService _systemInfoService = new SystemInfoService();
    private readonly StartupProgramService _startupProgramService = new StartupProgramService();
    private readonly DnsService _dnsService = new DnsService();
    private readonly IUniqueNameDnsProbe _uniqueProbe;

    /// <summary>
    /// Zona sobre la que se inventan los nombres. Debe ser neutral: sus servidores autoritativos no pueden
    /// pertenecer a un proveedor de DNS público, o ese proveedor saldría favorecido. example.com (y
    /// ietf.org, rfc-editor.org) está alojado hoy en los servidores de Cloudflare: medido con 1.1.1.1
    /// salía ~5 veces más rápido que con 8.8.8.8 solo por cercanía. iana.org lo operan ICANN/IANA y
    /// contesta un «no existe» (NXDOMAIN) real para nombres inventados. Declaración de límites: son 6
    /// consultas por medición a servidores de un tercero (carga mínima, pasan por el servidor DNS del usuario)
    /// y se mide la resolución «en frío», no el tiempo habitual con caché.
    /// </summary>
    public const string UniqueNamesBaseDomain = "iana.org";

    /// <summary>Pocas consultas (6) para no molestar al servidor DNS.</summary>
    public const int UniqueNameQueries = 6;

    public SystemMeasurementProvider(IUniqueNameDnsProbe? uniqueProbe = null)
        => _uniqueProbe = uniqueProbe ?? new UdpDnsLatencyProbe();

    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DnsQuery(string name, ushort type, uint options, IntPtr extra, out IntPtr results, IntPtr reserved);

    [DllImport("dnsapi.dll")]
    private static extern void DnsRecordListFree(IntPtr recordList, int freeType);

    private bool _nativeDnsUnavailable;

    public SystemMetrics GetMetrics()
    {
        try { return _systemInfoService.GetCurrentMetrics(); }
        catch { return new SystemMetrics(); }
    }

    public int GetProcessCount()
    {
        try
        {
            var processes = Process.GetProcesses();
            int count = processes.Length;
            foreach (var process in processes) process.Dispose();
            return count;
        }
        catch { return 0; }
    }

    public long? GetFreeSpaceOnSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return null; }
    }

    public HiberfilInfo GetHiberfil()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return new HiberfilInfo(false, false, 0);
            var info = new FileInfo(Path.Combine(root, "hiberfil.sys"));
            if (!info.Exists) return new HiberfilInfo(true, false, 0);
            return new HiberfilInfo(true, true, info.Length);
        }
        catch { return new HiberfilInfo(false, false, 0); }
    }

    public int? GetEnabledStartupPrograms()
    {
        try { return _startupProgramService.GetStartupPrograms().Count(p => p.Enabled); }
        catch { return null; }
    }

    /// <summary>
    /// Último arranque de Windows: evento 100 (BootTime) del registro
    /// Microsoft-Windows-Diagnostics-Performance/Operational, en modo solo lectura.
    /// </summary>
    public double? GetLastBootTimeSeconds() => GetLastBootTime().Seconds;

    /// <summary>Lee el último evento 100 y, si no hay dato, devuelve el motivo real (permiso, sin eventos...).</summary>
    public BootTimeReading GetLastBootTime()
    {
        var history = new SystemBootHistoryService().GetLastBoots(1);
        if (history.Entries.Count > 0)
            return new BootTimeReading(history.Entries[0].Seconds, null);

        var reason = history.Problem ?? "no disponible";
        return new BootTimeReading(null, reason.StartsWith("No disponible: ", StringComparison.Ordinal)
            ? reason["No disponible: ".Length..] : reason);
    }

    public string[] GetDnsServers()
    {
        try { return _dnsService.GetDnsServers() ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    public DnsProbeResult ProbeDns(string[] domains, int queriesPerDomain)
    {
        var servers = DescribeServers();

        // Camino principal: consulta directa al servidor DNS en uso con nombres nuevos e inexistentes en cada
        // intento (etiqueta aleatoria bajo iana.org). Ni la caché de Windows ni la del propio servidor o
        // router pueden contestarlos, así que se mide lo que de verdad tarda en consultar hacia afuera.
        var server = DnsProbeAnalyzer.PickServer(SafeConfiguredServers(), AdapterDnsServers());
        if (server != null)
        {
            try
            {
                var samples = _uniqueProbe.Probe(server, UniqueNamesBaseDomain, UniqueNameQueries, 2500,
                    System.Threading.CancellationToken.None);
                return UniqueNameAnalyzer.Summarize(samples, servers, server);
            }
            catch
            {
                // Si el camino directo falla, se usa el respaldo del resolver con filtro de caché.
            }
        }

        var times = new List<double>();

        try
        {
            foreach (var domain in domains)
            {
                for (var i = 0; i < Math.Max(1, queriesPerDomain); i++)
                {
                    if (_nativeDnsUnavailable)
                    {
                        var fallback = ProbeRandomSubdomain(domain);
                        if (fallback.HasValue) times.Add(fallback.Value);
                        continue;
                    }

                    if (TryResolveUncached(domain, out var ms))
                        times.Add(ms);
                    else if (_nativeDnsUnavailable)
                    {
                        var fallback = ProbeRandomSubdomain(domain);
                        if (fallback.HasValue) times.Add(fallback.Value);
                    }
                }
            }
        }
        catch { }

        return DnsProbeAnalyzer.Summarize(times, servers, domains.Length * Math.Max(1, queriesPerDomain));
    }

    public long? PingMs(string host, int timeoutMilliseconds)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(host, timeoutMilliseconds);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch { return null; }
    }

    private string[] SafeConfiguredServers()
    {
        try { return _dnsService.GetDnsServers() ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>DNS IPv4 del adaptador físico con puerta de enlace (cubre DHCP, donde no hay servidores «a mano»).</summary>
    private static string[] AdapterDnsServers()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(IPAddress.Any))) continue;
                var text = $"{nic.Name} {nic.Description}".ToLowerInvariant();
                if (new[] { "virtual", "vmware", "hyper-v", "vethernet", "tap-", "tap windows", "vpn", "wireguard", "bluetooth" }.Any(text.Contains)) continue;
                var dns = props.DnsAddresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString()).ToArray();
                if (dns.Length > 0) return dns;
            }
        }
        catch { }
        return Array.Empty<string>();
    }

    private string DescribeServers()
    {
        try
        {
            var servers = _dnsService.GetDnsServers();
            return servers is { Length: > 0 } ? string.Join(", ", servers) : "Automático (DHCP)";
        }
        catch { return "no disponible"; }
    }

    /// <summary>Consulta A con DnsQuery y DNS_QUERY_BYPASS_CACHE (no usa la caché del resolver).</summary>
    private bool TryResolveUncached(string domain, out double elapsedMs)
    {
        elapsedMs = 0;
        try
        {
            var watch = Stopwatch.StartNew();
            var status = DnsQuery(domain, DnsTypeA, DnsQueryBypassCache, IntPtr.Zero, out var results, IntPtr.Zero);
            watch.Stop();

            if (results != IntPtr.Zero) DnsRecordListFree(results, DnsFreeRecordList);
            if (status != 0) return false;

            elapsedMs = watch.Elapsed.TotalMilliseconds;
            return true;
        }
        catch (EntryPointNotFoundException) { _nativeDnsUnavailable = true; return false; }
        catch (DllNotFoundException) { _nativeDnsUnavailable = true; return false; }
        catch { return false; }
    }

    /// <summary>Alternativa acordada: subdominio aleatorio inexistente, nunca servido desde caché.</summary>
    private static double? ProbeRandomSubdomain(string domain)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            try { _ = Dns.GetHostAddresses($"{Guid.NewGuid():N}.{domain}"); }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException || ex is ArgumentException) { }
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds;
        }
        catch { return null; }
    }

    private static double Median(List<double> values)
    {
        var ordered = values.OrderBy(v => v).ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2.0;
    }
}
