# SiteBlocker

A local DNS blocker for focus sessions, similar to freedom.to. Blocks distracting websites by redirecting them to localhost via `/etc/hosts` modification.

## Features

- 🚫 Block distracting websites via DNS redirection
- 🎨 Beautiful HTML page shown when accessing blocked sites (with Tailwind CSS)
- ⏱️ Track active session duration
- 🔧 Configurable blocklist
- 🔒 Lock file mechanism to track active state
- 💻 Simple CLI interface
- 🌐 Built-in HTTP server to serve focus page

## Setup

### 1. Install .NET SDK

Make sure you have .NET SDK installed:
```bash
dotnet --version
```

### 2. Install dotnet-script

```bash
dotnet tool install -g dotnet-script
```

Add dotnet tools to your PATH (add to `~/.zprofile`):
```bash
export PATH="$PATH:/Users/pcox/.dotnet/tools"
```

### 3. Make the script executable

```bash
chmod +x siteblocker.csx
```

### 4. Set up the focus command

**Option A: Run the installer (Recommended)**

```bash
cd /Users/pcox/dev/SiteBlocker
./install.sh
source ~/.zshrc  # or ~/.bashrc
```

This will automatically add a `focus` alias to your shell config.

**Option B: Manual setup**

Add this to your `~/.zshrc` (or `~/.bashrc` if using bash):

```bash
alias focus="/Users/pcox/dev/SiteBlocker/focus.sh activate"
```

Then reload your shell:
```bash
source ~/.zshrc
```

**Option C: Use the wrapper script directly**

```bash
/Users/pcox/dev/SiteBlocker/focus.sh activate
```

**Note:** The script requires `sudo` because it modifies `/etc/hosts`. The wrapper script handles this automatically.

### 4. Configure your blocklist

Edit `config.json` to add or remove domains:

```json
{
  "blocklist": [
    "bbc.co.uk",
    "www.bbc.co.uk",
    "reddit.com",
    "www.reddit.com"
  ],
  "redirect_ip": "127.0.0.1"
}
```

## Usage

### Activate (with timer)
```bash
focus
```

This will:
- Activate the blocker
- Show a live timer of how long it's been active
- Run until you press Ctrl+C
- Automatically deactivate when you quit

### Check status
```bash
focus.sh status
```
or
```bash
/Users/pcox/dev/SiteBlocker/focus.sh status
```

### Deactivate manually
```bash
focus.sh deactivate
```
or
```bash
/Users/pcox/dev/SiteBlocker/focus.sh deactivate
```

## How it works

1. **DNS Blocking**: Modifies `/etc/hosts` to redirect blocked domains to `127.0.0.1`
2. **ASP.NET Core Server**: Starts a local web server on port 4000 (configurable) to serve a beautiful "Focus Mode" page
3. **Lock File**: Creates `~/.siteblocker.lock` to track active state and start time
4. **Timer**: Calculates duration from lock file timestamp

When you try to access a blocked site, instead of seeing an error, you'll see a beautiful HTML page encouraging you to stay focused!

**Note**: Modern browsers default to HTTPS. Since the server runs on HTTP port 4000, you may need to manually use `http://` for blocked sites, or the browser may show a connection error. The server is running and accessible at `http://127.0.0.1:4000`.

## Security Note

This script requires `sudo` privileges to modify `/etc/hosts`. The script only modifies the section between `# SiteBlocker START` and `# SiteBlocker END` markers, so it's safe to use alongside other hosts file entries.

## Troubleshooting

- **Permission denied**: Make sure you're using `sudo` when running the script
- **Sites still accessible**: Try flushing your DNS cache:
  ```bash
  sudo dscacheutil -flushcache; sudo killall -HUP mDNSResponder
  ```
- **Lock file stuck**: If the script crashes, you may need to manually remove `~/.siteblocker.lock` and clean up `/etc/hosts`

