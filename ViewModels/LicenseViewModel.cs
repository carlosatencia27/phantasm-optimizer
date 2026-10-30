using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using PC_Optimizer.Services.Licensing;

namespace PC_Optimizer.ViewModels;

/// <summary>Pantalla «Licencia»: ID de equipo, estado y activación. No usa red.</summary>
public sealed class LicenseViewModel : INotifyPropertyChanged
{
    private readonly LicenseService _service;
    private readonly Func<string?> _pickFile;
    private readonly Action<string> _copyToClipboard;
    private string _machineId = "Calculando…";
    private LicenseCheck? _check;
    private string _pastedText = string.Empty;
    private string _feedback = string.Empty;

    public LicenseViewModel(LicenseService service, Func<string?>? pickFile = null, Action<string>? copyToClipboard = null,
        bool computeInBackground = true)
    {
        _service = service;
        _pickFile = pickFile ?? (() => null);
        _copyToClipboard = copyToClipboard ?? (_ => { });
        CopyIdCommand = new RelayCommand(_ => CopyId(), _ => IdReady);
        ActivateCommand = new RelayCommand(_ => Activate(PastedText), _ => !string.IsNullOrWhiteSpace(PastedText));
        LoadFileCommand = new RelayCommand(_ => LoadFile());
        if (computeInBackground) _ = Task.Run(LoadInitial).ContinueWith(_ => { }, TaskScheduler.Default);
        else LoadInitial();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public ICommand CopyIdCommand { get; }
    public ICommand ActivateCommand { get; }
    public ICommand LoadFileCommand { get; }

    public static string PrivacyText =>
        "Este número se calcula en tu PC a partir de datos de Windows y de la placa base. No contiene tu IP ni tu nombre de usuario, y la app no lo envía por internet. Solo lo compartes tú, si quieres, con quien te da la licencia. La licencia y la última fecha de uso se guardan únicamente en este PC.";

    public string MachineId { get => _machineId; private set { _machineId = value; Raise(); Raise(nameof(IdReady)); } }
    public bool IdReady => _check != null;

    public string PastedText { get => _pastedText; set { _pastedText = value; Raise(); } }
    public string Feedback { get => _feedback; private set { _feedback = value; Raise(); Raise(nameof(HasFeedback)); } }
    public bool HasFeedback => !string.IsNullOrEmpty(_feedback);

    public string StatusMessage => _check?.Message ?? "Comprobando la licencia…";
    public bool IsValid => _check?.IsValid == true;
    /// <summary>true cuando falta una licencia válida: se muestra el aviso fijo de la barra superior.</summary>
    public bool NeedsAttention => _check is { IsValid: false };
    public string StatusTitle => _check is null ? "Comprobando…" : _check.IsValid ? "Licencia activa" : "Sin licencia válida";
    public string SidebarLabel => _check is null ? "Licencia" : _check.IsValid ? "Licencia · activa" : _check.Status == LicenseStatus.NoLicense ? "Licencia · sin activar" : "Licencia · revisar";
    /// <summary>Texto del aviso fijo: «Sin licencia» solo si de verdad no hay ninguna.</summary>
    public string BannerLabel => _check?.Status == LicenseStatus.NoLicense ? "Sin licencia · Activar" : "Licencia no válida · Ver";

    private void LoadInitial()
    {
        var id = _service.MachineId;
        var check = _service.Current;
        Apply(id, check);
    }

    private void Apply(string id, LicenseCheck check)
    {
        void Set()
        {
            _check = check;
            MachineId = id;
            Raise(nameof(StatusMessage)); Raise(nameof(IsValid)); Raise(nameof(NeedsAttention));
            Raise(nameof(StatusTitle)); Raise(nameof(SidebarLabel)); Raise(nameof(BannerLabel));
        }
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) Set(); else d.Invoke(Set);
    }

    private void CopyId()
    {
        _copyToClipboard(MachineId);
        Feedback = "ID copiado. Mándaselo a quien te da la licencia.";
    }

    private void LoadFile()
    {
        var text = _pickFile();
        if (string.IsNullOrWhiteSpace(text)) return;
        PastedText = text.Trim();
        Activate(text);
    }

    public void Activate(string? text)
    {
        if (text is { Length: > 8192 })
        {
            Feedback = "El texto es demasiado largo para ser una licencia. Pega solo el texto que recibiste.";
            return;
        }
        var check = _service.Activate(text);
        Apply(_service.MachineId, check.IsValid ? check : _service.Current);
        Feedback = check.IsValid ? "Licencia activada. " + check.Message : check.Message;
        if (check.IsValid) PastedText = string.Empty;
    }
}
