# SiteBlocker

A local DNS blocker for focus sessions, similar to freedom.to. Blocks distracting websites by redirecting them to localhost via `/etc/hosts` modification.

## Features

- 🚫 Block distracting websites via DNS redirection
- ⏱️ Track active session duration
- 🔧 Configurable blocklist
- 🔒 Lock file mechanism to track active state
- 💻 Simple CLI interface

## Setup

### 1. Install Python 3

Make sure you have Python 3 installed:
```bash
python3 --version
```

### 2. Make the script executable

```bash
chmod +x siteblocker.py
```

### 3. Create an alias

Add this to your `~/.zshrc` (or `~/.bashrc` if using bash):

```bash
alias focus="sudo python3 /Users/pcox/dev/SiteBlocker/siteblocker.py activate"
```

Then reload your shell:
```bash
source ~/.zshrc
```

**Note:** The script requires `sudo` because it modifies `/etc/hosts`.

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
sudo python3 siteblocker.py status
```

### Deactivate manually
```bash
sudo python3 siteblocker.py deactivate
```

## How it works

1. **DNS Blocking**: Modifies `/etc/hosts` to redirect blocked domains to `127.0.0.1`
2. **Lock File**: Creates `~/.siteblocker.lock` to track active state and start time
3. **Timer**: Calculates duration from lock file timestamp

## Security Note

This script requires `sudo` privileges to modify `/etc/hosts`. The script only modifies the section between `# SiteBlocker START` and `# SiteBlocker END` markers, so it's safe to use alongside other hosts file entries.

## Troubleshooting

- **Permission denied**: Make sure you're using `sudo` when running the script
- **Sites still accessible**: Try flushing your DNS cache:
  ```bash
  sudo dscacheutil -flushcache; sudo killall -HUP mDNSResponder
  ```
- **Lock file stuck**: If the script crashes, you may need to manually remove `~/.siteblocker.lock` and clean up `/etc/hosts`

