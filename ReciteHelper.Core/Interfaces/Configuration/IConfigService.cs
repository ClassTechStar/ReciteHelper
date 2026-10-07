using ReciteHelper.Core.Configuration;

namespace ReciteHelper.Core.Interfaces.Configuration;

public interface IConfigService
{
    /// <summary>Active configuration file path (after %APPDATA% migration).</summary>
    string ConfigPath { get; }

    Task<ConfigOptions> LoadAsync();
    Task SaveAsync(ConfigOptions config);
}
 