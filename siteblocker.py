#!/usr/bin/env python3
"""
SiteBlocker - A local DNS blocker for focus sessions
Similar to freedom.to, blocks distracting websites via /etc/hosts
"""

import json
import os
import sys
import time
import subprocess
import signal
from pathlib import Path
from datetime import datetime, timedelta
from typing import List, Dict

# Configuration
CONFIG_FILE = Path(__file__).parent / "config.json"
LOCK_FILE = Path.home() / ".siteblocker.lock"
HOSTS_FILE = Path("/etc/hosts")
MARKER_START = "# SiteBlocker START"
MARKER_END = "# SiteBlocker END"


class SiteBlocker:
    def __init__(self):
        self.config = self.load_config()
        self.lock_file = LOCK_FILE

    def load_config(self) -> Dict:
        """Load configuration from JSON file"""
        if not CONFIG_FILE.exists():
            print(f"Error: Config file not found at {CONFIG_FILE}")
            sys.exit(1)
        
        with open(CONFIG_FILE, 'r') as f:
            return json.load(f)

    def is_active(self) -> bool:
        """Check if blocker is currently active"""
        return self.lock_file.exists()

    def get_active_duration(self) -> timedelta:
        """Get how long the blocker has been active"""
        if not self.is_active():
            return timedelta(0)
        
        try:
            with open(self.lock_file, 'r') as f:
                start_time_str = f.read().strip()
                start_time = datetime.fromisoformat(start_time_str)
                return datetime.now() - start_time
        except Exception as e:
            print(f"Error reading lock file: {e}")
            return timedelta(0)

    def format_duration(self, duration: timedelta) -> str:
        """Format duration as human-readable string"""
        total_seconds = int(duration.total_seconds())
        hours, remainder = divmod(total_seconds, 3600)
        minutes, seconds = divmod(remainder, 60)
        
        if hours > 0:
            return f"{hours}h {minutes}m {seconds}s"
        elif minutes > 0:
            return f"{minutes}m {seconds}s"
        else:
            return f"{seconds}s"

    def read_hosts(self) -> List[str]:
        """Read current /etc/hosts file"""
        try:
            with open(HOSTS_FILE, 'r') as f:
                return f.readlines()
        except PermissionError:
            print("Error: Need sudo privileges to modify /etc/hosts")
            sys.exit(1)

    def write_hosts(self, lines: List[str]):
        """Write lines to /etc/hosts file"""
        try:
            with open(HOSTS_FILE, 'w') as f:
                f.writelines(lines)
        except PermissionError:
            print("Error: Need sudo privileges to modify /etc/hosts")
            sys.exit(1)

    def remove_blocker_entries(self, lines: List[str]) -> List[str]:
        """Remove existing SiteBlocker entries from hosts file"""
        result = []
        in_blocker_section = False
        
        for line in lines:
            if MARKER_START in line:
                in_blocker_section = True
                continue
            if MARKER_END in line:
                in_blocker_section = False
                continue
            if not in_blocker_section:
                result.append(line)
        
        return result

    def add_blocker_entries(self, lines: List[str]) -> List[str]:
        """Add SiteBlocker entries to hosts file"""
        # Remove existing entries first
        lines = self.remove_blocker_entries(lines)
        
        # Add new entries
        lines.append(f"\n{MARKER_START}\n")
        redirect_ip = self.config.get("redirect_ip", "127.0.0.1")
        
        for domain in self.config.get("blocklist", []):
            lines.append(f"{redirect_ip} {domain}\n")
        
        lines.append(f"{MARKER_END}\n")
        return lines

    def activate(self):
        """Activate the site blocker"""
        if self.is_active():
            duration = self.get_active_duration()
            print(f"SiteBlocker is already active (running for {self.format_duration(duration)})")
            return
        
        print("Activating SiteBlocker...")
        
        # Create lock file with start time
        with open(self.lock_file, 'w') as f:
            f.write(datetime.now().isoformat())
        
        # Modify /etc/hosts
        lines = self.read_hosts()
        lines = self.add_blocker_entries(lines)
        self.write_hosts(lines)
        
        print("✓ SiteBlocker activated!")
        print(f"Blocking {len(self.config.get('blocklist', []))} domains")
        print("\nPress Ctrl+C to deactivate")

    def deactivate(self):
        """Deactivate the site blocker"""
        if not self.is_active():
            print("SiteBlocker is not active")
            return
        
        duration = self.get_active_duration()
        print(f"Deactivating SiteBlocker (was active for {self.format_duration(duration)})...")
        
        # Remove entries from /etc/hosts
        lines = self.read_hosts()
        lines = self.remove_blocker_entries(lines)
        self.write_hosts(lines)
        
        # Remove lock file
        if self.lock_file.exists():
            self.lock_file.unlink()
        
        print("✓ SiteBlocker deactivated!")

    def status(self):
        """Show current status"""
        if self.is_active():
            duration = self.get_active_duration()
            blocked_count = len(self.config.get('blocklist', []))
            print(f"SiteBlocker is ACTIVE")
            print(f"Duration: {self.format_duration(duration)}")
            print(f"Blocking {blocked_count} domains")
        else:
            print("SiteBlocker is INACTIVE")

    def run_interactive(self):
        """Run in interactive mode with timer display"""
        if not self.is_active():
            print("SiteBlocker is not active. Use 'activate' command first.")
            return
        
        def signal_handler(sig, frame):
            print("\n\nDeactivating...")
            self.deactivate()
            sys.exit(0)
        
        signal.signal(signal.SIGINT, signal_handler)
        signal.signal(signal.SIGTERM, signal_handler)
        
        print("SiteBlocker is active. Timer running...")
        print("Press Ctrl+C to deactivate\n")
        
        try:
            while True:
                duration = self.get_active_duration()
                # Clear line and print duration (works in most terminals)
                print(f"\rActive for: {self.format_duration(duration)}", end="", flush=True)
                time.sleep(1)
        except KeyboardInterrupt:
            signal_handler(None, None)


def main():
    blocker = SiteBlocker()
    
    if len(sys.argv) < 2:
        print("Usage: siteblocker <command>")
        print("Commands:")
        print("  activate   - Activate the site blocker")
        print("  deactivate - Deactivate the site blocker")
        print("  status     - Show current status")
        print("  run        - Run interactive mode with timer")
        sys.exit(1)
    
    command = sys.argv[1].lower()
    
    if command == "activate":
        blocker.activate()
        # After activating, run in interactive mode
        blocker.run_interactive()
    elif command == "deactivate":
        blocker.deactivate()
    elif command == "status":
        blocker.status()
    elif command == "run":
        blocker.run_interactive()
    else:
        print(f"Unknown command: {command}")
        sys.exit(1)


if __name__ == "__main__":
    main()

