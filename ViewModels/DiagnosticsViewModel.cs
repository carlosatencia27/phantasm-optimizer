using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using PC_Optimizer.Core;
using PC_Optimizer.Services;

namespace PC_Optimizer.ViewModels;

/// <summary>Fila de la lista de programas: solo el nombre, la RAM y la CPU.</summary>
public sealed class ProcessUsageRow
{
    public ProcessUsageRow(ProcessUsage usage)
    {
        Name = usage.Name;
        RamText = ByteSize.Format(usage.RamBytes);
        CpuText = $"{usage.CpuPercent:0.0} %";
        CpuPercent = usage.CpuPercent;
    }

    public string Name { get; }
    public string RamText { get; }
    public string CpuText { get; }
    public double CpuPercent { get; }
}

/// <summary>Fila de un arranque de Windows (solo lectura).</summary>
public sealed class BootHistoryRow
{
    public BootHistoryRow(BootHistoryEntry entry)
    {
        WhenText = entry.When == DateTime.MinValue ? "fecha no disponible" : entry.When.ToString("dd/MM/yyyy HH:mm");
        SecondsText = $"{entry.Seconds:0.0} s";
    }

    public string WhenText { get; }
    public string SecondsText { get; }
}

/// <summary>Fila de una unidad fija.</summary>
public sealed class StorageVolumeRow
{
    public StorageVolumeRow(StorageVolume volume)
    {
        Name = volume.Name;
        MediaText = volume.MediaType switch
        {
            "SSD" => "SSD",
            "HDD" => "HDD",
            "SCM" => "SCM",
            _ => "Tipo desconocido"
        };
        SpaceText = $"{ByteSize.Format(volume.FreeBytes)} libres de {ByteSize.Format(volume.TotalBytes)} "
            + $"({volume.FreePercent:0} % libre)";
        FreePercent = volume.FreePercent;
    }

    public string Name { get; }
    public string MediaText { get; }
    public string SpaceText { get; }
    public double FreePercent { get; }
}

/// <summary>
/// Pantalla «Diagnóstico»: qué consume tu PC y cómo está el almacenamiento. Solo lectura: no
/// cambia, no cierra procesos y no pide administrador. Los procesos se actualizan cada 2 s
/// únicamente mientras la pantalla está visible.
/// </summary>
public class DiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly IProcessUsageProvider _processes;
    private readonly IStorageDiagnosticsService _storage;
    private readonly IStartupProgramService? _startupPrograms;
    private readonly IBootHistoryService _boots;
    private readonly IMemoryDiagnosticsService? _memory;
    private readonly INetworkDiagnosticsService? _network;
    private readonly Func<bool> _isAdmin;

    private DispatcherTimer? _timer;
    private bool _isRefreshing;
    private bool _isActive;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private string _trimText = "TRIM: no disponible";
    private string _trimDetail = string.Empty;
    private string _storageNote = string.Empty;
    private string _startupProgramsText = "Programas de inicio habilitados: no disponible";
    private string _updatedAtText = "Sin datos todavía";
    private string _bootNote = "Sin datos todavía";
    private string _memorySpeedHeadline = "No se puede determinar la velocidad de la RAM";
    private string _memorySpeedDetail = string.Empty;
    private string _channelHeadline = "No se puede determinar con certeza si la RAM está en dual channel";
    private string _channelDetail = string.Empty;
    private string _networkHeadline = "No se puede determinar el tipo de conexión";
    private string _networkDetail = string.Empty;

    public const int RefreshSeconds = 2;

    public ObservableCollection<ProcessUsageRow> TopRam { get; } = new();
    public ObservableCollection<ProcessUsageRow> TopCpu { get; } = new();
    public ObservableCollection<StorageVolumeRow> Volumes { get; } = new();
    public ObservableCollection<BootHistoryRow> Boots { get; } = new();
    public ObservableCollection<string> MemoryModules { get; } = new();
    public ObservableCollection<string> NetworkAdapters { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string TrimText
    {
        get => _trimText;
        private set { _trimText = value; OnPropertyChanged(); }
    }

    public string TrimDetail
    {
        get => _trimDetail;
        private set { _trimDetail = value; OnPropertyChanged(); }
    }

    public string StorageNote
    {
        get => _storageNote;
        private set { _storageNote = value; OnPropertyChanged(); }
    }

    public string StartupProgramsText
    {
        get => _startupProgramsText;
        private set { _startupProgramsText = value; OnPropertyChanged(); }
    }

    public string UpdatedAtText
    {
        get => _updatedAtText;
        private set { _updatedAtText = value; OnPropertyChanged(); }
    }

    public string BootNote
    {
        get => _bootNote;
        private set { _bootNote = value; OnPropertyChanged(); }
    }

    public string MemorySpeedHeadline
    {
        get => _memorySpeedHeadline;
        private set { _memorySpeedHeadline = value; OnPropertyChanged(); }
    }

    public string MemorySpeedDetail
    {
        get => _memorySpeedDetail;
        private set { _memorySpeedDetail = value; OnPropertyChanged(); }
    }

    public string ChannelHeadline
    {
        get => _channelHeadline;
        private set { _channelHeadline = value; OnPropertyChanged(); }
    }

    public string ChannelDetail
    {
        get => _channelDetail;
        private set { _channelDetail = value; OnPropertyChanged(); }
    }

    public string NetworkHeadline
    {
        get => _networkHeadline;
        private set { _networkHeadline = value; OnPropertyChanged(); }
    }

    public string NetworkDetail
    {
        get => _networkDetail;
        private set { _networkDetail = value; OnPropertyChanged(); }
    }

    public bool HasVolumes => Volumes.Count > 0;
    public bool HasBoots => Boots.Count > 0;

    public ICommand RefreshCommand { get; }
    public ICommand OpenStartupSectionCommand { get; }

    /// <summary>Lo conecta MainViewModel para navegar al Panel, donde se gestionan los programas de inicio.</summary>
    public Action<string>? NavigationRequested { get; set; }

    public DiagnosticsViewModel(IProcessUsageProvider processes, IStorageDiagnosticsService storage,
        Func<bool>? isAdmin = null, IStartupProgramService? startupPrograms = null,
        IBootHistoryService? boots = null, IMemoryDiagnosticsService? memory = null,
        INetworkDiagnosticsService? network = null)
    {
        _processes = processes;
        _storage = storage;
        _isAdmin = isAdmin ?? (() => false);
        _startupPrograms = startupPrograms;
        _boots = boots ?? new SystemBootHistoryService();
        _memory = memory;
        _network = network;

        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(includeStorage: true));
        OpenStartupSectionCommand = new RelayCommand(_ => NavigationRequested?.Invoke("Panel"));
    }

    /// <summary>Empieza a actualizar: solo mientras la pantalla está visible.</summary>
    public void Start()
    {
        if (_isActive) return;
        _isActive = true;

        if (_timer == null)
        {
            try
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RefreshSeconds) };
                _timer.Tick += (_, _) =>
                {
                    if (_isActive) _ = RefreshAsync(includeStorage: false);
                };
            }
            catch
            {
                // Sin Dispatcher (pruebas): se actualiza solo al llamar a RefreshAsync.
            }
        }

        _timer?.Start();
        _ = RefreshAsync(includeStorage: true);
    }

    /// <summary>Detiene el temporizador al salir de la pantalla o al ocultar la ventana.</summary>
    public void Stop()
    {
        _isActive = false;
        _timer?.Stop();
    }

    /// <summary>
    /// Una pasada completa: procesos (5 por RAM y 5 por CPU) y, si se pide, almacenamiento y
    /// programas de inicio. Nunca se solapan dos ciclos.
    /// </summary>
    public async Task RefreshAsync(bool includeStorage = false)
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        IsLoading = true;

        try
        {
            var snapshot = await Task.Run(() => _processes.GetTop(5));
            ApplySnapshot(snapshot ?? ProcessUsageSnapshot.Empty);

            if (includeStorage)
            {
                bool isAdmin = SafeIsAdmin();
                var report = await Task.Run(() => _storage.GetReport(isAdmin));
                ApplyStorage(report);
                ApplyStartupPrograms();
                var bootResult = await Task.Run(() => _boots.GetLastBoots(5));
                ApplyBoots(bootResult);

                var memoryTask = Task.Run(ReadMemorySafe);
                var networkTask = Task.Run(ReadNetworkSafe);
                await Task.WhenAll(memoryTask, networkTask);
                ApplyMemory(memoryTask.Result);
                ApplyNetwork(networkTask.Result);
            }

            UpdatedAtText = $"Actualizado: {DateTime.Now:HH:mm:ss}";
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo leer el sistema: {PC_Optimizer.Services.UserErrorText.Of(ex)}";
        }
        finally
        {
            IsLoading = false;
            _isRefreshing = false;
        }
    }

    private bool SafeIsAdmin()
    {
        try { return _isAdmin(); } catch { return false; }
    }

    private void ApplySnapshot(ProcessUsageSnapshot snapshot)
    {
        TopRam.Clear();
        foreach (var usage in snapshot.TopByRam) TopRam.Add(new ProcessUsageRow(usage));

        TopCpu.Clear();
        foreach (var usage in snapshot.TopByCpu) TopCpu.Add(new ProcessUsageRow(usage));

        OnPropertyChanged(nameof(HasProcessData));
    }

    private void ApplyStorage(StorageReport report)
    {
        Volumes.Clear();
        foreach (var volume in report.Volumes ?? Array.Empty<StorageVolume>())
            Volumes.Add(new StorageVolumeRow(volume));

        TrimText = "TRIM: " + (report.Trim?.Text ?? "no disponible");
        TrimDetail = report.Trim?.Detail ?? string.Empty;
        StorageNote = string.IsNullOrWhiteSpace(report.Note)
            ? "Solo lectura: la app no cambia el tipo de disco, el espacio ni TRIM."
            : report.Note;
        OnPropertyChanged(nameof(HasVolumes));
    }

    private void ApplyStartupPrograms()
    {
        if (_startupPrograms == null)
        {
            StartupProgramsText = "Programas de inicio habilitados: no disponible";
            return;
        }

        try
        {
            int enabled = _startupPrograms.GetStartupPrograms().Count(p => p.Enabled);
            StartupProgramsText = $"Programas de inicio habilitados: {enabled}";
        }
        catch
        {
            StartupProgramsText = "Programas de inicio habilitados: no disponible";
        }
    }

    public bool HasProcessData => TopRam.Count > 0 || TopCpu.Count > 0;

    private MemoryReport ReadMemorySafe()
    {
        if (_memory == null) return MemoryReport.Unavailable("No se puede determinar: la lectura de memoria no está disponible en esta versión.");
        try { return _memory.GetReport(); }
        catch (Exception ex) { return MemoryReport.Unavailable($"No se puede determinar: la lectura de memoria falló ({ex.GetType().Name})."); }
    }

    private NetworkReport ReadNetworkSafe()
    {
        if (_network == null) return NetworkReport.Unavailable("No se puede determinar: la lectura de red no está disponible en esta versión.");
        try { return _network.GetReport(); }
        catch (Exception ex) { return NetworkReport.Unavailable($"No se puede determinar: la lectura de red falló ({ex.GetType().Name})."); }
    }

    private void ApplyMemory(MemoryReport report)
    {
        MemorySpeedHeadline = report.SpeedHeadline;
        MemorySpeedDetail = report.SpeedDetail;
        ChannelHeadline = report.ChannelHeadline;
        ChannelDetail = report.ChannelDetail;
        MemoryModules.Clear();
        foreach (var line in report.ModuleLines ?? Array.Empty<string>()) MemoryModules.Add(line);
        OnPropertyChanged(nameof(HasMemoryModules));
    }

    private void ApplyNetwork(NetworkReport report)
    {
        NetworkHeadline = report.Headline;
        NetworkDetail = report.Detail;
        NetworkAdapters.Clear();
        foreach (var line in report.AdapterLines ?? Array.Empty<string>()) NetworkAdapters.Add(line);
        OnPropertyChanged(nameof(HasNetworkAdapters));
    }

    public bool HasMemoryModules => MemoryModules.Count > 0;
    public bool HasNetworkAdapters => NetworkAdapters.Count > 0;

    /// <summary>Últimos arranques: solo lo medido; si no se pudo leer se explica el motivo.</summary>
    private void ApplyBoots(BootHistoryResult result)
    {
        Boots.Clear();
        foreach (var entry in result.Entries ?? Array.Empty<BootHistoryEntry>())
            Boots.Add(new BootHistoryRow(entry));

        BootNote = result.Problem ?? "Últimos arranques registrados por Windows (evento 100). Solo lectura: la app no modifica ni borra el registro de eventos.";
        OnPropertyChanged(nameof(HasBoots));
    }    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
