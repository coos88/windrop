# WinDrop

Real **AirDrop on Windows**. Your iPhone, iPad or Mac sees your PC in the normal AirDrop share sheet, and you can send files back from File Explorer. Nothing gets installed on the Apple devices.

WinDrop is a Windows front end for [omdrop-owl / owly](https://github.com/t4t5/omdrop-owl), the open-source AirDrop implementation that brings AirDrop to Linux (Omarchy among others).

> **Status: experimental.** The Windows side, the BLE bridge and the WSL setup are built and tested. A full transfer with an iPhone hasn't been tested yet, because that needs the USB Wi-Fi adapter described below.

## Why a USB Wi-Fi adapter?

AirDrop runs over Apple's own Wi-Fi protocol (AWDL). owly reimplements it, which means driving the Wi-Fi card at a low level: monitor mode plus injecting raw frames. Windows drivers don't allow that; `netsh wlan show wirelesscapabilities` reports `Network monitor mode: Not supported`. So WinDrop works like this:

```
iPhone ──AWDL (Wi-Fi)──► USB Wi-Fi adapter ──usbipd──► WSL 2: owly (AirDrop)
   ▲                                                       │ files → Downloads
   └───── BLE "wake up" ◄── Windows Bluetooth ◄────────────┘ (BlueZ bridge)
```

- The **USB adapter** is handed to WSL with [usbipd-win](https://github.com/dorssel/usbipd-win), where owly drives it with the Linux driver. Your built-in Wi-Fi stays your internet connection.
- For **sending**, owly broadcasts a Bluetooth LE advert that tells Apple devices to switch AirDrop on. WSL has no Bluetooth, so a small BlueZ stand-in (`wsl/bluez-shim.py`) catches that advert, and WinDrop broadcasts it with Windows' own Bluetooth adapter.

### Which adapter?

You need an adapter with a chipset that [omdrop-owl's hardware table](https://github.com/t4t5/omdrop-owl#hardware) lists as working (✅). Recommended: **MediaTek MT7612U** (`0e8d:7612`), for example the Alfa AWUS036ACM, ALLNET ALL-WA1200AC or PIX-LINK LV-UAC04. When it arrives, check that `usbipd list` shows `0e8d:7612`.

Avoid the COMFAST CF-WU782AC (it first shows up as a USB drive) and adapters with an RTL8812AU or RTL8821AU chipset.

## Usage

- **iPhone/Mac → PC**: Share → AirDrop → your PC. WinDrop asks you to Accept or Decline, and the file lands in your Downloads folder.
- **PC → iPhone/Mac**: right-click a file → *Show more options* → **Share via AirDrop**. Or click the WinDrop tray icon and pick files.
- On the Apple device, set AirDrop to **"Everyone for 10 Minutes"**. "Contacts Only" works between Apple devices only.

## Installation

Requirements:
- Windows 11
- WSL 2 with Ubuntu
- [usbipd-win](https://github.com/dorssel/usbipd-win) (`winget install dorssel.usbipd-win`)
- a supported USB Wi-Fi adapter

```powershell
dotnet build -c Release
src\WinDrop\bin\Release\net10.0-windows10.0.22621.0\WinDrop.exe setup
```

`setup` does four things:
1. It installs build packages in WSL.
2. It builds owly and the USB Wi-Fi drivers for the WSL kernel (`wsl/build.sh`, 10–20 minutes). WSL's own kernel ships without Wi-Fi drivers, so WinDrop builds only the modules and leaves the kernel itself alone.
3. It installs owly, the BlueZ bridge and a polkit rule (`wsl/setup.sh`).
4. It shares the adapter with WSL. Windows asks for permission once.

After setup, WinDrop lives in the system tray. Its menu has these options:
- Visible to AirDrop
- Share via AirDrop in Explorer
- Start with Windows

Where things go:
- Received files: `%USERPROFILE%\Downloads`
- Settings: `%APPDATA%\WinDrop\settings.json` (`Distro`, `DisplayName`, `ReceiveDirectory`)
- Log: `%LOCALAPPDATA%\WinDrop\windrop.log`

## Project layout

| Path | Contents |
|---|---|
| `src/WinDrop/AirDrop` | Drives owly (radio, receive, browse, send) and parses its output |
| `src/WinDrop/Ble` | Broadcasts the AirDrop BLE advert through Windows |
| `src/WinDrop/Wsl` | WSL processes and usbipd |
| `src/WinDrop/UI` | Tray icon, macOS-style request and share windows, setup window |
| `wsl/` | Build and setup scripts, module loader, BlueZ bridge |
| `tests/` | Unit tests (owly output, paths, usbipd, wording) |

## Known limitations and open questions

- **Firmware in WSL**: drivers load their firmware through the kernel. Whether it finds the firmware in Ubuntu's `/lib/firmware` still has to be tested with a real adapter.
- **Speed**: owly is slower than real AirDrop. Photos are fine; large videos take a while.
- **Explorer menu**: the entry sits in the classic context menu ("Show more options"). Getting into the new Windows 11 menu requires a packaged app.
- AirDrop is reverse-engineered and may break with iOS or macOS updates.
- The app's UI is currently in Dutch.

## License

MIT. owly/omdrop-owl is also MIT-licensed.
