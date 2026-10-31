#!/bin/bash
# Loads the USB Wi-Fi driver for whatever stick is attached, from the modules
# WinDrop built for WSL's kernel (WSL's own kernel ships without them).
# Dependencies in the same directory load first; the rest come from modprobe.
set -u
shopt -s nullglob
DIR=${WINDROP_MODULES:-/opt/windrop/modules}

FW=/mnt/wsl/windrop-firmware

# The kernel reads firmware in WSL's root namespace, where this distro's
# /lib/firmware doesn't exist, but /mnt/wsl is shared with it. So copy the
# USB Wi-Fi firmware there (unpacking Ubuntu's .zst files) and point
# firmware_class at it. /mnt/wsl is a tmpfs: redo it on every run.
stage_firmware() {
  local src=/lib/firmware f rel
  mkdir -p "$FW"
  for f in "$src"/mediatek/mt76* "$src"/mediatek/WIFI_* "$src"/mt7601u.bin* "$src"/mt7662*.bin* "$src"/mt7610*.bin* \
           "$src"/rtw88/* "$src"/rtlwifi/* "$src"/ath9k_htc/* "$src"/htc_*.fw* "$src"/rt2870.bin* "$src"/rt3070.bin* \
           "$src"/carl9170-*.fw* "$src"/regulatory.db*; do
    [ -f "$f" ] || continue
    rel=${f#"$src"/}
    mkdir -p "$FW/$(dirname "$rel")"
    case $f in
      *.zst) [ -f "$FW/${rel%.zst}" ] || zstd -qdf "$f" -o "$FW/${rel%.zst}" ;;
      *.xz)  [ -f "$FW/${rel%.xz}" ] || xz -dc "$f" > "$FW/${rel%.xz}" ;;
      *)     cp --update=none "$f" "$FW/$rel" ;;
    esac
  done
  echo -n "$FW" > /sys/module/firmware_class/parameters/path
}

loaded() { grep -q "^${1//-/_} " /proc/modules; }

load() {
  local mod=${1//-/_} file dep
  loaded "$mod" && return 0
  file=$(ls "$DIR/$mod.ko" "$DIR/${mod//_/-}.ko" 2>/dev/null | head -1)
  if [ -z "$file" ]; then
    modprobe "$mod" 2>/dev/null
    return
  fi
  for dep in $(modinfo -F depends "$file" | tr ',' ' '); do
    load "$dep" || return 1
  done
  insmod "$file"
}

stage_firmware

found=0
for alias_file in /sys/bus/usb/devices/*/modalias; do
  modalias=$(cat "$alias_file")
  for ko in "$DIR"/*.ko; do
    while read -r alias; do
      # shellcheck disable=SC2053  # the alias is a glob on purpose
      if [[ $modalias == $alias ]]; then
        mod=$(basename "$ko" .ko)
        echo "$(basename "$(dirname "$alias_file")"): $modalias -> $mod"
        load "$mod" && found=1
      fi
    done < <(modinfo -F alias "$ko" | grep '^usb:')
  done
done
[ "$found" = 1 ] || { echo "no supported USB Wi-Fi adapter found" >&2; exit 1; }
