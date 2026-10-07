using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.Exceptions;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace ReciteHelper.Infrastructure.Configuration;

public class ConfigService : IConfigService
{
    // Values with this prefix are DPAPI-encrypted (CurrentUser scope). Anything else is
    // treated as plaintext and encrypted the next time the configuration is saved.
    private const string EncryptedPrefix = "enc:v1:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ReciteHelper.Config.v1");

    private readonly string _configPath;
    private readonly string _legacyConfigPath;

    public ConfigService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReciteHelper");
        _configPath = Path.Combine(appDataDirectory, "Config.xml");
        _legacyConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.xml");
    }

    /// <summary>
    /// Active configuration file path. Prefers %APPDATA%\ReciteHelper so the app works when
    /// installed into write-protected locations such as Program Files.
    /// </summary>
    public string ConfigPath => _configPath;

    public async Task<ConfigOptions> LoadAsync()
    {
        var configPath = EnsureConfigPath();
        if (!File.Exists(configPath))
            return new ConfigOptions();

        try
        {
            var serializer = new XmlSerializer(typeof(ConfigOptions));
            await using var stream = File.OpenRead(configPath);
            var config = (ConfigOptions?)serializer.Deserialize(stream);

            if (config is null)
                return new ConfigOptions();

            config.DeepSeekKey = ResolveConfigText(Unprotect(config.DeepSeekKey));
            config.QwenKey = ResolveConfigText(Unprotect(config.QwenKey));
            config.OpenRouterKey = ResolveConfigText(Unprotect(config.OpenRouterKey));
            config.HostedLicenseCode = Unprotect(config.HostedLicenseCode);
            config.HostedLicenseId = Unprotect(config.HostedLicenseId);
            return config;
        }
        catch (Exception ex)
        {
            throw new ConfigurationException($"Failed to load configuration: {ex.Message}.");
        }
    }

    public async Task SaveAsync(ConfigOptions config)
    {
        var configPath = EnsureConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var persisted = CloneForPersist(config);
        var serializer = new XmlSerializer(typeof(ConfigOptions));

        // Atomic write: a crash mid-save leaves the previous configuration intact.
        var tempPath = $"{configPath}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            serializer.Serialize(stream, persisted);
        }

        File.Move(tempPath, configPath, overwrite: true);
    }

    private static ConfigOptions CloneForPersist(ConfigOptions config)
    {
        // Encrypt secret fields for the on-disk representation. Environment-variable
        // placeholders (%...%) are kept verbatim so the indirection keeps working.
        return new ConfigOptions
        {
            Version = config.Version,
            DeepSeekKey = Protect(config.DeepSeekKey),
            QwenKey = Protect(config.QwenKey),
            OpenRouterKey = Protect(config.OpenRouterKey),
            ResourceCenterServerUrl = config.ResourceCenterServerUrl,
            HostedServiceUrl = config.HostedServiceUrl,
            HostedLicenseCode = Protect(config.HostedLicenseCode),
            HostedLicenseId = Protect(config.HostedLicenseId),
            RStandard = config.RStandard,
            PhonkOptions = config.PhonkOptions,
            Strategy = config.Strategy
        };
    }

    private string EnsureConfigPath()
    {
        // First run after upgrading: migrate the config shipped next to the executable
        // into %APPDATA% and use that copy from then on.
        if (!File.Exists(_configPath) && File.Exists(_legacyConfigPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                File.Copy(_legacyConfigPath, _configPath);
            }
            catch (IOException)
            {
                // Fall through: if the copy failed we simply keep using the legacy file.
                return File.Exists(_configPath) ? _configPath : _legacyConfigPath;
            }
        }

        return _configPath;
    }

    private static string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith(EncryptedPrefix, StringComparison.Ordinal) ||
            value.StartsWith('%'))
        {
            return value;
        }

        try
        {
            var encrypted = Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
            return EncryptedPrefix + encrypted;
        }
        catch (CryptographicException)
        {
            return value;
        }
    }

    private static string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return value;

        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(value[EncryptedPrefix.Length..]),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // Not decryptable (edited by hand, copied from another machine, or corrupt):
            // return as-is so callers can still see something meaningful.
            return value;
        }
    }

    private static string? ResolveConfigText(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('%'))
            return text;

        var match = Regex.Match(
            text,
            "^%\\s*Environment\\.GetEnvironmentVariable\\(\"(?<name>[A-Za-z_][A-Za-z0-9_]*)\"\\)\\s*%$");

        if (!match.Success)
            return text;

        return Environment.GetEnvironmentVariable(match.Groups["name"].Value);
    }
}
