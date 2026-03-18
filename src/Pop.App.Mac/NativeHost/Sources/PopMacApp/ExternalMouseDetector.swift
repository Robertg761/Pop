import Foundation
import IOKit.hid

struct ExternalMouseDetector {
    func hasExternalMouseConnected() -> Bool {
        let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
        IOHIDManagerSetDeviceMatching(manager, nil)

        let openResult = IOHIDManagerOpen(manager, IOOptionBits(kIOHIDOptionsTypeNone))
        guard openResult == kIOReturnSuccess else {
            return false
        }

        defer {
            IOHIDManagerClose(manager, IOOptionBits(kIOHIDOptionsTypeNone))
        }

        guard let devices = IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice> else {
            return false
        }

        return devices.contains { device in
            guard
                integerProperty(for: device, key: kIOHIDPrimaryUsagePageKey as CFString) == Int(kHIDPage_GenericDesktop),
                integerProperty(for: device, key: kIOHIDPrimaryUsageKey as CFString) == Int(kHIDUsage_GD_Mouse)
            else {
                return false
            }

            return !booleanProperty(for: device, key: kIOHIDBuiltInKey as CFString)
        }
    }

    private func integerProperty(for device: IOHIDDevice, key: CFString) -> Int? {
        guard let value = IOHIDDeviceGetProperty(device, key) else {
            return nil
        }

        if let number = value as? NSNumber {
            return number.intValue
        }

        return nil
    }

    private func booleanProperty(for device: IOHIDDevice, key: CFString) -> Bool {
        guard let value = IOHIDDeviceGetProperty(device, key) else {
            return false
        }

        if let number = value as? NSNumber {
            return number.boolValue
        }

        return false
    }
}
