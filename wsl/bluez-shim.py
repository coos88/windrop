#!/usr/bin/env python3
"""Stand-in for BlueZ's LE advertising API inside WSL.

owly sends the BLE advert that wakes Apple devices through BlueZ
(org.bluez.LEAdvertisingManager1). WSL has no Bluetooth, so this owns
org.bluez on the system bus, accepts owly's advert, and prints it on stdout
for WinDrop, which sends it with Windows' own Bluetooth adapter:

    READY                       the name is ours
    ADV <company-hex> <data-hex>  start advertising this manufacturer data
    STOP                        stop advertising

WinDrop starts it as root (wsl -u root) and reads its stdout.
"""
import sys

import dbus
import dbus.mainloop.glib
import dbus.service
from gi.repository import GLib

BUS_NAME = "org.bluez"
ADAPTER_PATH = "/org/bluez/hci0"
ADAPTER = "org.bluez.Adapter1"
MANAGER = "org.bluez.LEAdvertisingManager1"
ADVERT = "org.bluez.LEAdvertisement1"
PROPS = "org.freedesktop.DBus.Properties"
OBJECT_MANAGER = "org.freedesktop.DBus.ObjectManager"


def emit(line):
    print(line, flush=True)


class Adapter(dbus.service.Object):
    def __init__(self, bus):
        super().__init__(bus, ADAPTER_PATH)
        self.bus = bus
        self.adverts = {}  # (sender, path) -> name watch

    def adapter_props(self):
        return {
            "Address": dbus.String("00:00:00:00:00:00"),
            "Name": dbus.String("WinDrop"),
            "Powered": dbus.Boolean(True),
        }

    def manager_props(self):
        return {
            "ActiveInstances": dbus.Byte(len(self.adverts)),
            "SupportedInstances": dbus.Byte(1),
        }

    def interfaces(self):
        return {ADAPTER: self.adapter_props(), MANAGER: self.manager_props()}

    @dbus.service.method(PROPS, in_signature="ss", out_signature="v")
    def Get(self, interface, prop):
        return self.interfaces().get(interface, {})[prop]

    @dbus.service.method(PROPS, in_signature="s", out_signature="a{sv}")
    def GetAll(self, interface):
        return self.interfaces().get(interface, {})

    @dbus.service.method(PROPS, in_signature="ssv")
    def Set(self, interface, prop, value):
        pass  # always powered

    @dbus.service.method(MANAGER, in_signature="oa{sv}", sender_keyword="sender")
    def RegisterAdvertisement(self, path, options, sender=None):
        obj = self.bus.get_object(sender, path, introspect=False)
        props = dbus.Interface(obj, PROPS).GetAll(ADVERT)
        data = props.get("ManufacturerData", {})
        if not data:
            raise dbus.exceptions.DBusException(
                "org.bluez.Error.InvalidArguments", "no ManufacturerData"
            )
        self.drop_all()  # one advert at a time, like the single Windows publisher
        company, value = next(iter(data.items()))
        payload = bytes(int(b) for b in value)
        key = (sender, str(path))
        self.adverts[key] = self.bus.watch_name_owner(
            sender, lambda owner, key=key: owner or self.drop(key)
        )
        emit(f"ADV {int(company):04x} {payload.hex()}")

    @dbus.service.method(MANAGER, in_signature="o", sender_keyword="sender")
    def UnregisterAdvertisement(self, path, sender=None):
        self.drop((sender, str(path)))

    def drop(self, key):
        watch = self.adverts.pop(key, None)
        if watch is not None:
            watch.cancel()
            if not self.adverts:
                emit("STOP")

    def drop_all(self):
        for key in list(self.adverts):
            self.drop(key)


class Root(dbus.service.Object):
    def __init__(self, bus, adapter):
        super().__init__(bus, "/")
        self.adapter = adapter

    @dbus.service.method(OBJECT_MANAGER, out_signature="a{oa{sa{sv}}}")
    def GetManagedObjects(self):
        return {dbus.ObjectPath(ADAPTER_PATH): self.adapter.interfaces()}


def main():
    dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)
    bus = dbus.SystemBus()
    try:
        name = dbus.service.BusName(BUS_NAME, bus, do_not_queue=True)
    except dbus.exceptions.NameExistsException:
        print("org.bluez is already owned (is a real BlueZ running?)", file=sys.stderr)
        return 1
    adapter = Adapter(bus)
    Root(bus, adapter)
    emit("READY")
    loop = GLib.MainLoop()
    # WinDrop closing our stdin means it's gone: stop.
    GLib.io_add_watch(
        sys.stdin.fileno(), GLib.PRIORITY_DEFAULT, GLib.IO_HUP | GLib.IO_IN,
        lambda *_: loop.quit() or False,
    )
    try:
        loop.run()
    finally:
        adapter.drop_all()
        del name
    return 0


if __name__ == "__main__":
    sys.exit(main())
