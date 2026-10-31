#!/bin/bash
# Loads the USB Wi-Fi driver for whatever stick is attached, from the modules
# WinDrop built for WSL's kernel (WSL's own kernel ships without them).
# Dependencies in the same directory load first; the rest come from modprobe.
set -u
DIR=${WINDROP_MODULES:-/opt/windrop/modules}

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
[ "$found" = 1 ] || { echo "geen ondersteunde USB-wifistick gevonden" >&2; exit 1; }
