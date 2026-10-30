namespace PC_Optimizer.Services;

using PC_Optimizer.Models;

public interface ISystemInfoService
{
    SystemMetrics GetCurrentMetrics();
}
