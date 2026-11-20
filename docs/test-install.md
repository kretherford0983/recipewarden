# Installing a build on a test host (until the M7 installer)

`install.sh` only runs preflight checks until milestone M7. Until then, install a build by hand on a Debian 12/13
or Ubuntu 22.04/24.04 x64 host (a Proxmox LXC is fine). Everything below runs as root.

## 1. Download a build
From your Mac or the host (needs the GitHub CLI signed in, because the repository is private):
```bash
gh release list -R kretherford0983/recipewarden            # development builds are v<version>-dev.<n>
gh release download v0.1.0-dev.12 -R kretherford0983/recipewarden
sha256sum -c --ignore-missing SHA256SUMS.txt
```
Or download `recipewarden-<version>-linux-x64.tar.gz` and `SHA256SUMS.txt` from the release page and copy them over.

## 2. First install
```bash
V=0.1.0-dev.12
apt-get install -y libicu-dev ca-certificates curl
useradd --system --home-dir /var/lib/recipewarden --shell /usr/sbin/nologin recipewarden
install -d -m 0750 -o recipewarden -g recipewarden /var/lib/recipewarden /var/lib/recipewarden/backups
install -d -m 0750 -o recipewarden -g recipewarden /var/log/recipewarden
install -d -m 0750 -o root -g recipewarden /etc/recipewarden
install -d /opt/recipewarden
tar -xzf recipewarden-$V-linux-x64.tar.gz -C /opt/recipewarden
ln -sfn /opt/recipewarden/recipewarden-$V /opt/recipewarden/current
ln -sfn /opt/recipewarden/current/cli/recipewarden /usr/local/bin/recipewarden

cat > /etc/recipewarden/appsettings.Production.json <<'JSON'
{
  "Data": { "Directory": "/var/lib/recipewarden" },
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://192.168.1.15:5080" } } },
  "Network": { "TrustedProxies": [ "192.168.1.20" ], "AllowedSourceIps": [ "192.168.1.0/24" ] },
  "Backup": { "AllowedFolders": [ "/var/lib/recipewarden/backups" ] }
}
JSON
chmod 0640 /etc/recipewarden/appsettings.Production.json
chown root:recipewarden /etc/recipewarden/appsettings.Production.json

cp /opt/recipewarden/current/recipewarden.service /etc/systemd/system/
sudo -u recipewarden RECIPEWARDEN_CONFIG=/etc/recipewarden/appsettings.Production.json recipewarden admin create --email you@example.com
recipewarden apply-config --no-restart
systemctl daemon-reload && systemctl enable --now recipewarden
curl -s http://192.168.1.15:5080/health    # ok
```
Replace `192.168.1.15` with the test host's IP and `192.168.1.20` with the cloudflared container's IP. Adding your
LAN range to `AllowedSourceIps` lets you open the site directly while testing; remove it for production. For
loopback-only (cloudflared on the same host) use `http://127.0.0.1:5080` and `"TrustedProxies": [ "127.0.0.1" ]`.

The auth cookie is `Secure`, so sign-in works through the tunnel (HTTPS) but not over plain `http://` to the LAN IP.

## 3. Upgrade to a newer build
```bash
V=0.1.0-dev.13
tar -xzf recipewarden-$V-linux-x64.tar.gz -C /opt/recipewarden
ln -sfn /opt/recipewarden/recipewarden-$V /opt/recipewarden/current
cp /opt/recipewarden/current/recipewarden.service /etc/systemd/system/ && systemctl daemon-reload
systemctl restart recipewarden && recipewarden health
```
Roll back by pointing `current` at the previous folder and restarting. The database migrates forward on start, so
rolling back across a migration needs a backup (backups arrive in M7; until then copy `/var/lib/recipewarden/db`
with the service stopped).
