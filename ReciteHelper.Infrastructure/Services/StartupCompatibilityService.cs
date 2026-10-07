using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Infrastructure.Utilities;

namespace ReciteHelper.Infrastructure.Services;

public sealed class StartupCompatibilityService(IConfigService configService) : IStartupCompatibilityService
{
    public void Initialize()
    {
        // Deformity.HorribleMethod emits deliberately invalid IL to confuse
        // decompiler stack traces. That code is fragile: it pollutes crash logs and
        // can turn into a real crash when the .NET runtime changes. It therefore
        // runs in isolation and stays opt-in via Config.xml (EnableAntiDebug).
        try
        {
            var config = configService.LoadAsync().GetAwaiter().GetResult();
            if (!config.EnableAntiDebug)
                return;

            Deformity.HorribleMethod();
        }
        catch (Exception)
        {
            // Anti-tamper must never take the application down or break startup.
        }
    }
}
