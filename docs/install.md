# Installing, upgrading and removing RecipeWarden

One command installs or upgrades RecipeWarden on Debian 12/13 or Ubuntu 22.04, 24.04 or 26.04 LTS (x64). It uses only bash,
curl, tar and sha256sum (ADR 0005, ADR 0007). Run it as any user with sudo.

## Install or upgrade

**Production (newest release):**
```bash
curl -fsSL https://github.com/kretherford0983/recipewarden/releases/latest/download/install.sh | sudo bash
```

**A specific build**, e.g. a development build on the test host (each release's notes show this with the tag filled in):
```bash
curl -fsSL https://github.com/kretherford0983/recipewarden/releases/download/v0.1.0-dev.3/install.sh | sudo bash -s -- --version v0.1.0-dev.3
```

Without `--version`, the newest release is installed; while there is no release yet, the newest development
build is used (with a warning). `--channel release` or `--channel dev` picks explicitly.

The installer waits 5 seconds (Ctrl+C to cancel; `--yes` skips the wait), then:
1. checks root, the OS, systemd and (new installs) that the port is free;
2. downloads the package and `SHA256SUMS.txt` from the release and refuses it unless the checksum matches;
3. installs the ICU library if missing, creates the `recipewarden` service user and folders;
4. unpacks the release to `/opt/recipewarden/releases/<version>-<time>/` and points `current` at it;
5. on an **upgrade**: stops the service and copies the database to `/var/lib/recipewarden/backups/pre-upgrade-<time>/`;
6. writes the settings files (new installs only), the systemd unit and the backup-folder drop-in;
7. starts the service and waits for `/health`. **If an upgrade does not become healthy, the previous release is
   put back and started automatically**;
8. new installs: asks for the first Admin email and password (Enter skips; see *Admin account* below);
9. keeps the three newest releases and prints the URL and a Cloudflare Tunnel ingress example.

Prefer to read the script first:
```bash
curl -fsSLO https://github.com/kretherford0983/recipewarden/releases/latest/download/install.sh
less install.sh
sudo bash install.sh
```

## Where cloudflared runs

| cloudflared | First-install options | Ingress `service:` |
|---|---|---|
| on this host | none (defaults: `--bind 127.0.0.1 --port 5080`) | `http://localhost:5080` |
| in its own container/VM (e.g. a Proxmox LXC) | `--bind <this host's IP> --trusted-proxy <cloudflared's IP>` | `http://<this host's IP>:5080` |

Example for a test LXC at 192.168.1.15 with cloudflared at 192.168.1.20, also allowing your LAN while testing:
```bash
curl -fsSL https://github.com/kretherford0983/recipewarden/releases/download/v0.1.0-dev.3/install.sh \
  | sudo bash -s -- --version v0.1.0-dev.3 --bind 192.168.1.15 --trusted-proxy 192.168.1.20 --allowed-ips 192.168.1.0/24
```
The app refuses connections from any address that is not the tunnel host, loopback, the host itself or listed in
`--allowed-ips` (SEC-60). Sign-in needs HTTPS (secure cookies), so sign in through the tunnel; plain
`http://<LAN IP>:5080` shows pages but cannot sign you in.

## Settings files

| File | Owner | What |
|---|---|---|
| `/etc/recipewarden/appsettings.Production.json` | you | data folder, `Backup:AllowedFolders`, later settings. Written once, never overwritten by upgrades |
| `/etc/recipewarden/network.json` | installer | listening address and port, trusted proxies, allowed IPs |

Change either file, then restart:
```bash
sudo nano /etc/recipewarden/network.json
sudo systemctl restart recipewarden
```
After changing `Backup:AllowedFolders`, run `sudo recipewarden apply-config` instead (it opens the folders in the
service sandbox and restarts). To change network settings without editing JSON:
```bash
curl -fsSL https://github.com/kretherford0983/recipewarden/releases/latest/download/install.sh \
  | sudo bash -s -- --reconfigure --port 5090        # or --bind / --trusted-proxy / --allowed-ips
```

## Admin account
The installer asks for the first Admin's email and password on a new install. Unattended:
`--admin-email you@example.com --admin-password-file /root/pw.txt` (delete the file afterwards). Or later:
```bash
sudo recipewarden admin create --email you@example.com
```

## Everyday commands
```bash
sudo recipewarden health                 # database, journal mode, data folder
systemctl status recipewarden            # service state
journalctl -u recipewarden -f            # logs (also /var/log/recipewarden/)
curl http://127.0.0.1:5080/health        # liveness: "ok"
```

## Rolling back by hand
```bash
ls -1t /opt/recipewarden/releases/                                  # newest first
sudo ln -sfn /opt/recipewarden/releases/<previous> /opt/recipewarden/current
sudo systemctl restart recipewarden
```
The database moves forward with each release; to go back across a database change, restore the matching
`pre-upgrade-*` copy in `/var/lib/recipewarden/backups/` with the service stopped (full backup and restore arrive in M7).

## Uninstall
```bash
curl -fsSL https://github.com/kretherford0983/recipewarden/releases/latest/download/install.sh | sudo bash -s -- --uninstall
```
Keeps the data, settings, logs and service user. Add `--purge` to remove them as well.

## All options
Run `sudo bash install.sh --help`. Not available yet: `--restore` (M7), `--framework-dependent`, and managing the
host firewall (for a LAN bind, also limit the port to the tunnel host with ufw/nftables yourself).
