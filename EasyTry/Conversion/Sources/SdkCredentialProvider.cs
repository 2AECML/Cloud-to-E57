using System.Text.Json;

namespace EasyTry.Conversion.Sources;

internal sealed record ZfsCredential(string Licensee, string Edition, string Key);

internal interface ISdkCredentialProvider
{
    ZfsCredential GetZfsCredential();
    string GetFaroLicenseText();
}

/// <summary>
/// 仅从进程环境或用户本机私有配置读取凭据，不在源码中保存许可证。
/// </summary>
internal sealed class SdkCredentialProvider : ISdkCredentialProvider
{
    private const string ConfigFileName = "sdk.private.json";
    private JsonDocument? config;

    public ZfsCredential GetZfsCredential()
    {
        var licensee = ReadValue("EASYTRY_ZFS_LICENSEE", "Zfs", "Licensee");
        var edition = ReadValue("EASYTRY_ZFS_EDITION", "Zfs", "Edition");
        var key = ReadValue("EASYTRY_ZFS_KEY", "Zfs", "Key");

        if (string.IsNullOrWhiteSpace(licensee) ||
            string.IsNullOrWhiteSpace(edition) ||
            string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "未配置 Z+F SDK 本机私有凭据。请设置 EASYTRY_ZFS_LICENSEE、" +
                "EASYTRY_ZFS_EDITION、EASYTRY_ZFS_KEY，或在本机私有配置中提供。");
        }

        return new ZfsCredential(licensee, edition, key);
    }

    public string GetFaroLicenseText()
    {
        var licenseText = ReadValue("EASYTRY_FARO_LICENSE_TEXT", "Faro", "LicenseText");
        if (string.IsNullOrWhiteSpace(licenseText))
        {
            throw new InvalidOperationException(
                "未配置 FARO SDK 本机私有许可证文本。请设置 EASYTRY_FARO_LICENSE_TEXT，" +
                "或在本机私有配置中提供。");
        }

        return licenseText;
    }

    private string? ReadValue(string environmentName, string section, string property)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        config ??= LoadConfig();
        if (config is null ||
            !config.RootElement.TryGetProperty(section, out var sectionElement) ||
            !sectionElement.TryGetProperty(property, out var propertyElement))
        {
            return null;
        }

        return propertyElement.GetString();
    }

    private static JsonDocument? LoadConfig()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasyTry",
            ConfigFileName);

        return File.Exists(path)
            ? JsonDocument.Parse(File.ReadAllText(path))
            : null;
    }
}
