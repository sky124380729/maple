#include <initguid.h>
#include <ntddk.h>
#include <wdf.h>
#include <vhf.h>
#include "public.h"
#include "protocol.h"
#include "device.h"

#define MAPLE_VID 0xF1AE
#define MAPLE_PID 0x0001
#define MAPLE_VERSION 0x0001

static UCHAR MapleKeyboardReportDescriptor[] = {
    0x05, 0x01,
    0x09, 0x06,
    0xA1, 0x01,
    0x05, 0x07,
    0x19, 0xE0,
    0x29, 0xE7,
    0x15, 0x00,
    0x25, 0x01,
    0x75, 0x01,
    0x95, 0x08,
    0x81, 0x02,
    0x95, 0x01,
    0x75, 0x08,
    0x81, 0x01,
    0x95, 0x06,
    0x75, 0x08,
    0x15, 0x00,
    0x25, 0x65,
    0x05, 0x07,
    0x19, 0x00,
    0x29, 0x65,
    0x81, 0x00,
    0xC0
};

static const UCHAR MapleNeutralReport[MAPLE_HID_KEYBOARD_REPORT_LENGTH] = { 0 };

static
NTSTATUS
MapleSubmitReport(
    _In_ PMAPLE_DEVICE_CONTEXT Context,
    _In_reads_(MAPLE_HID_KEYBOARD_REPORT_LENGTH) const UCHAR* Report
    )
{
    HID_XFER_PACKET packet;

    if (!Context->VhfStarted || Context->VhfHandle == NULL) {
        return STATUS_DEVICE_NOT_READY;
    }

    packet.reportBuffer = (PUCHAR)Report;
    packet.reportBufferLen = MAPLE_HID_KEYBOARD_REPORT_LENGTH;
    packet.reportId = 0;
    return VhfReadReportSubmit(Context->VhfHandle, &packet);
}

static
BOOLEAN
MapleReportIsNeutral(
    _In_reads_(MAPLE_HID_KEYBOARD_REPORT_LENGTH) const UCHAR* Report
    )
{
    ULONG index;
    for (index = 0; index < MAPLE_HID_KEYBOARD_REPORT_LENGTH; index++) {
        if (Report[index] != 0) return FALSE;
    }
    return TRUE;
}

NTSTATUS
MapleEvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
    )
{
    NTSTATUS status;
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_TIMER_CONFIG timerConfig;
    WDF_OBJECT_ATTRIBUTES timerAttributes;
    VHF_CONFIG vhfConfig;
    PMAPLE_DEVICE_CONTEXT context;
    DECLARE_CONST_UNICODE_STRING(deviceSddl, L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;BU)");

    UNREFERENCED_PARAMETER(Driver);

    WdfDeviceInitSetDeviceType(DeviceInit, FILE_DEVICE_UNKNOWN);
    status = WdfDeviceInitAssignSDDLString(DeviceInit, &deviceSddl);
    if (!NT_SUCCESS(status)) return status;

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, MAPLE_DEVICE_CONTEXT);
    deviceAttributes.EvtCleanupCallback = MapleEvtDeviceCleanup;
    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status)) return status;

    context = MapleGetDeviceContext(device);
    RtlZeroMemory(context, sizeof(*context));

    status = WdfDeviceCreateDeviceInterface(device, &GUID_DEVINTERFACE_MAPLE_VHF_KEYBOARD, NULL);
    if (!NT_SUCCESS(status)) return status;

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = MapleEvtIoDeviceControl;
    status = WdfIoQueueCreate(device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
    if (!NT_SUCCESS(status)) return status;

    WDF_TIMER_CONFIG_INIT(&timerConfig, MapleEvtWatchdog);
    timerConfig.AutomaticSerialization = TRUE;
    WDF_OBJECT_ATTRIBUTES_INIT(&timerAttributes);
    timerAttributes.ParentObject = device;
    status = WdfTimerCreate(&timerConfig, &timerAttributes, &context->WatchdogTimer);
    if (!NT_SUCCESS(status)) return status;

    VHF_CONFIG_INIT(
        &vhfConfig,
        WdfDeviceWdmGetDeviceObject(device),
        (USHORT)sizeof(MapleKeyboardReportDescriptor),
        MapleKeyboardReportDescriptor);
    vhfConfig.VendorID = MAPLE_VID;
    vhfConfig.ProductID = MAPLE_PID;
    vhfConfig.VersionNumber = MAPLE_VERSION;

    status = VhfCreate(&vhfConfig, &context->VhfHandle);
    if (!NT_SUCCESS(status)) return status;

    status = VhfStart(context->VhfHandle);
    if (!NT_SUCCESS(status)) {
        VhfDelete(context->VhfHandle, TRUE);
        context->VhfHandle = NULL;
        return status;
    }

    context->VhfStarted = TRUE;
    status = MapleSubmitReport(context, MapleNeutralReport);
    if (!NT_SUCCESS(status)) return status;
    WdfTimerStart(context->WatchdogTimer, WDF_REL_TIMEOUT_IN_MS(MAPLE_HID_WATCHDOG_TIMEOUT_MS));
    return STATUS_SUCCESS;
}

VOID
MapleEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
{
    NTSTATUS status;
    PMAPLE_HID_REQUEST input;
    size_t inputLength;
    WDFDEVICE device = WdfIoQueueGetDevice(Queue);
    PMAPLE_DEVICE_CONTEXT context = MapleGetDeviceContext(device);

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    if (IoControlCode != IOCTL_MAPLE_HID_SUBMIT_REPORT && IoControlCode != IOCTL_MAPLE_HID_HEARTBEAT) {
        WdfRequestComplete(Request, STATUS_INVALID_DEVICE_REQUEST);
        return;
    }

    status = WdfRequestRetrieveInputBuffer(Request, sizeof(MAPLE_HID_REQUEST), (PVOID*)&input, &inputLength);
    if (!NT_SUCCESS(status) || inputLength != sizeof(MAPLE_HID_REQUEST)) {
        WdfRequestComplete(Request, NT_SUCCESS(status) ? STATUS_INFO_LENGTH_MISMATCH : status);
        return;
    }

    if (input->Magic != MAPLE_HID_MAGIC
        || input->Version != MAPLE_HID_VERSION
        || input->Sequence == 0
        || input->Sequence <= context->LastSequence) {
        WdfRequestComplete(Request, STATUS_INVALID_PARAMETER);
        return;
    }

    if (IoControlCode == IOCTL_MAPLE_HID_SUBMIT_REPORT
        && input->Command == MAPLE_HID_COMMAND_SUBMIT_REPORT) {
        status = MapleSubmitReport(context, input->Report);
    }
    else if (IoControlCode == IOCTL_MAPLE_HID_HEARTBEAT
        && input->Command == MAPLE_HID_COMMAND_HEARTBEAT
        && MapleReportIsNeutral(input->Report)) {
        status = STATUS_SUCCESS;
    }
    else {
        status = STATUS_INVALID_PARAMETER;
    }

    if (NT_SUCCESS(status)) {
        context->LastSequence = input->Sequence;
        WdfTimerStart(context->WatchdogTimer, WDF_REL_TIMEOUT_IN_MS(MAPLE_HID_WATCHDOG_TIMEOUT_MS));
    }
    WdfRequestComplete(Request, status);
}

VOID
MapleEvtWatchdog(
    _In_ WDFTIMER Timer
    )
{
    WDFDEVICE device = (WDFDEVICE)WdfTimerGetParentObject(Timer);
    PMAPLE_DEVICE_CONTEXT context = MapleGetDeviceContext(device);
    (VOID)MapleSubmitReport(context, MapleNeutralReport);
}

VOID
MapleEvtDeviceCleanup(
    _In_ WDFOBJECT DeviceObject
    )
{
    WDFDEVICE device = (WDFDEVICE)DeviceObject;
    PMAPLE_DEVICE_CONTEXT context = MapleGetDeviceContext(device);

    if (context->WatchdogTimer != NULL) {
        WdfTimerStop(context->WatchdogTimer, TRUE);
    }
    if (context->VhfHandle != NULL) {
        if (context->VhfStarted) (VOID)MapleSubmitReport(context, MapleNeutralReport);
        context->VhfStarted = FALSE;
        VhfDelete(context->VhfHandle, TRUE);
        context->VhfHandle = NULL;
    }
}
