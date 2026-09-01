using System.Text.Json.Serialization;

namespace CurseForgeUpdateMonitor;

// ---------- config.json ----------

public class AppConfig
{
    /// <summary>Your CurseForge Core API key (https://console.curseforge.com/).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>How often to poll, in seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>Full path to the bat file to invoke when any project has an update.</summary>
    public string BatFilePath { get; set; } = "";

    /// <summary>Path to the library.json state file (relative paths resolve next to the exe).</summary>
    public string LibraryFilePath { get; set; } = "library.json";

    /// <summary>CurseForge project (mod) IDs to watch, as a single comma-delimited line
    /// (e.g. "955333,985370,942249"). Whitespace around each ID is ignored.</summary>
    public string ProjectIds { get; set; } = "";

    /// <summary>Seconds to allow the bat file to run before giving up on waiting for it (0 = wait forever).</summary>
    public int BatFileTimeoutSeconds { get; set; } = 0;

    /// <summary>Optional crash monitor for the actual server processes (independent of mod-update checking).</summary>
    public CrashMonitorConfig CrashMonitor { get; set; } = new();
}

public class CrashMonitorConfig
{
    /// <summary>Master on/off switch for the crash monitor feature.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>How often to check whether each server's process is still running, in seconds.</summary>
    public int CheckIntervalSeconds { get; set; } = 30;

    /// <summary>Minimum seconds to wait between restart attempts for the same server, to avoid restart-loop spam
    /// if something is wrong beyond a simple crash (e.g. a bad path).</summary>
    public int RestartCooldownSeconds { get; set; } = 120;

    public List<MonitoredServer> Servers { get; set; } = new();
}

public class MonitoredServer
{
    /// <summary>Friendly name for logging (e.g. "Aberration").</summary>
    public string Name { get; set; } = "";

    /// <summary>Full path to that server's AsaApiLoader.exe (or whichever exe the run.cmd launches).
    /// Used to tell this server's process apart from other servers running the same exe name.</summary>
    public string ProcessPath { get; set; } = "";

    /// <summary>Full path to that server's own run.cmd, re-run as-is to bring it back up.</summary>
    public string RunCmdPath { get; set; } = "";
}

// ---------- library.json ----------

public class LibraryState
{
    // Keyed by project ID as a string (System.Text.Json dictionary keys are strings).
    public Dictionary<string, LibraryEntry> Projects { get; set; } = new();
}

public class LibraryEntry
{
    public int ProjectId { get; set; }
    public string Name { get; set; } = "";
    public int LastFileId { get; set; }
    public string LastFileName { get; set; } = "";
    public DateTimeOffset? LastFileDate { get; set; }
    public DateTimeOffset LastChecked { get; set; }
}

// ---------- CurseForge API (batch GetMods: POST /v1/mods) ----------

public class CurseForgeBatchRequest
{
    [JsonPropertyName("modIds")]
    public List<int> ModIds { get; set; } = new();
}

public class CurseForgeBatchResponse
{
    [JsonPropertyName("data")]
    public List<CurseForgeMod> Data { get; set; } = new();
}

public class CurseForgeMod
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("latestFiles")]
    public List<CurseForgeFile> LatestFiles { get; set; } = new();
}

public class CurseForgeFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("fileDate")]
    public DateTimeOffset FileDate { get; set; }
}
