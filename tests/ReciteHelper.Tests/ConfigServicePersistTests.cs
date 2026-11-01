using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.Enums;
using ReciteHelper.Infrastructure.Configuration;
using Xunit;

namespace ReciteHelper.Tests;

public class ConfigServicePersistTests
{
    [Fact]
    public void CloneForPersist_CopiesEveryPublicProperty()
    {
        // Regression guard: every property added to ConfigOptions must be carried
        // through the persist clone, otherwise saving the configuration silently
        // resets that setting to its default (this actually dropped the four
        // API endpoint/model overrides once already).
        var source = new ConfigOptions
        {
            Version = "test-version",
            DeepSeekKey = "deepseek-key",
            QwenKey = "qwen-key",
            OpenRouterKey = "openrouter-key",
            DeepSeekApiEndpoint = "https://deepseek.example.com",
            DeepSeekChatModel = "custom-chat-model",
            QwenApiEndpoint = "https://qwen.example.com",
            QwenEmbeddingModel = "custom-embedding-model",
            ResourceCenterServerUrl = "https://rc.example.com",
            HostedServiceUrl = "https://hosted.example.com",
            HostedLicenseCode = "license-code",
            HostedLicenseId = "license-id",
            RStandard = 42,
            EnableAntiDebug = true,
            Strategy = MissingStrategy.Replay
        };

        var clone = InvokeCloneForPersist(source);

        foreach (var property in typeof(ConfigOptions).GetProperties())
        {
            if (property.Name is nameof(ConfigOptions.PhonkOptions))
                continue; // nested options object, compared by reference below

            var expected = property.GetValue(source);
            var actual = property.GetValue(clone);

            if (IsSecret(property.Name))
            {
                var text = actual as string;
                Assert.True(
                    text is null || text.StartsWith("enc:v1:", StringComparison.Ordinal),
                    $"{property.Name} should be DPAPI-encrypted for persistence.");
                continue;
            }

            Assert.True(
                Equals(expected, actual),
                $"CloneForPersist dropped property {property.Name}: expected '{expected}', got '{actual}'.");
        }

        Assert.Same(source.PhonkOptions, clone.PhonkOptions);
    }

    private static bool IsSecret(string propertyName)
    {
        return propertyName is nameof(ConfigOptions.DeepSeekKey)
            or nameof(ConfigOptions.QwenKey)
            or nameof(ConfigOptions.OpenRouterKey)
            or nameof(ConfigOptions.HostedLicenseCode)
            or nameof(ConfigOptions.HostedLicenseId);
    }

    private static ConfigOptions InvokeCloneForPersist(ConfigOptions source)
    {
        var method = typeof(ConfigService).GetMethod(
            "CloneForPersist",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (ConfigOptions)method!.Invoke(null, [source])!;
    }
}
