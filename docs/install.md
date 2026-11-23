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
If no Admin was created, the installer prints a one-time **setup token** and the address of `/setup`, where you
either create the first Admin or restore a backup (SEC-15). The page disappears for good once either is done.
A new token: `sudo recipewarden setup-token`.

## Everyday commands
```bash
sudo recipewarden health                 # database, disks, backups, capacity (exit 2 when Critical)
sudo recipewarden backup                 # a backup now, into /var/lib/recipewarden/backups
systemctl status recipewarden            # service state
journalctl -u recipewarden -f            # logs (also /var/log/recipewarden/)
curl http://127.0.0.1:5080/health        # liveness: "ok"
curl http://127.0.0.1:5080/health/ready  # readiness for Uptime Kuma: 200 (ok/warning) or 503 (critical)
```

## Backups
In the app, **Admin → Backups**: back up now, create the encryption key (downloads the **recovery kit** once — keep
it and its passphrase away from the server), add schedules with retention, and restore. Until the key exists,
backups stay in the built-in folder on this server; with it, every backup is encrypted and can go to other folders.

### Backup folders (e.g. a NAS share)
The app only writes to folders you list, and never mounts anything itself (section 9.6.1):
1. `sudo mkdir -p /mnt/nas/recipewarden`
2. Mount the share owned by the service account, e.g. in `/etc/fstab`:
   `//nas/backups/recipewarden /mnt/nas/recipewarden cifs credentials=/etc/recipewarden-nas.cred,uid=recipewarden,gid=recipewarden,file_mode=0600,dir_mode=0700,_netdev,nofail,x-systemd.automount 0 0`
   (credentials file 0600 root). For NFS, make the export writable by the service UID.
3. The marker: `sudo -u recipewarden touch /mnt/nas/recipewarden/.recipewarden-backup-target`
4. Add the folder to `Backup:AllowedFolders` in `/etc/recipewarden/appsettings.Production.json`.
5. `sudo recipewarden apply-config` (opens the folder in the service sandbox, restarts, checks health).
6. In the app: add a schedule to that folder, press **Test folder**, save, and **Run now** once.

If Test says "Read-only file system" after mounting the share, restart the service.

## Restore
- **In the app**: Backups → an archive's **Restore…** (or upload one up to 100 MB) → check → type `RESTORE`.
  A pre-restore backup is made first; the site shows a maintenance page while it runs.
- **From the shell** (works when the web app is down):
  `sudo recipewarden restore /path/backup-….tar.gz.rwenc --kit /path/recipe-recovery-kit.txt`
  — checks the archive, asks for `RESTORE`, stops the service, makes a pre-restore backup, restores, migrates and
  starts the service. An archive from a newer version is refused before anything changes.

## Moving to a new host
1. On the old host: Admin → Backups → **Back up now**, then download the archive (or copy it from the backup folder).
2. Copy it and your recovery kit to the new host.
3. On the new host:
   ```bash
   curl -fsSLO https://github.com/kretherford0983/recipewarden/releases/latest/download/install.sh
   sudo bash install.sh --restore backup-….tar.gz.rwenc --restore-kit recovery-kit.txt \
     --bind 192.168.1.30 --port 5080 --trusted-proxy 192.168.1.20
   ```
   The archive is checked first; recipes, images, accounts and kitchen come from it, the network settings from this
   host. No Admin is created and no setup token is issued (the accounts are in the backup).
4. Point the tunnel's ingress `service` at the new address; check the site; stop the old host.

## Rolling back by hand
```bash
ls -1t /opt/recipewarden/releases/                                  # newest first
sudo ln -sfn /opt/recipewarden/releases/<previous> /opt/recipewarden/current
sudo systemctl restart recipewarden
```
The database moves forward with each release; to go back across a database change, restore the matching
`pre-upgrade-*` copy in `/var/lib/recipewarden/backups/` with the service stopped, or restore the full pre-upgrade
backup the installer made (`backup-*.tar.gz`, listed on Admin → Backups) with `sudo recipewarden restore`.

## Uninstall
```bash
curl -fsSL https://github.com/kretherford0983/recipewarden/releases/latest/download/install.sh | sudo bash -s -- --uninstall
```
Keeps the data, settings, logs and service user. Add `--purge` to remove them as well.

## All options
Run `sudo bash install.sh --help`. Not available yet: `--framework-dependent`.

For a LAN bind the installer also limits the port to the tunnel host (and `--allowed-ips`) in the host firewall:
with ufw when it is active, otherwise with an nftables table that a service drop-in re-applies at every start.
`--no-firewall` skips this; the app refuses other addresses either way (SEC-60).
