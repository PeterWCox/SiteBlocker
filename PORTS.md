# How Network Ports Work

## What is a Port?

A **port** is a number (0-65535) that identifies a specific process or service on a computer. Think of it like an apartment number in a building:

- The **IP address** (like `127.0.0.1` or `192.168.1.1`) is the building address
- The **port** (like `80` or `4000`) is the apartment number

When you connect to a server, you need both:
```
IP Address : Port
127.0.0.1  : 80
```

## Port Ranges

### Well-Known Ports (0-1023)
These are reserved for common services and require administrator/root privileges to use:

- **Port 80**: HTTP (web traffic, unencrypted)
- **Port 443**: HTTPS (web traffic, encrypted)
- **Port 22**: SSH (secure shell)
- **Port 25**: SMTP (email)
- **Port 53**: DNS (domain name resolution)

### Registered Ports (1024-49151)
Used by applications and services, but can be used by regular users:

- **Port 3000**: Often used by Node.js development servers
- **Port 4000**: Often used by development servers
- **Port 8080**: Alternative HTTP port
- **Port 5432**: PostgreSQL database

### Dynamic/Private Ports (49152-65535)
Used for temporary connections and client-side connections.

## How Ports Work in SiteBlocker

### The Problem

When you visit a website like `bbc.co.uk`:

1. Your browser tries to connect to `bbc.co.uk`
2. DNS lookup resolves it to an IP address (e.g., `151.101.0.81`)
3. Your browser connects to that IP on **port 443** (HTTPS) or **port 80** (HTTP)
4. The website server responds

### The Solution

SiteBlocker intercepts this process:

1. **DNS Blocking**: `/etc/hosts` redirects `bbc.co.uk` → `127.0.0.1`
2. **Local Server**: Our ASP.NET Core server runs on `127.0.0.1:80`
3. **Automatic Redirect**: Browser connects to `127.0.0.1:80` and gets the focus page

### Why Port 80?

We use **port 80** because:

- It's the **default HTTP port**
- Browsers automatically use port 80 for HTTP connections
- No need to specify `:80` in the URL
- Works automatically when `/etc/hosts` redirects to `127.0.0.1`

### Port 80 vs Port 4000

| Port | Requires Sudo | Default Behavior | Use Case |
|------|---------------|------------------|----------|
| **80** | ✅ Yes | Browser automatically uses it | Production, automatic redirect |
| **4000** | ❌ No | Must specify `:4000` in URL | Development, testing |

### Example URLs

```bash
# Port 80 (default HTTP) - works automatically
http://bbc.co.uk          → Connects to 127.0.0.1:80
http://127.0.0.1          → Connects to 127.0.0.1:80

# Port 4000 - must specify
http://127.0.0.1:4000     → Connects to 127.0.0.1:4000
```

## HTTPS vs HTTP

### The HTTPS Problem

Modern browsers **default to HTTPS** (port 443) for many sites:

```
bbc.co.uk → Browser tries https://bbc.co.uk (port 443)
           → Connection fails (no HTTPS server)
           → Shows "ERR_CONNECTION_REFUSED"
```

### The HTTP Solution

For HTTP (port 80):

```
bbc.co.uk → Browser tries http://bbc.co.uk (port 80)
           → Connects to 127.0.0.1:80
           → Gets focus page ✅
```

**Note**: Some browsers may try HTTPS first, which will fail. HTTP requests will work and show the focus page.

## Checking Ports

### See What's Using a Port

**macOS/Linux:**
```bash
# Check if port 80 is in use
lsof -i :80

# Check if port 4000 is in use
lsof -i :4000

# See all listening ports
netstat -an | grep LISTEN
```

**Windows:**
```bash
netstat -ano | findstr :80
```

### Test a Port

```bash
# Test if port 80 is accessible
curl http://127.0.0.1:80

# Test if port 4000 is accessible
curl http://127.0.0.1:4000
```

## Common Port Issues

### "Address already in use"
**Problem**: Another program is using the port.

**Solution**:
```bash
# Find what's using port 80
sudo lsof -i :80

# Kill the process (replace PID with actual process ID)
sudo kill -9 <PID>
```

### "Permission denied" on port 80
**Problem**: Ports below 1024 require root/sudo privileges.

**Solution**: Run with `sudo`:
```bash
sudo dotnet run --project SiteBlockerServer.csproj
```

### Port not accessible
**Problem**: Firewall blocking the port.

**Solution**: Check firewall settings or use a different port (like 4000).

## SiteBlocker Configuration

In `config.json`:

```json
{
  "server_port": 80
}
```

- **Port 80**: Requires sudo, works automatically with `/etc/hosts` redirects
- **Port 4000**: No sudo needed, but must access via `http://127.0.0.1:4000`

## Summary

1. **Ports** identify services on a computer (like apartment numbers)
2. **Port 80** is the default HTTP port (browsers use it automatically)
3. **Port 443** is the default HTTPS port (encrypted)
4. **Ports < 1024** require root/sudo privileges
5. SiteBlocker uses **port 80** so blocked sites automatically redirect to the focus page

