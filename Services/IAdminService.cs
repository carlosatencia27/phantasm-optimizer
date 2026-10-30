namespace PC_Optimizer.Services;

public interface IAdminService
{
    bool IsRunningAsAdmin { get; }
    bool IsElevationSupported { get; }
    void RequestElevation();
}
