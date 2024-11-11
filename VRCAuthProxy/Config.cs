using System.Text.Json;

namespace VRCAuthProxy;

public class ConfigAccount
{
    public string username { get; set; }
    public string password { get; set; }
    public string? totpSecret { get; set; }
}

public class iConfig
{
    public List<ConfigAccount> accounts { get; set; }
}

// Load config from appsettings.json
public class Config
{
    private static Config? _instance;
    public List<ConfigAccount> Accounts { get; set; }

    public static Config Instance
    {
        get
        {
            if (_instance == null) _instance = Load();
            return _instance;
        }
    }

    public static Config Load()
    {
        var config = new Config();
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            // load iConfig
            var iConfig = JsonSerializer.Deserialize<iConfig>(json);
            if (iConfig == null) throw new Exception("Failed to load config");
            config.Accounts = iConfig.accounts;
        }
        else
        {
            Console.WriteLine("No config found at " + configPath);
        }

        return config;
    }

    public void Save()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var iConfig = new iConfig
        {
            accounts = Accounts
        };
        var json = JsonSerializer.Serialize(iConfig, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);
    }
}