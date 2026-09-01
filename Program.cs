using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CurseForgeUpdateMonitor;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string ExeDirectory =
        AppContext.BaseDirectory;

    private static string LogFilePath =>
        Path.Combine(ExeDirectory, "monitor.log");

    private static AppConfig _config = new();
    private static string _configPath = "";
    private static string _libraryPath = "";
    private static List<int> _projectIds = new();

    public static async Task<int> Main(string[] args)
    {
        _configPath = args.Length > 0
            ? args[0]
            : Path.Combine(ExeDirectory, "config.json");

        if (!File.Exists(_configPath))
        {
            Log($"config.json not found at '{_configPath}'. Copy config.example.json to config.json next to the exe and fill it in.");
            return 1;
        }

        var configReadOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _config = JsonSerializer.Deserialize<AppConfig>(await File.ReadAllTextAsync(_configPath), configReadOptions)
                  ?? throw new InvalidOperationException("config.json is empty or invalid.");

        // Resolve library.json relative to the exe if a relative path was given.
        _libraryPath = Path.IsPathRooted(_config.LibraryFilePath)
            ? _config.LibraryFilePath
            : Path.Combine(ExeDirectory, _config.LibraryFilePath);

        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            Log("config.json: apiKey is empty. Get one at https://console.curseforge.com/");
            return 1;
        }

        _projectIds = ParseProjectIds(_config.ProjectIds);
        if (_projectIds.Count == 0)
        {
            Log("config.json: projectIds is empty or contained no valid IDs — nothing to watch. " +
                "Expected a comma-delimited line like \"955333,985370,942249\".");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(_config.BatFilePath) || !File.Exists(_config.BatFilePath))
        {
            Log($"WARNING: batFilePath '{_config.BatFilePath}' was not found. The app will keep polling, " +
                "but will fail to run the bat file if an update is detected until this is fixed.");
        }

        using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.curseforge.com") };
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Add("x-api-key", _config.ApiKey);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Log("Shutdown requested (Ctrl+C). Finishing current cycle then exiting.");
            cts.Cancel();
        };

        Log($"Starting. Watching {_projectIds.Count} project ID(s), " +
            $"polling every {_config.PollIntervalSeconds}s. Library file: {_libraryPath}");

        if (_config.CrashMonitor.Enabled)
        {
            if (_config.CrashMonitor.Servers.Count == 0)
            {
                Log("WARNING: crashMonitor.enabled is true but no servers are configured — nothing to watch.");
            }
            else
            {
                Log($"Crash monitor enabled: watching {_config.CrashMonitor.Servers.Count} server process(es), " +
                    $"checking every {_config.CrashMonitor.CheckIntervalSeconds}s.");
            }
        }

        var modUpdateTask = ModUpdateLoopAsync(httpClient, cts.Token);
        var crashMonitorTask = _config.CrashMonitor.Enabled
            ? CrashMonitorLoopAsync(cts.Token)
            : Task.CompletedTask;

        await Task.WhenAll(modUpdateTask, crashMonitorTask);

        Log("Stopped.");
        return 0;
    }

    private static async Task ModUpdateLoopAsync(HttpClient httpClient, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RunOneCycleAsync(httpClient);
            }
            catch (Exception ex)
            {
                Log($"ERROR during poll cycle: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _config.PollIntervalSeconds)), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static async Task CrashMonitorLoopAsync(CancellationToken token)
    {
        // Tracks the last restart attempt per server (by name) so a server that keeps failing
        // to come up doesn't get hammered with restart attempts every single check interval.
        var lastRestartAttempt = new Dictionary<string, DateTimeOffset>();

        while (!token.IsCancellationRequested)
        {
            foreach (var server in _config.CrashMonitor.Servers)
            {
                try
                {
                    CheckAndRestartIfCrashed(server, lastRestartAttempt);
                }
                catch (Exception ex)
                {
                    Log($"ERROR checking server '{server.Name}': {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _config.CrashMonitor.CheckIntervalSeconds)), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static void CheckAndRestartIfCrashed(MonitoredServer server, Dictionary<string, DateTimeOffset> lastRestartAttempt)
    {
        if (string.IsNullOrWhiteSpace(server.ProcessPath))
        {
            Log($"WARNING: crashMonitor server '{server.Name}' has no processPath configured; skipping.");
            return;
        }

        if (IsProcessRunning(server.ProcessPath))
        {
            return;
        }

        if (lastRestartAttempt.TryGetValue(server.Name, out var lastAttempt))
        {
            var elapsed = DateTimeOffset.UtcNow - lastAttempt;
            var cooldown = TimeSpan.FromSeconds(Math.Max(0, _config.CrashMonitor.RestartCooldownSeconds));
            if (elapsed < cooldown)
            {
                Log($"'{server.Name}' is still down but was restarted {elapsed.TotalSeconds:F0}s ago " +
                    $"(cooldown {cooldown.TotalSeconds:F0}s) — waiting before trying again.");
                return;
            }
        }

        Log($"CRASH DETECTED: '{server.Name}' — no running process found at '{server.ProcessPath}'. Restarting via {server.RunCmdPath}.");
        lastRestartAttempt[server.Name] = DateTimeOffset.UtcNow;
        RestartServer(server);
    }

    private static bool IsProcessRunning(string processPath)
    {
        var exeName = Path.GetFileNameWithoutExtension(processPath);
        foreach (var proc in Process.GetProcessesByName(exeName))
        {
            try
            {
                var modulePath = proc.MainModule?.FileName;
                if (modulePath is not null && string.Equals(modulePath, processPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // MainModule can throw (access denied, 32/64-bit mismatch, process exited mid-check) — treat as "can't confirm", not a match.
            }
            finally
            {
                proc.Dispose();
            }
        }

        return false;
    }

    private static void RestartServer(MonitoredServer server)
    {
        if (string.IsNullOrWhiteSpace(server.RunCmdPath) || !File.Exists(server.RunCmdPath))
        {
            Log($"ERROR: cannot restart '{server.Name}' — runCmdPath '{server.RunCmdPath}' does not exist.");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = server.RunCmdPath,
                WorkingDirectory = Path.GetDirectoryName(server.RunCmdPath) ?? ExeDirectory,
                UseShellExecute = true,
            };

            Process.Start(psi);
            Log($"Restart command issued for '{server.Name}'.");
        }
        catch (Exception ex)
        {
            Log($"ERROR restarting '{server.Name}': {ex.Message}");
        }
    }

    private static async Task RunOneCycleAsync(HttpClient httpClient)
    {
        var library = LoadLibrary();

        var request = new CurseForgeBatchRequest { ModIds = _projectIds };
        using var response = await httpClient.PostAsJsonAsync("/v1/mods", request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Log($"CurseForge API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            return;
        }

        var batch = await response.Content.ReadFromJsonAsync<CurseForgeBatchResponse>()
                    ?? new CurseForgeBatchResponse();

        var returnedIds = new HashSet<int>(batch.Data.Select(m => m.Id));
        foreach (var missingId in _projectIds.Where(id => !returnedIds.Contains(id)))
        {
            Log($"WARNING: project ID {missingId} was not returned by the API (deleted, private, or invalid ID?).");
        }

        var updatedProjects = new List<string>();

        foreach (var mod in batch.Data)
        {
            var latestFile = mod.LatestFiles
                .OrderByDescending(f => f.FileDate)
                .ThenByDescending(f => f.Id)
                .FirstOrDefault();

            if (latestFile is null)
            {
                Log($"WARNING: project '{mod.Name}' (ID {mod.Id}) has no files returned; skipping.");
                continue;
            }

            var key = mod.Id.ToString();
            var isKnown = library.Projects.TryGetValue(key, out var existing);
            var isUpdate = !isKnown || existing!.LastFileId != latestFile.Id;

            if (isUpdate)
            {
                Log(isKnown
                    ? $"UPDATE DETECTED: '{mod.Name}' (ID {mod.Id}) — file {existing!.LastFileId} -> {latestFile.Id} ({latestFile.DisplayName})"
                    : $"First check-in for '{mod.Name}' (ID {mod.Id}) — recording file {latestFile.Id} ({latestFile.DisplayName}). Not treated as an update.");

                // Only treat it as an actionable update if we had a prior known state.
                // (Otherwise every project would "update" on the very first run.)
                if (isKnown)
                {
                    updatedProjects.Add($"{mod.Name} (ID {mod.Id}): {existing!.LastFileId} -> {latestFile.Id}");
                }
            }

            library.Projects[key] = new LibraryEntry
            {
                ProjectId = mod.Id,
                Name = mod.Name,
                LastFileId = latestFile.Id,
                LastFileName = string.IsNullOrEmpty(latestFile.DisplayName) ? latestFile.FileName : latestFile.DisplayName,
                LastFileDate = latestFile.FileDate,
                LastChecked = DateTimeOffset.UtcNow,
            };
        }

        SaveLibrary(library);

        if (updatedProjects.Count > 0)
        {
            Log($"{updatedProjects.Count} project(s) updated this cycle:\n  - " + string.Join("\n  - ", updatedProjects));
            RunBatFile();
        }
    }

    private static void RunBatFile()
    {
        if (string.IsNullOrWhiteSpace(_config.BatFilePath) || !File.Exists(_config.BatFilePath))
        {
            Log($"ERROR: cannot run bat file — '{_config.BatFilePath}' does not exist.");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _config.BatFilePath,
                WorkingDirectory = Path.GetDirectoryName(_config.BatFilePath) ?? ExeDirectory,
                UseShellExecute = false,
            };

            Log($"Running bat file: {_config.BatFilePath}");
            using var process = Process.Start(psi);
            if (process is null)
            {
                Log("ERROR: Process.Start returned null for the bat file.");
                return;
            }

            var timeoutMs = _config.BatFileTimeoutSeconds > 0
                ? _config.BatFileTimeoutSeconds * 1000
                : -1; // wait indefinitely

            var exited = timeoutMs == -1 ? process.WaitForExit(-1) is var _ && true : process.WaitForExit(timeoutMs);

            if (!exited)
            {
                Log($"WARNING: bat file did not exit within {_config.BatFileTimeoutSeconds}s; leaving it running and continuing to poll.");
            }
            else
            {
                Log($"Bat file finished with exit code {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR running bat file: {ex.Message}");
        }
    }

    private static LibraryState LoadLibrary()
    {
        if (!File.Exists(_libraryPath))
        {
            return new LibraryState();
        }

        try
        {
            var text = File.ReadAllText(_libraryPath);
            return JsonSerializer.Deserialize<LibraryState>(text) ?? new LibraryState();
        }
        catch (Exception ex)
        {
            Log($"WARNING: failed to read library.json ({ex.Message}); starting with an empty library.");
            return new LibraryState();
        }
    }

    private static void SaveLibrary(LibraryState library)
    {
        var tempPath = _libraryPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(library, JsonOptions));
        File.Move(tempPath, _libraryPath, overwrite: true);
    }

    private static List<int> ParseProjectIds(string csvLine)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(csvLine))
        {
            return ids;
        }

        foreach (var part in csvLine.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id))
            {
                ids.Add(id);
            }
            else
            {
                Log($"WARNING: config.json projectIds — could not parse '{part}' as a number; skipping it.");
            }
        }

        return ids;
    }

    private static void Log(string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}";
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
        catch
        {
            // Don't crash the monitor over a logging failure.
        }
    }
}
