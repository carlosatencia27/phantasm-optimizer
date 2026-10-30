using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using PC_Optimizer.Core;

namespace PC_Optimizer.Services;

public sealed record GuidedDnsResult(bool Available, double? CurrentMs, string? CurrentServer,
    double? CloudflareMs, double? GoogleMs, string Reason);

public sealed record GuidedScanResult(IReadOnlyList<GuidedRecommendation> Recommendations,
    string Summary, GuidedDnsResult Dns);

public sealed class GuidedRecommendation : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>Frase simple si el texto usa jerga (HAGS, TRIM, DNS…); null si no.</summary>
    public string? Hint => PC_Optimizer.Core.Glossary.HintFor(Title, Change, Benefit);
    public string Change { get; init; } = "";
    public string Benefit { get; init; } = "";
    public string Risk { get; init; } = "";
    public string Reversible { get; init; } = "";
    public string Warning { get; init; } = "";
    public bool RequiresAdmin { get; init; }
    public bool IsSelectable => !RequiresAdmin;
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Vista previa de una limpieza. RequiresAdmin: sin administrador no se puede ejecutar.</summary>
public sealed record GuidedCleanupPreview(string Id, string Name, long Bytes, bool RequiresAdmin = false);

/// <summary>Frontera de solo lectura; las pruebas sustituyen todos los datos del PC.</summary>
public interface IGuidedAnalysisSource
{
    IReadOnlyList<StartupProgram> GetStartupPrograms();
    IReadOnlyList<GuidedCleanupPreview> GetCleanupPreviews();
    HiberfilInfo GetHiberfil();
    GuidedDnsResult MeasureDns(IProgress<string>? progress, CancellationToken cancellation);
    bool IsPortableComputer();
    double? GetLastBootTimeSeconds();
}

public interface IDnsServerLatencyProbe
{
    /// <summary>Mediana de las muestras obtenidas; null si no se pudo medir ninguna.</summary>
    double? MeasureMedian(string server, IReadOnlyList<string> domains, int repetitions, int timeoutMs, CancellationToken cancellation);
}

public sealed class SystemGuidedAnalysisSource : IGuidedAnalysisSource
{
    public const int DomainsCount = 5;
    public const int RoundsPerDomain = 2;          // 5 dominios × 2 rondas = 10 muestras por servidor
    public const int SamplesPerServer = DomainsCount * RoundsPerDomain;
    public const int PerQueryTimeoutMs = 3000;
    public static readonly TimeSpan DefaultGlobalCap = TimeSpan.FromSeconds(30);

    private readonly IDiskCleanupService _cleanup;
    private readonly IDeliveryOptimizationService _delivery;
    private readonly IStartupProgramService _startup;
    private readonly IDnsService _dns;
    private readonly IMeasurementProvider _measurement;
    private readonly IDnsServerLatencyProbe _probe;
    private readonly Func<bool> _isAdmin;
    private readonly TimeSpan _globalCap;
    private readonly string _temp;
    private readonly string _wer;

    public SystemGuidedAnalysisSource(IDiskCleanupService cleanup, IDeliveryOptimizationService delivery,
        IStartupProgramService startup, IDnsService dns, IMeasurementProvider measurement,
        IDnsServerLatencyProbe? probe = null, string? temp = null, string? wer = null,
        Func<bool>? isAdmin = null, TimeSpan? globalCap = null)
    {
        _cleanup = cleanup; _delivery = delivery; _startup = startup; _dns = dns; _measurement = measurement;
        // Consulta UDP directa a cada servidor: no pasa por el resolver de Windows (sin caché que falsee la medida).
        _probe = probe ?? new UdpDnsLatencyProbe();
        _temp = temp ?? (Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath());
        _wer = wer ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER");
        _isAdmin = isAdmin ?? (() => false);
        _globalCap = globalCap ?? DefaultGlobalCap;
    }

    public IReadOnlyList<StartupProgram> GetStartupPrograms() => _startup.GetStartupPrograms().Where(p => p.Enabled).ToArray();
    public IReadOnlyList<GuidedCleanupPreview> GetCleanupPreviews()
    {
        var result = new List<GuidedCleanupPreview>();
        AddFolder("cleanup.temp", "%TEMP% del usuario", _temp);
        AddFolder("cleanup.wer", "Informes de errores de Windows (usuario)", _wer);
        // Solo se ofrece si el cmdlet existe; sin administrador se muestra deshabilitada y con el botón de elevar.
        try
        {
            var availability = _delivery.GetStatus(false);
            if (availability.CmdletAvailable && availability.Bytes > 0)
                result.Add(new("cleanup.delivery", "Caché de Delivery Optimization", availability.Bytes, RequiresAdmin: !IsAdminSafe()));
        }
        catch { }
        return result;
        void AddFolder(string id, string name, string path)
        {
            try { var bytes = _cleanup.GetTempFolderSize(path); if (bytes > 0) result.Add(new(id, name, bytes)); } catch { }
        }
    }
    public HiberfilInfo GetHiberfil() => _measurement.GetHiberfil();
    public bool IsPortableComputer() => new SystemDeviceInfoService().Get().Kind == DeviceKind.Laptop;
    public double? GetLastBootTimeSeconds() => _measurement.GetLastBootTimeSeconds();
    private bool IsAdminSafe() { try { return _isAdmin(); } catch { return false; } }

    /// <summary>
    /// Compara el DNS actual con Cloudflare y Google sin cambiar nada: 5 dominios × 2 rondas por
    /// servidor (máximo 3 consultas a la vez), 3 s por consulta y un tope global de 30 s.
    /// </summary>
    public GuidedDnsResult MeasureDns(IProgress<string>? progress, CancellationToken cancellation)
    {
        try
        {
            var current = _dns.GetDnsServers();
            if (current.Length == 0) return new(false, null, null, null, null, "El DNS actual es automático o no se pudo leer; no hay un servidor concreto para comparar.");
            var currentServers = current.Where(s => IPAddress.TryParse(s, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToArray();
            if (currentServers.Length == 0) return new(false, null, null, null, null, "El DNS actual no se pudo medir: no hay una dirección IPv4 de servidor disponible.");

            var targets = currentServers.Concat(new[] { "1.1.1.1", "8.8.8.8" }).ToArray();
            var domains = SystemMeasurementProvider.DefaultDnsDomains;
            using var cap = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            cap.CancelAfter(_globalCap);
            var medians = new Dictionary<string, double?>();

            for (var i = 0; i < targets.Length; i++)
            {
                if (cap.IsCancellationRequested) break;
                progress?.Report($"Servidor {i + 1} de {targets.Length}: {targets[i]}");
                medians[targets[i]] = _probe.MeasureMedian(targets[i], domains, RoundsPerDomain, PerQueryTimeoutMs, cap.Token);
            }

            if (cancellation.IsCancellationRequested) return new(false, null, null, null, null, "Análisis cancelado.");
            if (cap.IsCancellationRequested || medians.Count < targets.Length)
                return new(false, null, null, null, null,
                    $"No se pudo medir con fiabilidad: se alcanzó el límite de {_globalCap.TotalSeconds:0} s. No se recomienda cambiar el DNS.");

            var measured = currentServers.Select(s => medians.TryGetValue(s, out var v) ? v : null).Where(v => v.HasValue).Select(v => v!.Value).Order().ToArray();
            double? currentMs = measured.Length == 0 ? null : measured.Length % 2 == 1
                ? measured[measured.Length / 2]
                : (measured[measured.Length / 2 - 1] + measured[measured.Length / 2]) / 2;
            double? cloudflare = medians.TryGetValue("1.1.1.1", out var cf) ? cf : null;
            double? google = medians.TryGetValue("8.8.8.8", out var gg) ? gg : null;
            var candidatesAvailable = cloudflare.HasValue || google.HasValue;
            return new(currentMs.HasValue && candidatesAvailable, currentMs, string.Join(", ", currentServers), cloudflare, google,
                !currentMs.HasValue ? "No se pudo medir el DNS actual con consultas directas."
                    : !candidatesAvailable ? "No se pudieron medir Cloudflare ni Google; no se recomienda cambiar el DNS."
                    : $"Mediana de consultas directas sin caché; {DomainsCount} dominios × {RoundsPerDomain} rondas por servidor, máximo 3 s por consulta.");
        }
        catch (Exception ex) { return new(false, null, null, null, null, $"Medición DNS no disponible: {PC_Optimizer.Services.UserErrorText.Of(ex)}"); }
    }
}

public static class GuidedRecommendationBuilder
{
    public static GuidedScanResult Build(IGuidedAnalysisSource source, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        var items = new List<GuidedRecommendation>();
        if (cancellation.IsCancellationRequested)
            return new(Array.Empty<GuidedRecommendation>(), "Análisis cancelado. No se cambió nada.",
                new GuidedDnsResult(false, null, null, null, null, "Análisis cancelado."));

        foreach (var p in source.GetStartupPrograms())
        {
            var displayName = Path.GetFileNameWithoutExtension(p.Name);
            if (string.IsNullOrWhiteSpace(displayName)) displayName = "Programa de inicio";
            items.Add(new() { Id = "startup." + p.KeyPath + "|" + p.ValueName, Title = displayName,
                Change = "Evita que este programa se ejecute automáticamente al iniciar sesión.", Benefit = "Menos programas al iniciar sesión; el arranque solo se compara tras reiniciar.",
                Risk = p.IsLocalMachine ? "Moderado" : "Seguro", Reversible = "Sí", IsSelected = false });
        }
        foreach (var c in source.GetCleanupPreviews())
        {
            // Marcada por defecto solo si se puede ejecutar ya, el riesgo es Seguro y hay bytes medidos.
            var risk = c.Id == "cleanup.delivery" ? "Moderado" : "Seguro";
            items.Add(new() { Id = c.Id, Title = c.Name, Change = "Borra solo los archivos de la vista previa.",
                Benefit = $"{MeasurementService.FormatBytes(c.Bytes)} liberables (vista previa)", Risk = risk,
                Reversible = "No; acción de limpieza", RequiresAdmin = c.RequiresAdmin,
                IsSelected = !c.RequiresAdmin && risk == "Seguro" && c.Bytes > 0,
                Warning = c.RequiresAdmin
                    ? "Requiere administrador: la caché está dentro de la carpeta de Windows. Reinicia la app como administrador para vaciarla."
                    : "" });
        }
        var h = source.GetHiberfil();
        if (h.Available && h.Exists && h.Bytes > 0)
            items.Add(new() { Id = "hibernate", Title = "Desactivar hibernación", Change = "Desactiva hibernación y elimina hiberfil.sys; también deja sin efecto Inicio rápido.",
                Benefit = $"{MeasurementService.FormatBytes(h.Bytes)} en hiberfil.sys", Risk = "Moderado", Reversible = "Sí, requiere administrador",
                IsSelected = false, Warning = source.IsPortableComputer()
                    ? "En un portátil, se pierde la hibernación de seguridad cuando la batería está baja. También se desactiva Inicio rápido."
                    : "También se desactiva Inicio rápido." });

        var dns = source.MeasureDns(progress, cancellation);
        if (cancellation.IsCancellationRequested)
            return new(Array.Empty<GuidedRecommendation>(), "Análisis cancelado. No se cambió nada.", dns);

        if (dns.Available && dns.CurrentMs.HasValue)
        {
            foreach (var (name, value, id) in new[] { ("Cloudflare", dns.CloudflareMs, DnsSetting.OptionCloudflare), ("Google", dns.GoogleMs, DnsSetting.OptionGoogle) })
                if (value.HasValue && value.Value <= dns.CurrentMs.Value * .8 && dns.CurrentMs.Value - value.Value >= 5)
                    items.Add(new() { Id = "dns." + id, Title = $"Probar DNS de {name}", Change = "Usa el ajuste DNS existente para cambiar los servidores.",
                        Benefit = $"Mediana: actual {dns.CurrentMs:0.0} ms; {name} {value:0.0} ms.", Risk = "Moderado", Reversible = "Sí", IsSelected = false });
        }
        var dnsMessage = !dns.CurrentMs.HasValue ? dns.Reason : !dns.Available ?
            $"{dns.Reason} DNS actual {dns.CurrentMs:0.0} ms; Cloudflare {FormatMs(dns.CloudflareMs)}; Google {FormatMs(dns.GoogleMs)}." :
            items.Any(r => r.Id.StartsWith("dns.", StringComparison.Ordinal))
                ? $"DNS medido: actual {dns.CurrentMs:0.0} ms; Cloudflare {FormatMs(dns.CloudflareMs)}; Google {FormatMs(dns.GoogleMs)}."
                : $"Tu DNS actual ya es rápido frente a los servidores comparados: actual {dns.CurrentMs:0.0} ms; Cloudflare {FormatMs(dns.CloudflareMs)}; Google {FormatMs(dns.GoogleMs)}.";
        var summary = items.Count > 0
            ? $"Se encontraron {items.Count} oportunidades revisables."
            : dns.CurrentMs.HasValue
                ? "Tu PC ya está bien en esto: el análisis no encontró cambios con beneficio medido."
                : "No hay recomendaciones de otros ajustes.";
        return new(items, summary + " " + dnsMessage, dns);
    }

    private static string FormatMs(double? value) => value.HasValue ? $"{value.Value:0.0} ms" : "no disponible";
}

public interface IBootBaselineStore
{
    BootBaseline? Load();
    void Save(BootBaseline baseline);
}
public sealed record BootBaseline(DateTime BootSessionUtc, DateTime RecordedUtc, double? BootSeconds);
public sealed class JsonBootBaselineStore : IBootBaselineStore
{
    private readonly string _path;
    public JsonBootBaselineStore(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PC Optimizer", "boot-baseline.json");
    public BootBaseline? Load() { try { return JsonSerializer.Deserialize<BootBaseline>(File.ReadAllText(_path)); } catch { return null; } }
    public void Save(BootBaseline baseline) { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, JsonSerializer.Serialize(baseline)); }
}
