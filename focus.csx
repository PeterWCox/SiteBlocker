#!/usr/bin/env dotnet-script
// SiteBlocker - A local DNS blocker for focus sessions
// Similar to freedom.to, blocks distracting websites via /etc/hosts

#nullable enable
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
// Find script directory by looking for .csx file in command line args
var scriptDir = Directory.GetCurrentDirectory();
var configFile = "config.json";

// Look through all command line args for the .csx script file
foreach (var arg in Environment.GetCommandLineArgs())
{
    if (arg.EndsWith(".csx"))
    {
        var fullPath = Path.GetFullPath(arg);
        if (File.Exists(fullPath))
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                scriptDir = dir;
                break;
            }
        }
    }
}

// Look for config.json in script directory first
var scriptConfig = Path.Combine(scriptDir, "config.json");
if (File.Exists(scriptConfig))
{
    configFile = scriptConfig;
}
else
{
    // Fallback: try current directory
    var currentConfig = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
    if (File.Exists(currentConfig))
    {
        configFile = currentConfig;
    }
}
var lockFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".siteblocker.lock");
var hostsFile = "/etc/hosts";
var markerStart = "# SiteBlocker START";
var markerEnd = "# SiteBlocker END";


class Config
{
    public List<string> blocklist { get; set; } = new List<string>();
    public string redirect_ip { get; set; } = "127.0.0.1";
    public int server_port { get; set; } = 4000;
}

class SiteBlocker
{
    private Config config;
    private string lockFilePath;
    private string configFilePath;
    private string hostsFilePath;
    private string markerStart;
    private string markerEnd;
    private HttpListener? httpListener;
    private Task? serverTask;
    private CancellationTokenSource? cancellationTokenSource;
    private HashSet<string> blockedDomains;

    public SiteBlocker(string configFile, string lockFile, string hostsFile, string markerStart, string markerEnd, string scriptDir)
    {
        this.configFilePath = configFile;
        this.lockFilePath = lockFile;
        this.hostsFilePath = hostsFile;
        this.markerStart = markerStart;
        this.markerEnd = markerEnd;
        this.config = LoadConfig();
        this.blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

    private HashSet<string> ExpandDomains(List<string> domains)
    {
        var expanded = new HashSet<string>(domains);
        
        foreach (var domain in domains)
        {
            // Always include the original domain
            expanded.Add(domain);
            
            // Extract base domain (remove www. prefix)
            var baseDomain = domain;
            var hasWww = baseDomain.StartsWith("www.");
            if (hasWww)
            {
                baseDomain = baseDomain.Substring(4);
            }
            
            // Only expand base domains (not subdomains)
            // A base domain has exactly one dot before the TLD (e.g., "twitter.com", not "news.google.com")
            var parts = baseDomain.Split('.');
            if (parts.Length < 2)
                continue; // Invalid domain format
            
            // Check if this is a base domain (only 2 parts: domain + TLD)
            // Or a UK domain (3 parts: domain + co + uk)
            bool isBaseDomain = false;
            string coreDomain = "";
            string tld = "";
            
            if (parts.Length == 2)
            {
                // Base domain like "twitter.com" or "x.com"
                isBaseDomain = true;
                coreDomain = parts[0];
                tld = parts[1];
            }
            else if (parts.Length == 3 && parts[1] == "co" && parts[2] == "uk")
            {
                // UK domain like "bbc.co.uk"
                isBaseDomain = true;
                coreDomain = parts[0];
                tld = "co.uk";
            }
            
            // Only expand base domains (not subdomains like "news.google.com" or "mobile.twitter.com")
            if (isBaseDomain && !string.IsNullOrEmpty(coreDomain))
            {
                var wwwPrefix = hasWww ? "www." : "";
                
                // If it's a .com domain, add .co.uk variant
                if (tld == "com")
                {
                    expanded.Add($"{wwwPrefix}{coreDomain}.co.uk");
                }
                // If it's a .co.uk domain, add .com variant
                else if (tld == "co.uk")
                {
                    expanded.Add($"{wwwPrefix}{coreDomain}.com");
                }
            }
        }
        
        return expanded;
    }

    private List<string> AddBlockerEntries(List<string> lines)
    {
        // Remove existing entries first
        lines = RemoveBlockerEntries(lines);

        // Expand domains to include both .com and .co.uk variants
        var expandedDomains = ExpandDomains(config.blocklist);
        
        // Store blocked domains for logging
        blockedDomains = expandedDomains;

        // Add new entries
        lines.Add($"");
        lines.Add(markerStart);
        
        foreach (var domain in expandedDomains.OrderBy(d => d))
        {
            lines.Add($"{config.redirect_ip} {domain}");
        }
        
        lines.Add(markerEnd);
        return lines;
    }

    private void LogBlockedRequest(string host, string path)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var domain = host.Split(':')[0]; // Remove port if present
        
        // Check if this domain is in our blocklist
        var isBlocked = blockedDomains.Contains(domain) || 
                       blockedDomains.Any(d => domain.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
        
        if (isBlocked)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    [{timestamp}] 🚫 BLOCKED: {domain}{path}");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("    💪 Stay strong! You're staying focused! 💪");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    [{timestamp}] ⚠️  Request: {domain}{path} (not in blocklist)");
            Console.ResetColor();
        }
    }

    private async Task RunLoggingServerAsync(CancellationToken cancellationToken)
    {
        httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://127.0.0.1:80/");
        httpListener.Prefixes.Add("http://localhost:80/");
        
        try
        {
            httpListener.Start();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("    ✓ Logging server started on port 80");
            Console.ResetColor();
        }
        catch (HttpListenerException ex)
        {
            // Port 80 might require sudo, try a different approach
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    ⚠️  Could not start logging server on port 80: {ex.Message}");
            Console.WriteLine("    Logging will be limited. Run with sudo for full logging.");
            Console.ResetColor();
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await httpListener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                // Log the request
                LogBlockedRequest(request.Headers["Host"] ?? "unknown", request.Url?.PathAndQuery ?? "/");

                // Send a simple response
                var responseString = "<!DOCTYPE html><html><head><title>Site Blocked</title></head><body><h1>Site Blocked</h1><p>This site is blocked by SiteBlocker.</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentType = "text/html";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
                response.Close();
            }
            catch (ObjectDisposedException)
            {
                // Listener was closed
                break;
            }
            catch (HttpListenerException)
            {
                // Connection closed or error
                continue;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    ⚠️  Server error: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private void StartLoggingServer()
    {
        cancellationTokenSource = new CancellationTokenSource();
        serverTask = Task.Run(() => RunLoggingServerAsync(cancellationTokenSource.Token));
    }

    private void StopLoggingServer()
    {
        cancellationTokenSource?.Cancel();
        httpListener?.Stop();
        httpListener?.Close();
        serverTask?.Wait(TimeSpan.FromSeconds(2));
    }


    private void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════════╗
    ║                                                       ║
    ║     ███████╗ ██████╗  ██████╗██╗   ██╗███████╗     ║
    ║     ██╔════╝██╔═══██╗██╔════╝██║   ██║██╔════╝     ║
    ║     █████╗  ██║   ██║██║     ██║   ██║███████╗     ║
    ║     ██╔══╝  ██║   ██║██║     ██║   ██║╚════██║     ║
    ║     ██║     ╚██████╔╝╚██████╗╚██████╔╝███████║     ║
    ║     ╚═╝      ╚═════╝  ╚═════╝ ╚═════╝ ╚══════╝     ║
    ║                                                       ║
    ╚═══════════════════════════════════════════════════════╝
");
        Console.ResetColor();
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("    🚀 Time to focus and achieve greatness! 🚀\n");
        Console.ResetColor();
    }

    private void PrintMotivationalMessage()
    {
        var messages = new[]
        {
            "✨ Every moment of focus is a step toward your goals ✨",
            "🌟 Distractions blocked. Dreams unlocked. 🌟",
            "💪 You've got this! Your future self will thank you. 💪",
            "🎯 Focus is a superpower. You're activating yours now. 🎯",
            "🔥 Turn off the noise. Turn on your potential. 🔥"
        };
        
        var random = new Random();
        var message = messages[random.Next(messages.Length)];
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"    {message}\n");
        Console.ResetColor();
    }

    public void Activate()
    {
        if (IsActive())
        {
            var duration = GetActiveDuration();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n⚠️  SiteBlocker is already active (running for {FormatDuration(duration)})\n");
            Console.ResetColor();
            return;
        }

        PrintBanner();
        Console.WriteLine("    Activating SiteBlocker...\n");

        // Create lock file with start time
        File.WriteAllText(lockFilePath, DateTime.Now.ToString("O"));

        // Modify /etc/hosts
        var lines = ReadHosts();
        lines = AddBlockerEntries(lines);
        WriteHosts(lines);

        // Start logging server
        StartLoggingServer();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("    ✓ SiteBlocker activated!");
        Console.WriteLine($"    ✓ Blocking {config.blocklist.Count} domains");
        Console.ResetColor();
        
        PrintMotivationalMessage();
        
        Console.WriteLine("    Press Ctrl+C to deactivate\n");
    }

    public void Deactivate()
    {
        if (!IsActive())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n⚠️  SiteBlocker is not active\n");
            Console.ResetColor();
            return;
        }

        var duration = GetActiveDuration();
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════════╗
    ║                                                       ║
    ║              🎉 Great Session Complete! 🎉            ║
    ║                                                       ║
    ╚═══════════════════════════════════════════════════════╝
");
        Console.ResetColor();
        
        Console.WriteLine($"    Deactivating SiteBlocker...");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"    ⏱️  You stayed focused for: {FormatDuration(duration)}");
        Console.ResetColor();
        Console.WriteLine();

        // Stop logging server
        StopLoggingServer();

        // Remove entries from /etc/hosts
        var lines = ReadHosts();
        lines = RemoveBlockerEntries(lines);
        WriteHosts(lines);

        // Remove lock file
        if (File.Exists(lockFilePath))
        {
            File.Delete(lockFilePath);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("    ✓ SiteBlocker deactivated!");
        Console.ResetColor();
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n    🌟 Well done! Every focused moment counts. 🌟\n");
        Console.ResetColor();
    }

    public void Status()
    {
        if (IsActive())
        {
            var duration = GetActiveDuration();
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════════╗
    ║                                                       ║
    ║              ✅ SiteBlocker is ACTIVE ✅              ║
    ║                                                       ║
    ╚═══════════════════════════════════════════════════════╝
");
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"    ⏱️  Duration: {FormatDuration(duration)}");
            Console.WriteLine($"    🚫 Blocking: {config.blocklist.Count} domains");
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n    💪 Keep going! You're doing great! 💪\n");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════════╗
    ║                                                       ║
    ║            ⚪ SiteBlocker is INACTIVE ⚪              ║
    ║                                                       ║
    ╚═══════════════════════════════════════════════════════╝
");
            Console.ResetColor();
            
            Console.WriteLine("    Run 'focus' to start a focus session!\n");
        }
    }

    public void RunInteractive()
    {
        if (!IsActive())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n❌ SiteBlocker is not active. Use 'activate' command first.\n");
            Console.ResetColor();
            return;
        }

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n");
            Deactivate();
            Environment.Exit(0);
        };

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("    ✓ SiteBlocker is active. Timer running...");
        Console.ResetColor();
        Console.WriteLine("    Press Ctrl+C to deactivate\n");
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("    📊 Blocked site visits will be logged below:\n");
        Console.ResetColor();

        try
        {
            while (true)
            {
                var duration = GetActiveDuration();
                var totalSeconds = (int)duration.TotalSeconds;
                
                // Create a simple progress indicator
                var progressBar = "";
                var barLength = 20;
                var filled = (totalSeconds % (barLength * 10)) / 10;
                for (int i = 0; i < barLength; i++)
                {
                    if (i < filled)
                        progressBar += "█";
                    else
                        progressBar += "░";
                }
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"\r    ⏱️  Active for: {FormatDuration(duration)}  {progressBar}");
                Console.ResetColor();
                Thread.Sleep(1000);
            }
        }
        catch (Exception)
        {
            Console.WriteLine("\n");
            Deactivate();
        }
    }
}

// Main
var blocker = new SiteBlocker(configFile, lockFile, hostsFile, markerStart, markerEnd, scriptDir);
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
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════════╗
    ║              SiteBlocker - Focus Helper               ║
    ╚═══════════════════════════════════════════════════════╝
");
    Console.ResetColor();
    Console.WriteLine("    Usage: siteblocker <command>\n");
    Console.WriteLine("    Commands:");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("      activate   - Activate the site blocker");
    Console.WriteLine("      deactivate - Deactivate the site blocker");
    Console.WriteLine("      status     - Show current status");
    Console.WriteLine("      run        - Run interactive mode with timer");
    Console.ResetColor();
    Console.WriteLine();
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

