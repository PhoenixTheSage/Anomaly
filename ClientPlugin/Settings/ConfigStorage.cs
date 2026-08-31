using System;
using System.IO;
using System.Xml.Serialization;
using VRage.FileSystem;
using VRage.Utils;

namespace ClientPlugin.Settings;

public static class ConfigStorage
{
    private static readonly string ConfigFileName = Plugin.Name + ".cfg";
    private static string ConfigFilePath => Path.Combine(MyFileSystem.UserDataPath, "Storage", ConfigFileName);

    public static void Save(Config config)
    {
        var path = ConfigFilePath;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            using var text = File.CreateText(path);
            new XmlSerializer(typeof(Config)).Serialize(text, config);
        }
        catch (Exception e)
        {
            MyLog.Default.Warning(
                $"{ConfigFileName}: Failed to save config file: {e.GetType().Name}: {e.Message} ({path})");
        }
    }

    public static Config Load()
    {
        var path = ConfigFilePath;
        if (!File.Exists(path))
            return Config.Default;

        var xmlSerializer = new XmlSerializer(typeof(Config));
        try
        {
            using var streamReader = File.OpenText(path);
            return (Config)xmlSerializer.Deserialize(streamReader) ?? Config.Default;
        }
        catch (Exception e)
        {
            MyLog.Default.Warning(
                $"{ConfigFileName}: Failed to read config file: {e.GetType().Name}: {e.Message} ({ConfigFilePath})");
        }

        return Config.Default;
    }
}
