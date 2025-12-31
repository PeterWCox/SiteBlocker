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
2. **Connection Failure**: Browser tries to connect to `127.0.0.1` but there's no server running
3. **Blocked**: Browser shows connection error, effectively blocking the site

### How It Works

When SiteBlocker is active:

- Blocked domains resolve to `127.0.0.1` (localhost)
- Browsers try to connect to `127.0.0.1:443` (HTTPS) or `127.0.0.1:80` (HTTP)
- Since no server is running on those ports, the connection fails
- Browser displays "ERR_CONNECTION_REFUSED" or similar error
- Site is effectively blocked

## HTTPS vs HTTP

### How Blocking Works

Modern browsers **default to HTTPS** (port 443) for many sites:

```
bbc.co.uk → Browser tries https://bbc.co.uk (port 443)
           → Resolves to 127.0.0.1:443
           → Connection fails (no server)
           → Shows "ERR_CONNECTION_REFUSED" ✅
```

For HTTP (port 80):

```
bbc.co.uk → Browser tries http://bbc.co.uk (port 80)
           → Resolves to 127.0.0.1:80
           → Connection fails (no server)
           → Shows "ERR_CONNECTION_REFUSED" ✅
```

**Note**: Both HTTP and HTTPS connections will fail, effectively blocking the site. The browser will show a connection error instead of loading the distracting content.

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

### Test DNS Resolution

```bash
# Check if a domain resolves to localhost (blocked)
nslookup bbc.co.uk

# Or use dig
dig bbc.co.uk

# Should show 127.0.0.1 when SiteBlocker is active
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

### "Permission denied" when modifying /etc/hosts
**Problem**: Modifying `/etc/hosts` requires root/sudo privileges.

**Solution**: Run SiteBlocker with `sudo`:
```bash
sudo dotnet script siteblocker.csx activate
```

### Port not accessible
**Note**: SiteBlocker doesn't run a server, so port accessibility isn't a concern. The blocking works entirely through `/etc/hosts` DNS redirection.

## SiteBlocker Configuration

In `config.json`:

```json
{
  "redirect_ip": "127.0.0.1",
  "server_port": 80
}
```

- **redirect_ip**: The IP address blocked domains resolve to (default: `127.0.0.1`)
- **server_port**: Legacy setting (no longer used - SiteBlocker doesn't run a server)

## Summary

1. **Ports** identify services on a computer (like apartment numbers)
2. **Port 80** is the default HTTP port (browsers use it automatically)
3. **Port 443** is the default HTTPS port (encrypted)
4. **Ports < 1024** require root/sudo privileges
5. SiteBlocker blocks sites by redirecting DNS to `127.0.0.1` via `/etc/hosts`
6. When browsers try to connect, they get connection errors, effectively blocking the sites

