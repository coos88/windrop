#!/bin/bash
# One-time WSL setup for WinDrop. Run as root:
#
#   wsl -d ubuntu -u root -- bash setup.sh <user> <owly-src> <modules-dir>
#
#   <user>         the WSL user WinDrop runs owly as
#   <owly-src>     an omdrop-owl checkout with owly/owlyd built
#                  (cargo build --release --locked -p owlyd -p owly-cli)
#   <modules-dir>  USB Wi-Fi .ko files built for this kernel
set -euo pipefail
USER_NAME=${1:?user}
OWLY_SRC=${2:?owly source dir}
MODULES=${3:?modules dir}
HERE=$(cd "$(dirname "$0")" && pwd)

echo "== packages"
export DEBIAN_FRONTEND=noninteractive
apt-get install -y -qq python3-dbus python3-gi polkitd hostapd iw wireless-regdb kmod zstd xz-utils linux-firmware >/dev/null
systemctl disable --now hostapd 2>/dev/null || true

echo "== owly"
[ -x "$OWLY_SRC/target/release/owly" ] || { echo "owly is not built in $OWLY_SRC" >&2; exit 1; }
(cd "$OWLY_SRC" && packaging/install.sh owly)
systemctl daemon-reload
mkdir -p /etc/owly
[ -f /etc/owly/radio.toml ] || cat > /etc/owly/radio.toml <<'EOF'
# WinDrop: the USB stick is the only Wi-Fi card WSL sees, so owly takes the
# first station. Exclusive mode needs the regulatory domain.
country = "NL"
EOF

echo "== modules"
install -d /opt/windrop/modules
install -m 644 "$MODULES"/*.ko /opt/windrop/modules/
install -m 755 "$HERE/load-modules.sh" /usr/local/sbin/windrop-load-modules

echo "== bluez shim"
install -m 755 "$HERE/bluez-shim.py" /opt/windrop/bluez-shim.py
install -m 644 "$HERE/windrop-bluez-shim.conf" /etc/dbus-1/system.d/windrop-bluez-shim.conf

echo "== polkit"
getent group windrop >/dev/null || groupadd --system windrop
usermod -aG windrop "$USER_NAME"
install -m 644 "$HERE/50-windrop-owly.rules" /etc/polkit-1/rules.d/50-windrop-owly.rules

systemctl reload dbus || true
systemctl restart polkit || true
echo "== done"
