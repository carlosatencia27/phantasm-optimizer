using System;
using System.Collections.Generic;
using System.Linq;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class DnsSetting : ISetting, IApplyVerification
{
    private readonly IDnsService _dnsService;
    private readonly IDnsCacheService? _cacheService;
    private readonly IActivityLogger _logger;
    private readonly IDeviceInfoService? _device;
    private readonly string _baseDescription;
    private bool? _isLaptop;

    public const string OptionDhcp = "Automático (DHCP)";
    public const string OptionCloudflare = "Cloudflare (1.1.1.1 y 1.0.0.1)";
    public const string OptionGoogle = "Google (8.8.8.8 y 8.8.4.4)";

    public static readonly string[] AvailableOptions =
    {
        OptionDhcp,
        OptionCloudflare,
        OptionGoogle
    };

    public DnsSetting(IDnsService dnsService, IDnsCacheService? cacheService = null,
        IActivityLogger? activityLogger = null, IDeviceInfoService? deviceInfo = null)
    {
        _device = deviceInfo;
        _dnsService = dnsService;
        _cacheService = cacheService;
        _logger = activityLogger ?? new NullActivityLogger();
        Key = "Network.Dns";
        Name = "Usar los servidores DNS de Cloudflare";
        _baseDescription = "Activado: usa los servidores DNS de Cloudflare (1.1.1.1 y 1.0.0.1) en el adaptador principal (tus consultas de nombres irán a un servicio externo). Al revertir, restaura los servidores anteriores o vuelve a obtenerlos por DHCP.";
        RealisticEffect = "Cambia los servidores DNS, que traducen nombres de páginas en direcciones. El efecto depende de tu red: la resolución puede ser más rápida (se puede comprobar en «Antes y después»), igual o más lenta. No aumenta el ancho de banda ni cambia el ping dentro de los juegos. Cloudflare es un servicio externo: tus consultas de nombres irán a sus servidores en lugar de a los que te asigna tu red.";
        RiskLevel = SettingRiskLevel.Safe;
        IsWindowsVersionSupported = true;
    }

    public string Key { get; }
    public string Name { get; }
    private bool IsLaptop
    {
        get
        {
            if (_isLaptop == null)
            {
                try { _isLaptop = _device?.Get().Kind == DeviceKind.Laptop; }
                catch { _isLaptop = false; }
            }
            return _isLaptop.Value;
        }
    }

    public const string LaptopNote =
        " ⚠ En un portátil: el DNS fijo se queda en este adaptador en todas las redes (casa, hoteles, cafés, trabajo). " +
        "Puede impedir que cargue la página de inicio de sesión de hoteles y cafés (portales cautivos) y romper los DNS internos de una empresa o VPN. " +
        "Si una red no deja iniciar sesión, desactiva este ajuste un momento para entrar. Para revertirlo: desactiva este interruptor o pulsa «Revertir»; " +
        "el equipo vuelve a los servidores que tenía antes o, si no había ninguno configurado, al DNS automático (DHCP).";

    public string Description => IsLaptop ? _baseDescription + LaptopNote : _baseDescription;
    public string RealisticEffect { get; }
    public SettingRiskLevel RiskLevel { get; }
    public bool IsWindowsVersionSupported { get; }
    public bool IsReversible => true;
    public bool RequiresConfirmation => false;
    public bool RequiresRestart => false;
    public bool RequiresAdmin => true; // Configurar WMI requiere elevación en Windows
    public string VerificationStatus => "Documentado por Microsoft";
    public string? DocumentationUrl => "https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/setdnsserversearchorder-method-in-class-win32-networkadapterconfiguration";

    public IDnsService DnsService => _dnsService;

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = usar el DNS de Cloudflare.</summary>
    public object? ApplyValue => OptionCloudflare;
    /// <summary>Sin copia original, desactivar el ajuste vuelve a DNS automático (DHCP).</summary>
    public object? RevertValue => OptionDhcp;

    /// <inheritdoc />
    public void RestoreBackup(object? originalValue, bool originalExisted)
    {
        if (HasOriginal) return;
        OriginalValue = originalValue;
        HasOriginal = true;
    }
    public bool RequiresRestartAfterApply { get; private set; }
    private string _lastOperationDetails = string.Empty;

    /// <summary>Aviso cuando el DNS cambió pero no se pudo vaciar la caché (nunca es un fallo del ajuste).</summary>
    public string CacheFlushWarning { get; private set; } = string.Empty;

    /// <summary>
    /// Vacía la caché DNS después de cambiar los servidores para que el cambio se note de inmediato.
    /// Si falla, el ajuste sigue siendo válido: se muestra un aviso y se registra en error.log.
    /// </summary>
    private void FlushDnsCache()
    {
        CacheFlushWarning = string.Empty;
        if (_cacheService == null) return;

        if (_cacheService.TryFlush(out var error))
            return;

        CacheFlushWarning = "El DNS cambió, pero no se pudo vaciar la caché";
        _lastOperationDetails += $" {CacheFlushWarning}.";
        try { _logger.Write($"DNS: no se pudo vaciar la caché. Detalle: {error}"); }
        catch { }
    }

    public string GetOperationDetails(object? requestedValue, bool dryRun)
    {
        var adapter = _dnsService.GetAdapterName();
        var before = string.Join(", ", _dnsService.GetDnsServers());
        if (dryRun)
            return $"DNS simulación=true; adaptador={adapter}; estado actual={before}; solicitado={requestedValue}.";
        return _lastOperationDetails;
    }

    /// <summary>
    /// Devuelve los servidores DNS crudos (lista de IPs separada por comas, o cadena vacía si es DHCP).
    /// Esto asegura que OriginalValue conserve los servidores exactos para revertir.
    /// </summary>
    public object? ReadCurrentState()
    {
        var servers = _dnsService.GetDnsServers();
        return servers != null && servers.Length > 0 ? string.Join(", ", servers) : string.Empty;
    }

    /// <summary>
    /// Está aplicado cuando los servidores configurados son los de Cloudflare (el valor que escribe
    /// el interruptor), comparado con la misma descripción que se muestra en pantalla.
    /// </summary>
    public bool IsAppliedInSystem
    {
        get
        {
            try { return DescribeServers(_dnsService.GetDnsServers()) == OptionCloudflare; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Devuelve el nombre amigable de los servidores DNS para mostrar en la interfaz de usuario.
    /// </summary>
    public string GetFriendlyState()
    {
        var servers = _dnsService.GetDnsServers();
        var adapterName = _dnsService.GetAdapterName();
        if (string.IsNullOrWhiteSpace(adapterName)) return "No disponible (no se identificó un adaptador principal)";
        return $"{DescribeServers(servers)} · {adapterName}";
    }

    /// <summary>
    /// Convierte un array de servidores en una cadena legible para la UI.
    /// </summary>
    public static string DescribeServers(string[]? servers)
    {
        if (servers == null || servers.Length == 0)
            return OptionDhcp;

        if (servers.Contains("1.1.1.1") || servers.Contains("1.0.0.1"))
            return OptionCloudflare;

        if (servers.Contains("8.8.8.8") || servers.Contains("8.8.4.4"))
            return OptionGoogle;

        return string.Join(", ", servers);
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            // Guardar los servidores DNS crudos actuales (lista de IPs o vacío si DHCP) para poder revertirlos exactamente
            var currentServers = _dnsService.GetDnsServers();
            var originalDhcp = _dnsService.IsUsingDhcp();
            var adapterName = _dnsService.GetAdapterName();
            OriginalValue = new DnsOriginalConfiguration(originalDhcp, currentServers?.ToArray() ?? Array.Empty<string>());
            var val = newValue?.ToString() ?? string.Empty;
            _lastOperationDetails = $"DNS simulación=false; adaptador={adapterName}; antes={(currentServers is null ? string.Empty : string.Join(", ", currentServers))}; solicitado={val}.";
            if (!ApplyDnsValue(val))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(_dnsService.GetAdapterName())
                    ? "No se aplicó DNS porque no se pudo identificar un único adaptador principal con puerta de enlace."
                    : $"No se pudo cambiar los servidores DNS. {_dnsService.LastOperationMessage}");
            RequiresRestartAfterApply = _dnsService.LastOperationRequiresRestart;
            _lastOperationDetails += $" {_dnsService.LastOperationMessage}; después={string.Join(", ", _dnsService.GetDnsServers())}.";
            HasOriginal = true;
            FlushDnsCache();
        }
    }

    public bool VerifyApplied(object? requestedValue, out string failureReason)
    {
        var val = requestedValue?.ToString() ?? string.Empty;
        var expectsDhcp = val.Equals(OptionDhcp, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(val);
        var expected = expectsDhcp
            ? Array.Empty<string>()
            : val.StartsWith("Cloudflare", StringComparison.OrdinalIgnoreCase) ? new[] { "1.1.1.1", "1.0.0.1" }
            : val.StartsWith("Google", StringComparison.OrdinalIgnoreCase) ? new[] { "8.8.8.8", "8.8.4.4" }
            : val.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        var actual = _dnsService.GetDnsServers();
        if (expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase) && (!expectsDhcp || _dnsService.IsUsingDhcp()))
        {
            failureReason = string.Empty;
            return true;
        }
        failureReason = $"Windows no confirmó el DNS esperado en {_dnsService.GetAdapterName()}: esperado {string.Join(", ", expected)}, leído {string.Join(", ", actual)}.";
        return false;
    }

    private bool ApplyDnsValue(string val)
    {
        if (val.Equals(OptionDhcp, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(val))
        {
            return _dnsService.SetDhcp();
        }

        // Presets: solo activar si el valor coincide con el nombre del preset, no con IPs arbitrarias
        if (val.StartsWith("Cloudflare", StringComparison.OrdinalIgnoreCase) ||
            val == OptionCloudflare)
        {
            return _dnsService.SetDnsServers(new[] { "1.1.1.1", "1.0.0.1" });
        }

        if (val.StartsWith("Google", StringComparison.OrdinalIgnoreCase) ||
            val == OptionGoogle)
        {
            return _dnsService.SetDnsServers(new[] { "8.8.8.8", "8.8.4.4" });
        }

        // IPs personalizadas: parsear y usar exactamente como se proporcionaron
        var servers = val.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        if (servers.Length > 0)
            return _dnsService.SetDnsServers(servers);
        else
            return _dnsService.SetDhcp();
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            if (OriginalValue is DnsOriginalConfiguration original && original.DhcpEnabled)
            {
                if (!_dnsService.SetDhcp()) throw new InvalidOperationException("No se pudo restaurar DNS automático.");
            }
            else
            {
                var servers = OriginalValue is DnsOriginalConfiguration config
                    ? config.Servers
                    : (OriginalValue as string ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

                if (servers.Length == 0)
                {
                    if (!_dnsService.SetDhcp())
                        throw new InvalidOperationException("No se pudo restaurar DNS automático.");
                }
                else
                {
                    if (!_dnsService.SetDnsServers(servers))
                        throw new InvalidOperationException("No se pudo restaurar los servidores DNS.");
                }
            }

            OriginalValue = null;
            HasOriginal = false;
            FlushDnsCache();
        }
    }
}

public sealed record DnsOriginalConfiguration(bool DhcpEnabled, string[] Servers)
{
    public override string ToString() => DhcpEnabled ? "Automático (DHCP)" : string.Join(", ", Servers);
}
