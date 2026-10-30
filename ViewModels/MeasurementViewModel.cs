using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using PC_Optimizer.Services;

namespace PC_Optimizer.ViewModels;

public class MeasurementViewModel : INotifyPropertyChanged
{
    private readonly IMeasurementService _measurementService;
    private bool _isMeasuring;
    private string _statusMessage = string.Empty;
    private MeasurementReport? _lastReport;

    public ObservableCollection<MeasurementSnapshotViewModel> Snapshots { get; } = new();

    /// <summary>Tabla antes/después: una fila por métrica, con su margen y clasificación.</summary>
    public ObservableCollection<MetricComparison> ComparisonRows { get; } = new();

    public bool IsMeasuring
    {
        get => _isMeasuring;
        set { _isMeasuring = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public MeasurementReport? LastReport
    {
        get => _lastReport;
        set { _lastReport = value; OnPropertyChanged(); }
    }

    /// <summary>Texto de los márgenes de ruido, para mostrarlo junto a la tabla.</summary>
    public string MarginsDescription => MeasurementMargins.Description;

    public ICommand TakeBeforeCommand { get; }
    public ICommand TakeAfterCommand { get; }
    public ICommand CompareCommand { get; }

    public MeasurementViewModel(IMeasurementService? measurementService = null)
    {
        _measurementService = measurementService ?? new MeasurementService(new SystemMeasurementProvider());

        TakeBeforeCommand = new RelayCommand(_ => _ = TakeSnapshotAsync("Antes de aplicar ajustes"));
        TakeAfterCommand = new RelayCommand(_ => _ = TakeSnapshotAsync("Después de aplicar ajustes"));
        CompareCommand = new RelayCommand(_ => GenerateReport());
    }

    /// <summary>
    /// La medición corre en segundo plano (muestras de 1 s y consultas DNS): la ventana sigue
    /// respondiendo y el botón muestra el indicador de carga.
    /// </summary>
    private async Task TakeSnapshotAsync(string label)
    {
        if (IsMeasuring) return;

        IsMeasuring = true;
        StatusMessage = "Midiendo en segundo plano: 10 muestras de CPU y RAM (1 s entre cada una) y 6 consultas DNS con nombres nuevos enviadas directamente a tu servidor DNS (sin caché).";
        try
        {
            var service = _measurementService;
            var snapshot = await Task.Run(() => service.TakeSnapshot(label));

            Snapshots.Add(new MeasurementSnapshotViewModel(snapshot));
            ComparisonRows.Clear();
            LastReport = null;
            StatusMessage = $"Instantánea «{label}» tomada a las {snapshot.Timestamp:HH:mm:ss}. " +
                "Pulsa «Generar reporte comparativo» cuando tengas las dos.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al tomar instantánea: {PC_Optimizer.Services.UserErrorText.Of(ex)}";
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    private void GenerateReport()
    {
        var snapshots = _measurementService.Snapshots;
        if (snapshots.Count < 2)
        {
            StatusMessage = "Se necesitan al menos dos instantáneas (antes y después).";
            return;
        }

        var before = snapshots[^2];
        var after = snapshots[^1];

        LastReport = _measurementService.Compare(before.Label, after.Label);
        ComparisonRows.Clear();
        foreach (var metric in LastReport.Metrics)
            ComparisonRows.Add(metric);

        StatusMessage = LastReport.Summary;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class MeasurementSnapshotViewModel : INotifyPropertyChanged
{
    private readonly MeasurementSnapshot _snapshot;

    public string Label => _snapshot.Label;
    public string Timestamp => _snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    public string CpuUsage => $"{_snapshot.CpuUsagePercent:0.0}% (mediana de {_snapshot.CpuSampleCount}, informativo)";
    public string RamUsage => $"{_snapshot.RamUsagePercent:0.0}% ({_snapshot.RamUsedBytes / 1024 / 1024:N0} MB de {_snapshot.RamTotalBytes / 1024 / 1024:N0} MB, informativo)";
    public string ProcessCount => $"{_snapshot.ProcessCount} (informativo)";

    public string FreeSpace => _snapshot.FreeSpaceBytes.HasValue
        ? MeasurementService.FormatBytes(_snapshot.FreeSpaceBytes.Value)
        : "no disponible";

    public string Hiberfil => _snapshot.Hiberfil is { Available: true } hiberfil
        ? (hiberfil.Exists ? MeasurementService.FormatBytes(hiberfil.Bytes) : "no existe")
        : "no disponible";

    public string StartupPrograms => _snapshot.StartupProgramsEnabled?.ToString() ?? "no disponible";

    public string LastBoot => _snapshot.LastBootTimeSeconds.HasValue
        ? $"{_snapshot.LastBootTimeSeconds.Value:0.0} s"
        : $"no disponible: {_snapshot.BootTimeUnavailableReason ?? "Windows no informó el dato."}";

    public string DnsResolution => _snapshot.DnsResolutionMs >= 0
        ? $"{_snapshot.DnsResolutionMs} ms ({_snapshot.DnsServersUsed})"
        : $"no medible: {_snapshot.DnsUnavailableReason ?? "no se pudo consultar el servidor DNS."}";

    public string Ping => _snapshot.PingMs >= 0 ? $"{_snapshot.PingMs} ms" : "no disponible";
    public bool HasNetwork => _snapshot.HasNetwork;

    public MeasurementSnapshotViewModel(MeasurementSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
