using ReciteHelper.Core.Interfaces.Services;

namespace ReciteHelper.Infrastructure.Services;

public sealed class PromptProvider : IPromptProvider
{
    public async Task<string> GetPromptAsync(string promptName)
    {
        var path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Images",
            "Prompts",
            promptName);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"提示词文件缺失：{promptName}。请恢复程序目录下 Images\\Prompts 文件夹中的内容后重试。",
                path);

        return await File.ReadAllTextAsync(path);
    }
}
