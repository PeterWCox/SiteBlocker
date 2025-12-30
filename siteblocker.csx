#!/usr/bin/env dotnet-script
// SiteBlocker - A local DNS blocker for focus sessions
// Similar to freedom.to, blocks distracting websites via /etc/hosts

#r "nuget: System.Text.Json, 8.0.0"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text;

// Configuration
// Try to find config.json relative to script location or current directory
var configFile = "config.json";
if (!File.Exists(configFile))
{
    // If not in current dir, try script directory
    var scriptPath = Environment.GetCommandLineArgs().FirstOrDefault() ?? "";
    if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
    {
        var scriptDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
        if (!string.IsNullOrEmpty(scriptDir))
        {
            var altConfig = Path.Combine(scriptDir, "config.json");
            if (File.Exists(altConfig))
            {
                configFile = altConfig;
            }
        }
    }
}
var lockFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".siteblocker.lock");
var hostsFile = "/etc/hosts";
var markerStart = "# SiteBlocker START";
var markerEnd = "# SiteBlocker END";

// Find HTML file path
var htmlFile = "blocked.html";
if (!File.Exists(htmlFile))
{
    var scriptPath = Environment.GetCommandLineArgs().FirstOrDefault() ?? "";
    if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
    {
        var scriptDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
        if (!string.IsNullOrEmpty(scriptDir))
        {
            var altHtml = Path.Combine(scriptDir, "blocked.html");
            if (File.Exists(altHtml))
            {
                htmlFile = altHtml;
            }
        }
    }
}

class Config
{
    public List<string> blocklist { get; set; } = new List<string>();
    public string redirect_ip { get; set; } = "127.0.0.1";
}

class SiteBlocker
{
    private Config config;
    private string lockFilePath;
    private string configFilePath;
    private string hostsFilePath;
    private string htmlFilePath;
    private string markerStart;
    private string markerEnd;
    private HttpListener httpListener;
    private CancellationTokenSource serverCancellation;
    private Task serverTask;

    public SiteBlocker(string configFile, string lockFile, string hostsFile, string htmlFile, string markerStart, string markerEnd)
    {
        this.configFilePath = configFile;
        this.lockFilePath = lockFile;
        this.hostsFilePath = hostsFile;
        this.htmlFilePath = htmlFile;
        this.markerStart = markerStart;
        this.markerEnd = markerEnd;
        this.config = LoadConfig();
    }

    private Config LoadConfig()
    {
        if (!File.Exists(configFilePath))
        {
            Console.WriteLine($"Error: Config file not found at {configFilePath}");
            Environment.Exit(1);
        }

        var json = File.ReadAllText(configFilePath);
        return JsonSerializer.Deserialize<Config>(json) ?? new Config();
    }

    public bool IsActive()
    {
        return File.Exists(lockFilePath);
    }

    public TimeSpan GetActiveDuration()
    {
        if (!IsActive())
            return TimeSpan.Zero;

        try
        {
            var startTimeStr = File.ReadAllText(lockFilePath).Trim();
            var startTime = DateTime.Parse(startTimeStr);
            return DateTime.Now - startTime;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error reading lock file: {e.Message}");
            return TimeSpan.Zero;
        }
    }

    public string FormatDuration(TimeSpan duration)
    {
        var totalSeconds = (int)duration.TotalSeconds;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        if (hours > 0)
            return $"{hours}h {minutes}m {seconds}s";
        else if (minutes > 0)
            return $"{minutes}m {seconds}s";
        else
            return $"{seconds}s";
    }

    private List<string> ReadHosts()
    {
        try
        {
            return File.ReadAllLines(hostsFilePath).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine("Error: Need sudo privileges to modify /etc/hosts");
            Environment.Exit(1);
            return new List<string>();
        }
    }

    private void WriteHosts(List<string> lines)
    {
        try
        {
            File.WriteAllLines(hostsFilePath, lines);
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine("Error: Need sudo privileges to modify /etc/hosts");
            Environment.Exit(1);
        }
    }

    private List<string> RemoveBlockerEntries(List<string> lines)
    {
        var result = new List<string>();
        var inBlockerSection = false;

        foreach (var line in lines)
        {
            if (line.Contains(markerStart))
            {
                inBlockerSection = true;
                continue;
            }
            if (line.Contains(markerEnd))
            {
                inBlockerSection = false;
                continue;
            }
            if (!inBlockerSection)
            {
                result.Add(line);
            }
        }

        return result;
    }

    private List<string> AddBlockerEntries(List<string> lines)
    {
        // Remove existing entries first
        lines = RemoveBlockerEntries(lines);

        // Add new entries
        lines.Add($"");
        lines.Add(markerStart);
        
        foreach (var domain in config.blocklist)
        {
            lines.Add($"{config.redirect_ip} {domain}");
        }
        
        lines.Add(markerEnd);
        return lines;
    }

    private void StartHttpServer()
    {
        try
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://127.0.0.1:80/");
            httpListener.Start();
            
            serverCancellation = new CancellationTokenSource();
            serverTask = RunHttpServer(serverCancellation.Token);
            
            Console.WriteLine("✓ HTTP server started on port 80");
        }
        catch (HttpListenerException ex)
        {
            if (ex.ErrorCode == 5) // Access denied - try port 8080
            {
                Console.WriteLine("Port 80 requires root. Trying port 8080...");
                try
                {
                    httpListener = new HttpListener();
                    httpListener.Prefixes.Add("http://127.0.0.1:8080/");
                    httpListener.Start();
                    
                    serverCancellation = new CancellationTokenSource();
                    serverTask = Task.Run(() => RunHttpServer(serverCancellation.Token));
                    
                    Console.WriteLine("✓ HTTP server started on port 8080");
                    Console.WriteLine("Note: You may need to update /etc/hosts to redirect to 127.0.0.1:8080");
                }
                catch (Exception e2)
                {
                    Console.WriteLine($"Warning: Could not start HTTP server: {e2.Message}");
                    Console.WriteLine("Blocked sites will show connection errors instead of the focus page.");
                }
            }
            else
            {
                Console.WriteLine($"Warning: Could not start HTTP server: {ex.Message}");
                Console.WriteLine("Blocked sites will show connection errors instead of the focus page.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not start HTTP server: {ex.Message}");
            Console.WriteLine("Blocked sites will show connection errors instead of the focus page.");
        }
    }

    private async Task RunHttpServer(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && httpListener != null && httpListener.IsListening)
        {
            try
            {
                var context = await httpListener.GetContextAsync();
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var response = context.Response;
                        response.ContentType = "text/html; charset=utf-8";
                        response.StatusCode = 200;
                        
                        if (File.Exists(htmlFilePath))
                        {
                            var htmlContent = File.ReadAllText(htmlFilePath);
                            var buffer = Encoding.UTF8.GetBytes(htmlContent);
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
                        }
                        else
                        {
                            var defaultHtml = "<html><body><h1>Focus Mode Active</h1><p>This site is blocked.</p></body></html>";
                            var buffer = Encoding.UTF8.GetBytes(defaultHtml);
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
                        }
                        
                        response.OutputStream.Close();
                    }
                    catch
                    {
                        // Ignore errors
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                // Listener was closed
                break;
            }
            catch (Exception)
            {
                // Ignore other errors and continue
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private void StopHttpServer()
    {
        if (httpListener != null && httpListener.IsListening)
        {
            serverCancellation?.Cancel();
            httpListener.Stop();
            httpListener.Close();
            httpListener = null;
            Console.WriteLine("✓ HTTP server stopped");
        }
    }

    public void Activate()
    {
        if (IsActive())
        {
            var duration = GetActiveDuration();
            Console.WriteLine($"SiteBlocker is already active (running for {FormatDuration(duration)})");
            return;
        }

        Console.WriteLine("Activating SiteBlocker...");

        // Create lock file with start time
        File.WriteAllText(lockFilePath, DateTime.Now.ToString("O"));

        // Start HTTP server
        StartHttpServer();

        // Modify /etc/hosts
        var lines = ReadHosts();
        lines = AddBlockerEntries(lines);
        WriteHosts(lines);

        Console.WriteLine("✓ SiteBlocker activated!");
        Console.WriteLine($"Blocking {config.blocklist.Count} domains");
        Console.WriteLine("\nPress Ctrl+C to deactivate");
    }

    public void Deactivate()
    {
        if (!IsActive())
        {
            Console.WriteLine("SiteBlocker is not active");
            return;
        }

        var duration = GetActiveDuration();
        Console.WriteLine($"Deactivating SiteBlocker (was active for {FormatDuration(duration)})...");

        // Stop HTTP server
        StopHttpServer();

        // Remove entries from /etc/hosts
        var lines = ReadHosts();
        lines = RemoveBlockerEntries(lines);
        WriteHosts(lines);

        // Remove lock file
        if (File.Exists(lockFilePath))
        {
            File.Delete(lockFilePath);
        }

        Console.WriteLine("✓ SiteBlocker deactivated!");
    }

    public void Status()
    {
        if (IsActive())
        {
            var duration = GetActiveDuration();
            Console.WriteLine("SiteBlocker is ACTIVE");
            Console.WriteLine($"Duration: {FormatDuration(duration)}");
            Console.WriteLine($"Blocking {config.blocklist.Count} domains");
        }
        else
        {
            Console.WriteLine("SiteBlocker is INACTIVE");
        }
    }

    public void RunInteractive()
    {
        if (!IsActive())
        {
            Console.WriteLine("SiteBlocker is not active. Use 'activate' command first.");
            return;
        }

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n\nDeactivating...");
            Deactivate();
            Environment.Exit(0);
        };

        Console.WriteLine("SiteBlocker is active. Timer running...");
        Console.WriteLine("Press Ctrl+C to deactivate\n");

        try
        {
            while (true)
            {
                var duration = GetActiveDuration();
                Console.Write($"\rActive for: {FormatDuration(duration)}");
                Thread.Sleep(1000);
            }
        }
        catch (Exception)
        {
            Console.WriteLine("\n\nDeactivating...");
            Deactivate();
        }
    }
}

// Main
var blocker = new SiteBlocker(configFile, lockFile, hostsFile, htmlFile, markerStart, markerEnd);
var args = Environment.GetCommandLineArgs();

// dotnet-script passes: [dotnet-script path, script path, ...args]
// We need to find the first argument that's not a script path
var command = "";
if (args.Length > 1)
{
    // Find first arg that doesn't end with .csx and isn't dotnet-script related
    var possibleCommands = args.Skip(1).Where(a => !a.EndsWith(".csx") && !a.Contains("dotnet-script")).ToList();
    command = possibleCommands.FirstOrDefault()?.ToLower() ?? "";
}

if (string.IsNullOrEmpty(command))
{
    Console.WriteLine("Usage: siteblocker <command>");
    Console.WriteLine("Commands:");
    Console.WriteLine("  activate   - Activate the site blocker");
    Console.WriteLine("  deactivate - Deactivate the site blocker");
    Console.WriteLine("  status     - Show current status");
    Console.WriteLine("  run        - Run interactive mode with timer");
    Environment.Exit(1);
}

switch (command)
{
    case "activate":
        blocker.Activate();
        blocker.RunInteractive();
        break;
    case "deactivate":
        blocker.Deactivate();
        break;
    case "status":
        blocker.Status();
        break;
    case "run":
        blocker.RunInteractive();
        break;
    default:
        Console.WriteLine($"Unknown command: {command}");
        Environment.Exit(1);
        break;
}

