# WinDrop

Echte **AirDrop op Windows**. Je iPhone, iPad of Mac ziet je pc gewoon in het AirDrop-deelmenu, en vanuit de Verkenner stuur je bestanden terug. Op de Apple-apparaten installeer je niets.

WinDrop is de Windows-schil rond [omdrop-owl / owly](https://github.com/t4t5/omdrop-owl), de open-source AirDrop-implementatie waarmee AirDrop op Linux (onder andere Omarchy) werkt.

> **Status: experimenteel.** De Windows-kant, de BLE-brug en de WSL-installatie zijn gebouwd en getest. Een volledige overdracht met een iPhone is nog niet getest, want daarvoor is de USB-wifistick nodig (zie hieronder).

## Waarom een USB-wifistick?

AirDrop gebruikt Apple's eigen wifiprotocol (AWDL). owly doet dat na, maar moet de wifikaart daarvoor op laag niveau aansturen (monitor mode en het zelf versturen van frames). Windows-drivers laten dat niet toe; `netsh wlan show wirelesscapabilities` meldt dan `Network monitor mode: Not supported`. Daarom gaat het zo:

```
iPhone ──AWDL (wifi)──► USB-wifistick ──usbipd──► WSL 2: owly (AirDrop)
   ▲                                                   │ files → Downloads
   └──── BLE "word wakker" ◄── Windows-Bluetooth ◄─────┘ (BlueZ-brug)
```

- De **USB-stick** wordt met [usbipd-win](https://github.com/dorssel/usbipd-win) aan WSL gegeven, waar owly hem met de Linux-driver bestuurt. Je ingebouwde wifi blijft gewoon je internetverbinding.
- Voor **versturen** stuurt owly een Bluetooth-signaal dat Apple-apparaten AirDrop laat aanzetten. WSL heeft geen Bluetooth. Daarom vangt een kleine stand-in voor BlueZ (`wsl/bluez-shim.py`) dat signaal op, en WinDrop zendt het uit via de Bluetooth-adapter van Windows.

### Welke stick?

Een stick met een chip uit de [hardwaretabel van omdrop-owl](https://github.com/t4t5/omdrop-owl#hardware) die als werkend (✅) staat. Aanbevolen: **MediaTek MT7612U** (`0e8d:7612`), bijvoorbeeld de Alfa AWUS036ACM, ALLNET ALL-WA1200AC of PIX-LINK LV-UAC04. Controleer na aankomst met `usbipd list` dat `0e8d:7612` verschijnt.

Vermijd de COMFAST CF-WU782AC (die meldt zich eerst als USB-schijf) en sticks met een RTL8812AU- of RTL8821AU-chip.

## Gebruik

- **iPhone/Mac → pc**: delen → AirDrop → je pc. WinDrop vraagt "Accepteren / Weigeren", en het bestand komt in je map Downloads.
- **pc → iPhone/Mac**: rechtsklik op een bestand → *Meer opties weergeven* → **Delen via AirDrop**. Je kunt ook op het WinDrop-icoon klikken en bestanden kiezen.
- Zet AirDrop op het Apple-apparaat op **"Iedereen gedurende 10 minuten"**. "Alleen contacten" werkt alleen tussen Apple-apparaten.

## Installeren

Vereisten: Windows 11, WSL 2 met Ubuntu, [usbipd-win](https://github.com/dorssel/usbipd-win) (`winget install dorssel.usbipd-win`) en een ondersteunde USB-wifistick.

```powershell
dotnet build -c Release
src\WinDrop\bin\Release\net10.0-windows10.0.22621.0\WinDrop.exe setup
```

`setup` doet het volgende:
1. installeert bouwpakketten in WSL;
2. bouwt owly en de USB-wifidrivers voor de WSL-kernel (`wsl/build.sh`, 10 à 20 minuten). WSL's eigen kernel heeft geen wifidrivers; WinDrop bouwt alleen de modules en laat de kernel zelf ongemoeid;
3. installeert owly, de BlueZ-brug en een polkit-regel (`wsl/setup.sh`);
4. deelt de stick met WSL. Windows vraagt daarbij één keer om toestemming.

Daarna staat WinDrop in het systeemvak. In het menu vind je:
- "Zichtbaar voor AirDrop";
- "Delen via AirDrop in Verkenner";
- "Starten met Windows".

Ontvangen bestanden en de instellingen:
- ontvangen bestanden komen in `%USERPROFILE%\Downloads`;
- de instellingen staan in `%APPDATA%\WinDrop\settings.json` (`Distro`, `DisplayName`, `ReceiveDirectory`);
- het logboek staat in `%LOCALAPPDATA%\WinDrop\windrop.log`.

## Projectindeling

| Pad | Wat |
|---|---|
| `src/WinDrop/AirDrop` | owly aansturen: radio, ontvangen, zoeken, versturen, en de uitvoer van owly lezen |
| `src/WinDrop/Ble` | het AirDrop-BLE-signaal uitzenden via Windows |
| `src/WinDrop/Wsl` | WSL-processen en usbipd |
| `src/WinDrop/UI` | systeemvak, macOS-achtige vensters voor verzoeken en delen, installatievenster |
| `wsl/` | bouw- en installatiescripts, het laden van de modules, de BlueZ-brug |
| `tests/` | unit tests (owly-uitvoer, paden, usbipd, teksten) |

## Bekende beperkingen en open punten

- **Firmware in WSL**: drivers laden hun firmware via de kernel. Of die de firmware uit Ubuntu's `/lib/firmware` vindt, moet nog met een echte stick getest worden.
- **Snelheid**: owly is langzamer dan echte AirDrop. Foto's gaan prima, grote video's duren lang.
- **Verkenner-menu**: de optie staat in het klassieke menu ("Meer opties weergeven"). Een plek in het nieuwe Windows 11-menu vraagt een app-pakket.
- AirDrop is nagebouwd via reverse engineering en kan breken door updates van iOS of macOS.

## Licentie

MIT. owly/omdrop-owl is ook MIT-gelicentieerd.
