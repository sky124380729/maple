#pragma once

#include <ntddk.h>
#include <wdf.h>
#include <vhf.h>

typedef struct _MAPLE_DEVICE_CONTEXT {
    VHFHANDLE VhfHandle;
    WDFTIMER WatchdogTimer;
    ULONG LastSequence;
    BOOLEAN VhfStarted;
} MAPLE_DEVICE_CONTEXT, *PMAPLE_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(MAPLE_DEVICE_CONTEXT, MapleGetDeviceContext);

EVT_WDF_DRIVER_DEVICE_ADD MapleEvtDeviceAdd;
EVT_WDF_OBJECT_CONTEXT_CLEANUP MapleEvtDeviceCleanup;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL MapleEvtIoDeviceControl;
EVT_WDF_TIMER MapleEvtWatchdog;
