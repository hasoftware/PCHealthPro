namespace PCHealthPro.Core.Inventory;

public static class InventoryCatalog
{
    public static IEnumerable<IHardwareModule> Create(IInventorySource source)
    {
        const string wmi = @"root\WMI";
        const string standard = @"root\StandardCimv2";
        yield return new InventoryModule("Machine", source,
            new InventoryQuery("Machine", "Win32_ComputerSystem", "Model,Manufacturer,PCSystemType"),
            new InventoryQuery("BIOS", "Win32_BIOS", "SMBIOSBIOSVersion,Manufacturer,ReleaseDate,SerialNumber"),
            new InventoryQuery("Board", "Win32_BaseBoard", "Product,Manufacturer,SerialNumber"),
            new InventoryQuery("USB", "Win32_USBController", "Name,Status"),
            new InventoryQuery("TPM", "Win32_Tpm", "SpecVersion,IsEnabled_InitialValue", @"root\CIMV2\Security\MicrosoftTpm"),
            new InventoryQuery("SecureBoot", "SecureBootRegistry", "SecureBootState"));
        yield return new InventoryModule("CPU", source,
            new InventoryQuery("CPU", "Win32_Processor", "Name,NumberOfCores,NumberOfLogicalProcessors,CurrentClockSpeed,MaxClockSpeed,L2CacheSize,L3CacheSize,Architecture"),
            new InventoryQuery("CPU", "ProcessorRegistry", "NominalMHz"));
        yield return new InventoryModule("RAM", source,
            new InventoryQuery("MemoryModule", "Win32_PhysicalMemory", "Capacity,DeviceLocator,SMBIOSMemoryType,ConfiguredClockSpeed,Manufacturer,PartNumber"),
            new InventoryQuery("MemoryArray", "Win32_PhysicalMemoryArray", "MemoryDevices", Filter: "Use = 3"));
        yield return new InventoryModule("GPU", source,
            new InventoryQuery("GPU", "Win32_VideoController", "Name,AdapterRAM,DriverVersion,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate,PNPDeviceID"));
        yield return new InventoryModule("Display", source,
            new InventoryQuery("Panel", "WmiMonitorID", "UserFriendlyName,ManufacturerName,ProductCodeID,InstanceName", wmi, "Active = TRUE"),
            new InventoryQuery("Dimensions", "WmiMonitorBasicDisplayParams", "InstanceName,MaxHorizontalImageSize,MaxVerticalImageSize", wmi, "Active = TRUE"),
            new InventoryQuery("PresentPanels", "PresentPanels", "Name,PanelName,MaxHorizontalImageSize,MaxVerticalImageSize"),
            new InventoryQuery("DisplayMode", "DisplayModes", "DisplayName,PanelName,Resolution,RefreshHz,MaxHorizontalImageSize,MaxVerticalImageSize"));
        yield return new InventoryModule("Network", source,
            new InventoryQuery("Network", "MSFT_NetAdapter", "InterfaceDescription,Name,DriverProvider,DriverVersionString,PermanentAddress,ReceiveLinkSpeed,TransmitLinkSpeed,InterfaceType", standard, "HardwareInterface = TRUE"),
            new InventoryQuery("Network", "Win32_NetworkAdapter", "Name,Manufacturer,MACAddress", Filter: "PhysicalAdapter = TRUE"),
            new InventoryQuery("Wifi", "WifiCapabilities", "InterfaceDescription,RadioTypes"),
            Pnp("Bluetooth", "PNPClass = 'Bluetooth'"));
        yield return new InventoryModule("Windows", source,
            new InventoryQuery("Windows", "Win32_OperatingSystem", "Caption,Version,BuildNumber,InstallDate,OSArchitecture"),
            new InventoryQuery("WindowsActivation", "SoftwareLicensingProduct", "Name,Description,LicenseStatus,PartialProductKey,KeyManagementServiceMachine,DiscoveredKeyManagementServiceMachineName",
                Filter: "ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL"),
            new InventoryQuery("Office", "OfficeRegistry", "ProductReleaseIds,VersionToReport"),
            new InventoryQuery("OfficeActivation", "SoftwareLicensingProduct", "Name,Description,LicenseStatus,PartialProductKey,KeyManagementServiceMachine,DiscoveredKeyManagementServiceMachineName",
                Filter: "ApplicationID = '0ff1ce15-a989-479d-af46-f275c6370663' AND PartialProductKey IS NOT NULL"),
            new InventoryQuery("OfficeLegacy", "OfficeSoftwareProtectionProduct", "Name,Description,LicenseStatus,PartialProductKey,KeyManagementServiceMachine",
                Filter: "PartialProductKey IS NOT NULL"));
        yield return new InventoryModule("Camera", source, Pnp("Camera", "PNPClass = 'Camera' OR PNPClass = 'Image'"));
        yield return new InventoryModule("Audio", source, Pnp("Audio", "PNPClass = 'AudioEndpoint'"));
        yield return new InventoryModule("Input", source, Pnp("Input", "PNPClass = 'Mouse' OR PNPClass = 'Keyboard' OR Name LIKE '%touchpad%' OR Name LIKE '%trackpad%'"));
        // Basic enumeration only. SMART, disk health and battery wear belong to phase 3.
        yield return new InventoryModule("Storage", source, new InventoryQuery("Storage", "Win32_DiskDrive", "Model,Size"));
        yield return new InventoryModule("Battery", source, new InventoryQuery("Battery", "Win32_Battery", "Name,EstimatedChargeRemaining"));
    }

    private static InventoryQuery Pnp(string label, string condition) =>
        new(label, "Win32_PnPEntity", "Name,Manufacturer,Status", Filter: $"Present = TRUE AND ({condition})");
}
