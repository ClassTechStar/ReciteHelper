using ReciteHelper.Core.Enums;
using System.Xml.Serialization;

namespace ReciteHelper.Core.Configuration;

[XmlRoot("Config")]
public class ConfigOptions
{
    public const string SectionName = "ReciteHelper";

    public string Version { get; set; } = "v3";
    public string? DeepSeekKey { get; set; }
    public string? QwenKey { get; set; }

    // API endpoints and model names: defaults match the current service setup and
    // can be overridden in Config.xml without touching code.
    public string DeepSeekApiEndpoint { get; set; } = "https://api.deepseek.com";
    public string DeepSeekChatModel { get; set; } = "deepseek-v4-flash";
    public string QwenApiEndpoint { get; set; } = "https://dashscope.aliyuncs.com/compatible-mode/v1";
    public string QwenEmbeddingModel { get; set; } = "text-embedding-v4";

    public string ResourceCenterServerUrl { get; set; } = "http://localhost:5000";
    public string? HostedServiceUrl { get; set; }
    public string? HostedLicenseCode { get; set; }
    public string? HostedLicenseId { get; set; }
    public int RStandard { get; set; } = 60;
    public bool EnableAntiDebug { get; set; }
    public PhonkOptions PhonkOptions { get; set; } = new();
    [XmlElement("MissingStrategy")]
    public MissingStrategy Strategy { get; set; } = MissingStrategy.Ignore;
}
