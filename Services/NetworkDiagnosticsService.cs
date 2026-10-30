using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace PC_Optimizer.Services;

/// <summary>Un adaptador de red conectado, tal como lo informa Windows.</summary>
public sealed record NetworkAdapterInfo(string Name, string Description, NetworkInterfaceType Type,
    long SpeedBitsPerSecond, bool HasGateway);

public enum NetworkConnectionKind { None, Ethernet, WiFi, Mobile, Other, Multiple, Undetermined, SharedFromPhone }

/// <summary>Resultado ya redactado para el usuario: tipo de conexión activa y sus adaptadores.</summary>
public sealed record NetworkReport(NetworkConnectionKind Kind, string Headline, string Detail,
    IReadOnlyList<string> AdapterLines, string? Problem)
{
    public static NetworkReport Unavailable(string reason) => new(
        NetworkConnectionKind.Undetermined,
        "No se puede determinar el tipo de conexión",
        reason,
        Array.Empty<string>(),
        reason);
}

/// <summary>Solo lectura; detrás de una interfaz para que las pruebas usen datos simulados.</summary>
public interface INetworkDiagnosticsService
{
    NetworkReport GetReport();
}

/// <summary>
/// Detecta si la conexión activa es Wi-Fi o Ethernet con System.Net.NetworkInformation (solo
/// consulta). Ignora adaptadores virtuales (VPN, máquinas virtuales). No cambia nada.
/// </summary>
public sealed class SystemNetworkDiagnosticsService : INetworkDiagnosticsService
{
    private static readonly string[] VirtualMarkers =
    {
        "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "tap-", "tap windows", "wsl",
        "bluetooth", "loopback", "pseudo", "wintun", "wireguard", "openvpn", "vpn"
    };

    private readonly Func<IReadOnlyList<NetworkAdapterInfo>> _reader;

    public SystemNetworkDiagnosticsService(Func<IReadOnlyList<NetworkAdapterInfo>>? reader = null)
        => _reader = reader ?? Read;

    public NetworkReport GetReport()
    {
        try { return Analyze(_reader()); }
        catch (Exception ex)
        {
            return NetworkReport.Unavailable($"No se puede determinar: Windows no permitió leer los adaptadores de red ({ex.GetType().Name}).");
        }
    }

    private static readonly string[] PhoneTetheringMarkers =
    {
        "rndis", "remote ndis", "internet sharing", "tether", "apple mobile device", "usb ncm"
    };

    /// <summary>Conexión compartida desde un móvil por USB (aparece como Ethernet, pero no es un cable de red).</summary>
    public static bool IsPhoneTethering(NetworkAdapterInfo adapter)
    {
        var text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        return PhoneTetheringMarkers.Any(text.Contains);
    }

    public static bool IsVirtual(NetworkAdapterInfo adapter)
    {
        string text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        return VirtualMarkers.Any(text.Contains);
    }

    public static NetworkReport Analyze(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var physical = (adapters ?? Array.Empty<NetworkAdapterInfo>()).Where(a => !IsVirtual(a)).ToList();
        var active = physical.Where(a => a.HasGateway).ToList();
        var lines = physical.Select(Describe).ToList();

        if (active.Count == 0 && (adapters ?? Array.Empty<NetworkAdapterInfo>()).Any(a => IsVirtual(a) && a.HasGateway))
        {
            return new NetworkReport(NetworkConnectionKind.Undetermined,
                "No se puede determinar el tipo de conexión: solo hay adaptadores virtuales o VPN activos",
                "La app ignora los adaptadores virtuales (VPN, máquinas virtuales) porque no indican si sales por cable o por Wi-Fi. "
                + "Si un adaptador físico tiene la palabra «virtual» o «VPN» en su nombre, también se ignora. La app no cambia nada.",
                lines, null);
        }

        if (active.Count == 0)
        {
            return new NetworkReport(NetworkConnectionKind.None,
                "No se detectó una conexión activa a internet",
                physical.Count == 0
                    ? "Windows no muestra adaptadores de red físicos conectados. Si esperabas estar conectado, revisa el cable o el Wi-Fi. La app no cambia nada."
                    : "Hay adaptadores conectados, pero ninguno con puerta de enlace, así que no se puede saber si salen a internet. La app no cambia nada.",
                lines, null);
        }

        if (active.Count > 1)
        {
            return new NetworkReport(NetworkConnectionKind.Multiple,
                "Hay varios adaptadores activos: no se puede determinar cuál usa Windows para internet",
                "Windows decide cuál usar según su prioridad interna, y la app no puede leerla con certeza. "
                + "Si quieres una sola conexión, puedes desconectar la que no uses desde Windows. La app no cambia nada.",
                lines, null);
        }

        var adapter = active[0];
        if (IsPhoneTethering(adapter))
        {
            return new NetworkReport(NetworkConnectionKind.SharedFromPhone,
                $"Parece una conexión compartida por USB (un móvil o un módem): {Label(adapter)}",
                "Windows la trata como Ethernet, pero por el nombre del adaptador no parece un cable de red: suele ser internet compartido por un móvil o un módem USB. " +
                "Si es un móvil, usa sus datos móviles: puede variar más en velocidad y latencia y tener un límite de consumo según tu plan. " +
                "Es una deducción por el nombre del adaptador, no una comprobación. La app no cambia nada.",
                lines, null);
        }

        switch (adapter.Type)
        {
            case NetworkInterfaceType.Wireless80211:
                return new NetworkReport(NetworkConnectionKind.WiFi,
                    $"Estás conectado por Wi-Fi: {Label(adapter)}",
                    "Con Wi-Fi la estabilidad depende de la distancia al router, de las paredes y de otras redes cercanas. "
                    + "Si juegas en línea o haces videollamadas y notas cortes o saltos en el ping, probar con un cable Ethernet "
                    + "o acercarte al router puede dar una conexión más estable. No aumenta la velocidad contratada y no hay "
                    + "garantía de mejora en tu caso. La velocidad del enlace que se muestra es la negociada con el router, no "
                    + "la velocidad real de internet. La app no cambia nada.",
                    lines, null);

            case NetworkInterfaceType.Ethernet:
            case NetworkInterfaceType.GigabitEthernet:
            case NetworkInterfaceType.FastEthernetT:
            case NetworkInterfaceType.FastEthernetFx:
            case NetworkInterfaceType.Ethernet3Megabit:
                return new NetworkReport(NetworkConnectionKind.Ethernet,
                    $"Estás conectado por cable (Ethernet): {Label(adapter)}",
                    "Una conexión por cable suele ser más estable que el Wi-Fi (menos variación en el ping y menos pérdida de "
                    + "paquetes). No cambia la velocidad de tu plan de internet. No hay nada que ajustar. La app no cambia nada.",
                    lines, null);

            case NetworkInterfaceType.Wwanpp:
            case NetworkInterfaceType.Wwanpp2:
                return new NetworkReport(NetworkConnectionKind.Mobile,
                    $"Estás conectado por datos móviles: {Label(adapter)}",
                    "Los datos móviles suelen variar más en latencia y pueden tener límite de consumo según tu plan. "
                    + "La app no cambia nada.",
                    lines, null);

            default:
                return new NetworkReport(NetworkConnectionKind.Other,
                    $"Conexión activa de un tipo no reconocido ({adapter.Type}): {Label(adapter)}",
                    "No se puede determinar si es por cable o inalámbrica. La app no cambia nada.",
                    lines, null);
        }
    }

    /// <summary>
    /// Solo la descripción del controlador: el nombre del adaptador lo puede escribir el usuario
    /// (por ejemplo con el nombre de su red) y no se muestra.
    /// </summary>
    private static string Label(NetworkAdapterInfo a)
        => string.IsNullOrWhiteSpace(a.Description) ? "adaptador de red" : a.Description.Trim();

    private static string Describe(NetworkAdapterInfo a)
    {
        string kind = IsPhoneTethering(a) ? "Compartido por USB (móvil o módem)" : a.Type switch
        {
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "Ethernet",
            NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Datos móviles",
            _ => $"Tipo {a.Type}"
        };
        string speed = a.SpeedBitsPerSecond > 0 && a.SpeedBitsPerSecond < long.MaxValue / 2
            ? $"{a.SpeedBitsPerSecond / 1_000_000.0:0.#} Mbps de enlace"
            : "velocidad de enlace no disponible";
        string gateway = a.HasGateway ? "con puerta de enlace" : "sin puerta de enlace";
        return $"{Label(a)} · {kind} · {speed} · {gateway}";
    }

    private static IReadOnlyList<NetworkAdapterInfo> Read()
    {
        var result = new List<NetworkAdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            bool gateway = false;
            long speed = 0;
            try
            {
                gateway = nic.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address != null && !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
            }
            catch { /* sin propiedades IP: se trata como sin puerta de enlace */ }
            try { speed = nic.Speed; } catch { speed = 0; }

            result.Add(new NetworkAdapterInfo(nic.Name, nic.Description, nic.NetworkInterfaceType, speed, gateway));
        }
        return result;
    }
}
