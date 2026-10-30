using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using PC_Optimizer.Core;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.ViewModels;

public sealed class GuidedOptimizationViewModel : INotifyPropertyChanged
{
    private readonly ITuningEngine _engine;
    private readonly IGuidedAnalysisSource _source;
    private readonly IRestorePointService _restore;
    private readonly RestorePointCoordinator _restoreCoordinator;
    private readonly IMeasurementService _measurement;
    private readonly IDiskCleanupService _cleanup;
    private readonly IDeliveryOptimizationService _delivery;
    private readonly Func<bool> _isAdmin;
    private readonly Func<string, bool> _confirmCleanup;
    private readonly IBootBaselineStore _baseline;
    private readonly Func<string, bool> _confirmRun;
    private readonly Action _restartAsAdmin;
    private CancellationTokenSource? _analysisCancellation;
    private bool _dryRun;
    private bool _isBusy;
    private string _status = "Analiza el equipo para ver oportunidades medidas.";
    private string _bootComparison = "La comparación del arranque aparece aquí después de reiniciar.";
    private MeasurementReport? _report;

    public ObservableCollection<GuidedRecommendation> Recommendations { get; } = new();
    public ObservableCollection<GuidedStep> Steps { get; } = new();
    public ObservableCollection<MetricComparison> ReportRows { get; } = new();
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public string BootComparison { get => _bootComparison; private set { _bootComparison = value; OnPropertyChanged(); } }
    public MeasurementReport? Report { get => _report; private set { _report = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); OnPropertyChanged(nameof(CanCancel)); } }
    public bool IsNotBusy => !IsBusy;
    public bool CanCancel => IsBusy;
    public bool DryRunMode { get => _dryRun; set { _dryRun = value; OnPropertyChanged(); } }
    public string MarginsDescription => MeasurementMargins.Description;
    public ICommand AnalyzeCommand { get; }
    public ICommand OptimizeCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RestartAsAdminCommand { get; }

    public GuidedOptimizationViewModel(ITuningEngine engine, IGuidedAnalysisSource source,
        IRestorePointService restore, IMeasurementService measurement, IDiskCleanupService cleanup,
        IDeliveryOptimizationService delivery, Func<bool>? isAdmin = null,
        Func<string, bool>? confirmCleanup = null, IBootBaselineStore? baseline = null,
        Action? restartAsAdmin = null, RestorePointCoordinator? restoreCoordinator = null,
        Func<string, bool>? confirmRun = null)
    {
        _restoreCoordinator = restoreCoordinator ?? new RestorePointCoordinator();
        _engine = engine; _source = source; _restore = restore; _measurement = measurement;
        _cleanup = cleanup; _delivery = delivery; _isAdmin = isAdmin ?? ConfirmDialog.IsRunningAsAdmin;
        _confirmCleanup = confirmCleanup ?? (message => System.Windows.MessageBox.Show(message, "Confirmar limpieza", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes);
        // Confirmación explícita antes de aplicar (en la app real). Sin ventana (pruebas) no pregunta.
        _confirmRun = confirmRun ?? (message => System.Windows.Application.Current == null ||
            System.Windows.MessageBox.Show(message, "Confirmar cambios", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes);
        _baseline = baseline ?? new JsonBootBaselineStore();
        _restartAsAdmin = restartAsAdmin ?? (() => { });
        AnalyzeCommand = new RelayCommand(_ => _ = AnalyzeAsync());
        OptimizeCommand = new RelayCommand(_ => _ = RunSelectedAsync());
        RevertCommand = new RelayCommand(_ => RevertReversible());
        CancelCommand = new RelayCommand(_ => CancelAnalysis());
        RestartAsAdminCommand = new RelayCommand(_ => _restartAsAdmin());
    }

    /// <summary>Detiene el análisis en curso (sin cambios en el sistema y sin dejar consultas colgadas).</summary>
    public void CancelAnalysis()
    {
        try { _analysisCancellation?.Cancel(); } catch { }
    }

    public async Task AnalyzeAsync()
    {
        if (IsBusy) return;
        IsBusy = true; Status = "Analizando solo lectura en segundo plano…";
        var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        try
        {
            var progress = new Progress<string>(text => { if (IsBusy) Status = text; });
            var result = await Task.Run(() => GuidedRecommendationBuilder.Build(_source, progress, cancellation.Token));
            Recommendations.Clear();
            if (cancellation.IsCancellationRequested)
            {
                Status = "Análisis cancelado. No se cambió nada.";
            }
            else
            {
                foreach (var item in result.Recommendations) Recommendations.Add(item);
                Status = result.Summary;
            }
            await CheckBootBaselineAsync();
        }
        catch (OperationCanceledException) { Recommendations.Clear(); Status = "Análisis cancelado. No se cambió nada."; }
        catch (Exception ex) { Status = $"No se pudo completar el análisis: {PC_Optimizer.Services.UserErrorText.Of(ex)}"; }
        finally
        {
            if (ReferenceEquals(_analysisCancellation, cancellation)) _analysisCancellation = null;
            IsBusy = false;
        }
    }

    public async Task RunSelectedAsync()
    {
        if (IsBusy) return;
        if (!_dryRun && !_engine.License.CanModifySystem)
        {
            Status = "No se aplicó nada porque falta una licencia válida. " + _engine.License.BlockedMessage;
            return;
        }
        var selected = Recommendations.Where(x => x.IsSelected && x.IsSelectable).ToList();
        if (selected.Count == 0)
        {
            Status = Recommendations.Any(x => x.IsSelected && !x.IsSelectable)
                ? "Las oportunidades seleccionadas requieren administrador. Usa «Reiniciar como administrador» y vuelve a analizar."
                : "Selecciona al menos una oportunidad.";
            return;
        }
        if (!_dryRun)
        {
            var list = string.Join(Environment.NewLine, selected.Select(x => "• " + x.Title));
            if (!_confirmRun($"Vas a aplicar {selected.Count} cambio(s) en este equipo:{Environment.NewLine}{list}{Environment.NewLine}{Environment.NewLine}" +
                             $"Antes se intenta crear un punto de restauración. Los ajustes se pueden revertir; la limpieza de archivos no, y pide su propia confirmación.{Environment.NewLine}¿Continuar?"))
            {
                Status = "No se aplicó nada: cancelaste la confirmación.";
                return;
            }
        }
        IsBusy = true; Steps.Clear(); Report = null; ReportRows.Clear();
        try
        {
            if (_dryRun)
            {
                AddStep("Punto de restauración", "Simulación: no se crea ningún punto ni se cambia Windows.", true);
            }
            else
            {
                AddStep("Punto de restauración", "Creando y verificando…", false);
                // Mismo límite de tiempo y mismos mensajes que el botón «Punto de restauración».
                RestorePointResult point;
                if (!_restore.IsSupported)
                    point = new RestorePointResult(RestorePointOutcome.NotSupported, RestorePointMessages.NotSupported);
                else
                {
                    try { point = await _restoreCoordinator.CreateAsync(_restore, "Phantasm Optimizer - optimización guiada"); }
                    catch (Exception ex) { point = RestorePointMessages.FromException(ex); }
                }
                UpdateLastStep(point.Success ? "Completado: " + point.Message : "Falló: " + point.Message, point.Success);
                if (!point.Success)
                {
                    Status = point.Outcome == RestorePointOutcome.TooSoon
                        ? "Se detuvo la optimización porque Windows no creó un punto nuevo (ya hay uno reciente). Vuelve a intentarlo más tarde."
                        : "Se detuvo la optimización porque no se pudo crear y comprobar el punto de restauración.";
                    return;
                }
            }

            var failures = new List<string>();
            MeasurementSnapshot? before = null;
            AddStep("Medición antes", "Tomando 10 muestras de CPU/RAM y las demás métricas…", false);
            try
            {
                before = await Task.Run(() => _measurement.TakeSnapshot("Optimización guiada · Antes")).WaitAsync(TimeSpan.FromSeconds(90));
                UpdateLastStep("Medición antes completada.", true);
                var session = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
                await Task.Run(() => _baseline.Save(new BootBaseline(session, DateTime.UtcNow, before.LastBootTimeSeconds)));
            }
            catch (Exception ex) { failures.Add("Medición antes/línea base: " + PC_Optimizer.Services.UserErrorText.Of(ex)); UpdateLastStep("Falló: " + PC_Optimizer.Services.UserErrorText.Of(ex), false); }

            foreach (var item in selected)
            {
                AddStep(item.Title, "En curso…", false);
                try
                {
                    var message = await ApplyRecommendationAsync(item);
                    var success = message.StartsWith("OK:", StringComparison.Ordinal);
                    if (!success) failures.Add($"{item.Title}: {message}");
                    UpdateLastStep(message, success);
                }
                catch (Exception ex) { failures.Add($"{item.Title}: {PC_Optimizer.Services.UserErrorText.Of(ex)}"); UpdateLastStep("Falló: " + PC_Optimizer.Services.UserErrorText.Of(ex), false); }
            }

            AddStep("Medición después", "Tomando métricas…", false);
            try
            {
                var after = await Task.Run(() => _measurement.TakeSnapshot("Optimización guiada · Después")).WaitAsync(TimeSpan.FromSeconds(90));
                UpdateLastStep("Medición después completada.", true);
                if (before != null)
                {
                    Report = _measurement.Compare(before.Label, after.Label);
                    ReportRows.Clear(); foreach (var row in Report.Metrics) ReportRows.Add(row);
                }
                else failures.Add("No se pudo comparar: faltó la medición anterior.");
            }
            catch (Exception ex) { failures.Add("Medición después: " + PC_Optimizer.Services.UserErrorText.Of(ex)); UpdateLastStep("Falló: " + PC_Optimizer.Services.UserErrorText.Of(ex), false); }
            Status = _dryRun ? "Simulación terminada: Windows no se cambió. Las métricas son observaciones, no una promesa de mejora." :
                failures.Count == 0 ? "Proceso terminado. El informe muestra solo diferencias medidas fuera de los márgenes de ruido." :
                "Proceso terminado con pasos fallidos: " + string.Join(" | ", failures);
        }
        catch (Exception ex) { Status = $"Falló el proceso guiado: {PC_Optimizer.Services.UserErrorText.Of(ex)}"; }
        finally { IsBusy = false; }
    }

    private async Task<string> ApplyRecommendationAsync(GuidedRecommendation item)
    {
        if (item.Id.StartsWith("cleanup.", StringComparison.Ordinal))
        {
            if (!_dryRun && !_engine.License.CanModifySystem) return "Omitido (sin licencia válida): " + _engine.License.BlockedMessage;
            if (!_confirmCleanup($"Se borrarán los elementos mostrados en la vista previa de «{item.Title}». Esta acción no se puede deshacer. ¿Continuar?")) return "Omitido: no se confirmó la limpieza.";
            if (_dryRun) { _engine.RecordNonReversibleAction("Guided." + item.Id, item.Title, true, "Simulación: no se borró nada."); return "OK: Simulado; no se borró nada."; }
            object? result = item.Id switch
            {
                "cleanup.temp" => await Task.Run(() => new TempFileSetting(_cleanup, Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), item.Title, "", "", SettingRiskLevel.Safe).CleanAndVerify(_isAdmin())),
                "cleanup.wer" => await Task.Run(() => new TempFileSetting(_cleanup, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER"), item.Title, "", "", SettingRiskLevel.Safe).CleanAndVerify(_isAdmin())),
                "cleanup.delivery" => await Task.Run(() => _delivery.Clean(_isAdmin())),
                _ => null
            };
            if (result is CleanupRunResult cleanupResult)
            {
                _engine.RecordNonReversibleAction("Guided." + item.Id, item.Title, false, cleanupResult.Message);
                return cleanupResult.Succeeded ? "OK: " + cleanupResult.Message : "Falló: " + cleanupResult.Message;
            }
            if (result is DeliveryOptimizationOutcome deliveryResult)
            {
                _engine.RecordNonReversibleAction("Guided." + item.Id, item.Title, false, deliveryResult.Message);
                return deliveryResult.Succeeded ? "OK: " + deliveryResult.Message : "Falló: " + deliveryResult.Message;
            }
            return "Falló: no se reconoce esta acción de limpieza.";
        }

        var setting = item.Id switch
        {
            "hibernate" => _engine.GetSetting("Power.Hibernate"),
            _ when item.Id.StartsWith("dns.", StringComparison.Ordinal) => _engine.GetSetting("Network.Dns"),
            _ when item.Id.StartsWith("startup.", StringComparison.Ordinal) => _engine.Settings
                .OfType<StartupProgramSetting>().FirstOrDefault(s => item.Id == "startup." + s.ProgramKeyPath + "|" + s.ProgramValueName),
            _ => null
        };
        if (setting == null) return "Falló: el ajuste ya no está disponible.";
        if (setting.RequiresAdmin && !_isAdmin()) return "Falló: requiere permisos de administrador. Usa «Reiniciar como administrador» y vuelve a intentarlo.";
        object? value = item.Id.StartsWith("dns.", StringComparison.Ordinal) ? item.Id[4..] : setting.ApplyValue;
        var apply = await Task.Run(() => _engine.ApplySetting(setting.Key, value, _dryRun));
        return apply.Success ? "OK: " + apply.Message : "Falló: " + apply.Message;
    }

    private void RevertReversible()
    {
        if (_dryRun) { Status = "Simulación de reversión: no se cambió Windows."; _ = _engine.RevertAll(true); return; }
        var result = _engine.RevertAll(false);
        Status = result.Success ? "Se revirtieron los ajustes reversibles con copia original. La limpieza no se puede deshacer." : "Algunas reversiones fallaron: " + result.Message;
    }

    private async Task CheckBootBaselineAsync()
    {
        try
        {
            var saved = await Task.Run(() => _baseline.Load());
            var current = await Task.Run(() => _source.GetLastBootTimeSeconds());
            var currentSession = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

            // La línea base se guarda en el primer análisis (aunque no se ejecute la guía), de modo
            // que la comparación funciona tras cualquier reinicio posterior.
            if (saved == null)
            {
                var fresh = new BootBaseline(currentSession, DateTime.UtcNow, current);
                await Task.Run(() => _baseline.Save(fresh));
                BootComparison = current.HasValue
                    ? $"Línea base de arranque guardada: {current:0.0} s (evento 100). Tras reiniciar se comparará aquí."
                    : "Línea base de arranque guardada; el tiempo de arranque todavía no está disponible (puede requerir administrador).";
                return;
            }

            if (currentSession <= saved.BootSessionUtc.AddSeconds(5))
            {
                BootComparison = "La comparación aparecerá después del próximo reinicio.";
                return;
            }

            BootComparison = saved.BootSeconds.HasValue && current.HasValue
                ? $"Arranque tras reiniciar: {saved.BootSeconds:0.0} s → {current:0.0} s (evento 100; la diferencia puede depender de otros cambios)."
                : "Se detectó un reinicio, pero el tiempo de arranque no está disponible (puede requerir administrador).";
            await Task.Run(() => _baseline.Save(new BootBaseline(currentSession, DateTime.UtcNow, current)));
        }
        catch { BootComparison = "Tiempo de arranque no disponible."; }
    }

    private void AddStep(string title, string message, bool succeeded) => Steps.Add(new(title, message, succeeded));
    private void UpdateLastStep(string message, bool succeeded) { if (Steps.Count == 0) return; Steps[^1].Message = message; Steps[^1].Succeeded = succeeded; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class GuidedStep : INotifyPropertyChanged
{
    private string _message; private bool _succeeded;
    public string Title { get; }
    public string Message { get => _message; set { _message = value; PropertyChanged?.Invoke(this, new(nameof(Message))); } }
    public bool Succeeded { get => _succeeded; set { _succeeded = value; PropertyChanged?.Invoke(this, new(nameof(Succeeded))); } }
    public GuidedStep(string title, string message, bool succeeded) { Title = title; _message = message; _succeeded = succeeded; }
    public event PropertyChangedEventHandler? PropertyChanged;
}
