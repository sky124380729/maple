#include <ntddk.h>
#include <wdf.h>
#include "device.h"

DRIVER_INITIALIZE DriverEntry;

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    WDF_DRIVER_CONFIG config;

    ExInitializeDriverRuntime(DrvRtPoolNxOptIn);
    WDF_DRIVER_CONFIG_INIT(&config, MapleEvtDeviceAdd);
    return WdfDriverCreate(
        DriverObject,
        RegistryPath,
        WDF_NO_OBJECT_ATTRIBUTES,
        &config,
        WDF_NO_HANDLE);
}
