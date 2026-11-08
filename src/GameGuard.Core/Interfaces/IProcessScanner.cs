using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IProcessScanner
{
    IReadOnlyList<DetectedProcessInfo> ScanProcesses();
}
