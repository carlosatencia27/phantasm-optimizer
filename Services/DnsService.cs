using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Net;

namespace PC_Optimizer.Services;

public interface IDnsService
{
    string[] GetDnsServers();
    bool SetDnsServers(string[] servers);
    bool SetDhcp();
    string GetAdapterName();
    bool LastOperationRequiresRestart { get; }
    string LastOperationMessage { get; }
    bool IsUsingDhcp();
}

public sealed record DnsAdapterInfo(string Id, string Name, bool IpEnabled, string[]? DefaultGateways, string[]? DnsServers,
    bool IsConnected = true, bool IsExcluded = false, bool DhcpEnabled = false, int? InterfaceIndex = null);

public sealed record DnsProviderResult(bool Success, uint? ReturnCode, bool RequiresRestart, bool UsedFallback, string Diagnostic);

/// <summary>Frontera WMI reemplazable para que las pruebas nunca consulten ni modifiquen Windows.</summary>
public interface IDnsAdapterProvider
{
    IReadOnlyList<DnsAdapterInfo> GetAdapters();
    DnsProviderResult SetDnsServers(string adapterId, string adapterName, string[]? servers);
}

public class DnsService : IDnsService
{
    private readonly IDnsAdapterProvider _provider;

    public DnsService(IDnsAdapterProvider? provider = null) => _provider = provider ?? new WmiDnsAdapterProvider();

    public bool LastOperationRequiresRestart { get; private set; }
    public string LastOperationMessage { get; private set; } = string.Empty;
    public bool IsUsingDhcp() => GetPrimaryAdapter()?.DhcpEnabled ?? false;

    private DnsAdapterInfo? GetPrimaryAdapter()
    {
        try
        {
            var candidates = _provider.GetAdapters()
                .Where(a => a.IpEnabled && a.IsConnected && !a.IsExcluded && a.DefaultGateways is { Length: > 0 } &&
                    a.DefaultGateways.Any(g => !string.IsNullOrWhiteSpace(g)))
                .ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }
        catch (Exception ex) { LastOperationMessage = $"No se pudieron leer los adaptadores: {PC_Optimizer.Services.UserErrorText.Of(ex)}"; return null; }
    }

    public string[] GetDnsServers() => GetPrimaryAdapter()?.DnsServers ?? Array.Empty<string>();

    public string GetAdapterName() => GetPrimaryAdapter()?.Name ?? string.Empty;

    public bool SetDnsServers(string[] servers) => SetOnPrimary(servers);

    public bool SetDhcp() => SetOnPrimary(null);

    private bool SetOnPrimary(string[]? servers)
    {
        LastOperationRequiresRestart = false;
        LastOperationMessage = string.Empty;
        var adapter = GetPrimaryAdapter();
        if (adapter == null)
        {
            LastOperationMessage = "No se identificó un único adaptador con IP y puerta de enlace predeterminada.";
            return false;
        }
        try
        {
            var result = _provider.SetDnsServers(adapter.Id, adapter.Name, servers);
            LastOperationRequiresRestart = result.RequiresRestart;
            var codeText = result.ReturnCode.HasValue ? $"Código WMI {result.ReturnCode}: {DescribeWmiCode(result.ReturnCode.Value)}." : string.Empty;
            LastOperationMessage = $"Adaptador: {adapter.Name}; DNS enviados: {(servers == null ? "DHCP" : string.Join(", ", servers))}; {codeText} {result.Diagnostic}".Trim();
            return result.Success;
        }
        catch (Exception ex) { LastOperationMessage = $"Adaptador: {adapter.Name}; error WMI: {ex}"; return false; }
    }

    private static string DescribeWmiCode(uint code) => code switch
    {
        0 => "operación completada",
        1 => "operación completada; se requiere reiniciar",
        64 => "método no compatible con este adaptador",
        65 => "fallo interno al configurar el adaptador",
        66 => "máscara de subred no válida",
        67 => "falló el procesamiento de la instancia del adaptador",
        68 => "parámetro de entrada no válido",
        69 => "se especificaron más de cinco puertas de enlace",
        70 => "dirección IP no válida",
        71 => "dirección IP de puerta de enlace no válida",
        72 => "no se pudo leer la configuración solicitada del registro",
        73 => "nombre de dominio no válido",
        74 => "nombre de host no válido",
        75 => "no hay servidores WINS definidos",
        76 => "archivo no válido",
        77 => "ruta de sistema no válida",
        78 => "falló la copia de archivos de configuración",
        79 => "parámetro de seguridad no válido",
        80 => "no se pudo configurar el servicio TCP/IP",
        81 => "no se pudo configurar el servicio DHCP",
        82 => "no se pudo renovar la concesión DHCP",
        83 => "no se pudo liberar la concesión DHCP",
        84 => "IP no está habilitado en el adaptador",
        91 => "Windows denegó el acceso; compruebe permisos de administrador y directivas del equipo",
        _ => "Windows rechazó el cambio; revise permisos y configuración del adaptador"
    };
}

public sealed class WmiDnsAdapterProvider : IDnsAdapterProvider
{
    private const string Query = "SELECT SettingID, Description, Index, InterfaceIndex, IPEnabled, DHCPEnabled, DefaultIPGateway, DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE";
    private readonly IDnsCommandRunner _commandRunner;

    public WmiDnsAdapterProvider(IDnsCommandRunner? commandRunner = null) => _commandRunner = commandRunner ?? new ProcessDnsCommandRunner();

    public IReadOnlyList<DnsAdapterInfo> GetAdapters()
    {
        var adapters = new List<DnsAdapterInfo>();
        using var searcher = new ManagementObjectSearcher(Query);
        foreach (ManagementObject obj in searcher.Get())
        {
            var interfaceIndex = Convert.ToInt32(obj["InterfaceIndex"]);
            var networkInterface = FindNetworkInterface(interfaceIndex);
            var description = Convert.ToString(obj["Description"]) ?? string.Empty;
            adapters.Add(new DnsAdapterInfo(
                Convert.ToString(obj["Index"]) ?? string.Empty,
                networkInterface?.Name ?? description,
                Convert.ToBoolean(obj["IPEnabled"]),
                obj["DefaultIPGateway"] as string[],
                obj["DNSServerSearchOrder"] as string[],
                networkInterface?.OperationalStatus == OperationalStatus.Up,
                IsExcluded(networkInterface, description),
                Convert.ToBoolean(obj["DHCPEnabled"] ?? false),
                interfaceIndex));
        }
        return adapters;
    }

    private static NetworkInterface? FindNetworkInterface(int interfaceIndex)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
            {
                try { return n.GetIPProperties().GetIPv4Properties().Index == interfaceIndex; }
                catch { return false; }
            });
        }
        catch { return null; }
    }

    private static bool IsExcluded(NetworkInterface? networkInterface, string description)
    {
        if (networkInterface?.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return true;
        var identity = $"{networkInterface?.Name} {description}";
        return identity.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("TAP", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("VPN", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("WireGuard", StringComparison.OrdinalIgnoreCase);
    }

    public DnsProviderResult SetDnsServers(string adapterId, string adapterName, string[]? servers)
    {
        var query = $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE AND Index = {ParseWmiIndex(adapterId)}";
        var dnsServers = servers?.ToArray(); // fuerza System.String[] para el parámetro CIM
        if (dnsServers != null && (dnsServers.Length == 0 || dnsServers.Any(ip => !IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)))
            return new DnsProviderResult(false, 68, false, false, $"Parámetro DNSServerSearchOrder inválido: tipo System.String[]; valores [{string.Join(", ", dnsServers)}].");

        string parameterDescription = dnsServers == null
            ? "null (DHCP)"
            : $"System.String[] [{string.Join(", ", dnsServers)}]";
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2", query);
            using var matches = searcher.Get();
            var instances = matches.Cast<ManagementObject>().ToList();
            if (instances.Count != 1)
                return new DnsProviderResult(false, null, false, false, $"Consulta WMI: {query}; coincidencias: {instances.Count}; parámetro DNSServerSearchOrder: {parameterDescription}.");

            using var adapter = instances[0];
            adapter.Get(); // enlaza explícitamente el ManagementObject a la instancia real antes de invocar el método
            using var input = adapter.GetMethodParameters("SetDNSServerSearchOrder");
            input["DNSServerSearchOrder"] = dnsServers;
            using var output = adapter.InvokeMethod("SetDNSServerSearchOrder", input, null);
            if (output == null)
                throw new InvalidOperationException("WMI no devolvió ReturnValue al invocar la instancia del adaptador.");
            var returnCode = Convert.ToUInt32(output["ReturnValue"]);
            var diagnostic = $"Consulta: {query}; Index={adapter["Index"]}; InterfaceIndex={adapter["InterfaceIndex"]}; parámetro DNSServerSearchOrder: {parameterDescription}.";
            if (returnCode is 0 or 1)
                return new DnsProviderResult(true, returnCode, returnCode == 1, false, diagnostic);

            var fallback = TryNetsh(adapterName, dnsServers);
            return fallback.Success
                ? new DnsProviderResult(true, returnCode, false, true, $"{diagnostic} WMI rechazó el cambio; respaldo netsh completado. {fallback.Diagnostic}")
                : new DnsProviderResult(false, returnCode, false, false, $"{diagnostic} Respaldo netsh falló: {fallback.Diagnostic}");
        }
        catch (Exception ex)
        {
            var fallback = TryNetsh(adapterName, dnsServers);
            var diagnostic = $"Consulta: {query}; parámetro DNSServerSearchOrder: {parameterDescription}; excepción WMI completa: {ex}.";
            return fallback.Success
                ? new DnsProviderResult(true, null, false, true, $"{diagnostic} Respaldo netsh completado. {fallback.Diagnostic}")
                : new DnsProviderResult(false, null, false, false, $"{diagnostic} Respaldo netsh falló: {fallback.Diagnostic}");
        }
    }

    private static int ParseWmiIndex(string adapterId) => int.TryParse(adapterId, out var index) && index > 0
        ? index : throw new InvalidOperationException($"Index WMI del adaptador inválido: '{adapterId}'.");

    private (bool Success, string Diagnostic) TryNetsh(string adapterName, string[]? servers)
    {
        if (string.IsNullOrWhiteSpace(adapterName)) return (false, "No hay nombre de interfaz para netsh.");
        var args = new List<string> { "interface", "ip", "set", "dns", $"name={adapterName}" };
        if (servers == null)
        {
            args.Add("dhcp");
            return _commandRunner.Run(args);
        }

        args.Add("static");
        args.Add(servers[0]);
        var first = _commandRunner.Run(args);
        if (!first.Success) return first;
        for (var i = 1; i < servers.Length; i++)
        {
            var addArgs = new List<string> { "interface", "ip", "add", "dns", $"name={adapterName}", servers[i], $"index={i + 1}" };
            var added = _commandRunner.Run(addArgs);
            if (!added.Success) return added;
        }
        return (true, "netsh configuró la lista DNS solicitada.");
    }
}

public interface IDnsCommandRunner
{
    (bool Success, string Diagnostic) Run(IReadOnlyList<string> arguments);
}

public sealed class ProcessDnsCommandRunner : IDnsCommandRunner
{
    public (bool Success, string Diagnostic) Run(IReadOnlyList<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo("netsh.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar netsh.exe.");
            if (!process.WaitForExit(10000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (false, "netsh excedió el límite de 10 segundos.");
            }
            return process.ExitCode == 0
                ? (true, $"netsh finalizó con código {process.ExitCode}.")
                : (false, $"netsh finalizó con código {process.ExitCode}.");
        }
        catch (Exception ex) { return (false, $"No se pudo ejecutar netsh: {ex}"); }
    }
}
