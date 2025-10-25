using System.Threading.Tasks;
using NyxMachina.Core;

namespace NyxMachina.Core
{
    public interface ISystem
    {
        public int Priority => 0;
        bool IsInitialized { get; set; }
        public Task<AsyncResult> Init();
        public bool PreSystemShutdown();
        public Task<AsyncResult> ShutdownAsync();
    }
}

public static class ISystemExtension
{
    public static bool IsInitialized(this ISystem system)
    {
        var result = system is { IsInitialized: true };
        return result;
    }
}