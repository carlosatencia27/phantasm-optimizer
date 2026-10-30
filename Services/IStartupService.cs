namespace PC_Optimizer.Services;

public interface IStartupService
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
}
