#!/bin/bash
# Builds what WinDrop needs in WSL, as the normal user:
#   - USB Wi-Fi drivers for WSL's own kernel (it ships without them)
#   - owly/owlyd from omdrop-owl
# Output: $OUT/modules/*.ko and $OUT/omdrop-owl/target/release/{owly,owlyd}
# Root packages it needs: build-essential flex bison libssl-dev libelf-dev bc
# dwarves cpio git pkg-config curl clang (setup installs them).
set -euo pipefail
OUT=${WINDROP_BUILD:-$HOME/windrop-build}
OWLY_TAG=${OWLY_TAG:-v0.4.2}
KVER=$(uname -r | sed 's/-microsoft-standard-WSL2.*//')
mkdir -p "$OUT" && cd "$OUT"

echo "== rust"
if [ ! -x "$HOME/.cargo/bin/cargo" ]; then
  curl -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal >/dev/null
fi
. "$HOME/.cargo/env"

echo "== owly $OWLY_TAG"
[ -d omdrop-owl ] || git clone -q --depth 1 --branch "$OWLY_TAG" https://github.com/t4t5/omdrop-owl.git
(cd omdrop-owl && cargo build --release --locked -p owlyd -p owly-cli)

echo "== kernel modules for $KVER"
[ -d WSL2-Linux-Kernel ] || git clone -q --depth 1 --branch "linux-msft-wsl-$KVER" https://github.com/microsoft/WSL2-Linux-Kernel.git
cd WSL2-Linux-Kernel
zcat /proc/config.gz > .config
for o in WLAN_VENDOR_MEDIATEK WLAN_VENDOR_REALTEK WLAN_VENDOR_ATH WLAN_VENDOR_RALINK RTL_CARDS \
         RT2800USB_RT33XX RT2800USB_RT35XX RT2800USB_RT3573 RT2800USB_RT53XX RT2800USB_RT55XX; do
  ./scripts/config -k --enable $o
done
for o in MT76x2U MT76x0U MT7601U MT7921U MT7663U RTL8XXXU RTW88 RTW88_8821CU RTW88_8822BU RTW88_8822CU RTW88_8723DU \
         ATH9K_HTC CARL9170 RT2X00 RT2800USB TEST_FIRMWARE; do
  ./scripts/config -k --module $o
done
make olddefconfig >/dev/null
# A full build: modules need vmlinux's Module.symvers to match the running kernel.
# LOCALVERSION= (set, empty) keeps setlocalversion from appending "+", which
# would make the vermagic differ from the running kernel's.
make -j"$(nproc)" LOCALVERSION= >/dev/null
make -j"$(nproc)" LOCALVERSION= modules >/dev/null
rm -rf "$OUT/modules" && mkdir -p "$OUT/modules"
find drivers/net/wireless net/wireless net/mac80211 -name '*.ko' -exec cp {} "$OUT/modules/" \;
cp lib/test_firmware.ko "$OUT/modules/" 2>/dev/null || true
echo "vermagic: $(modinfo -F vermagic "$OUT/modules/mt76x2u.ko")"
echo "== done: $OUT"
