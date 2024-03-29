﻿using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Microsoft.Extensions.Logging;

using static Windows.Win32.PInvoke;
using System.Diagnostics;
using System.Buffers;
using ProcessReporterWin.Helper;

namespace ProcessReporterWin.Services;

// Reference: https://github.com/walterlv/Walterlv.ForegroundWindowMonitor

public class ProcessTraceService
{
    private HWINEVENTHOOK _hookHandle;

    private readonly ILogger<ProcessTraceService> _logger;

    public delegate void FrontWindowChangeHandler(string windowTitle, string processName);

    public event FrontWindowChangeHandler OnFrontWindowChanged;

    public ProcessTraceService(ILogger<ProcessTraceService> logger)
    {
        _logger = logger;

        _hookHandle = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, 
            EVENT_SYSTEM_FOREGROUND, 
            HMODULE.Null,
            OnFrontWindowChange,
            0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        // 开启消息循环，以便 WinEventProc 能够被调用。
        if (GetMessage(out var lpMsg, default, default, default))
        {
            TranslateMessage(in lpMsg);
            DispatchMessage(in lpMsg);
        }
    }

    ~ProcessTraceService()
    {
        UnhookWinEvent(_hookHandle);
    }

    private static void OnFrontWindowChange(HWINEVENTHOOK eventHook, uint @event, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        var currentWindow = GetForegroundWindow();

        var processId = GetProcessIdCore(currentWindow);

        var processName = Process.GetProcessById((int)processId).ProcessName;

        var windowTitle = CallWin32ToGetPWSTR(512, (p, l) => GetWindowText(currentWindow, p, l));

        var service = ServiceHelper.GetService<ProcessTraceService>();

        if (service?.OnFrontWindowChanged is not null)
        {
            service.OnFrontWindowChanged(windowTitle, processName);
        }
    }

    private static unsafe uint GetProcessIdCore(HWND hWnd)
    {
        uint pid = 0;
        GetWindowThreadProcessId(hWnd, &pid);
        return pid;
    }

    private static unsafe string CallWin32ToGetPWSTR(int bufferLength, Func<PWSTR, int, int> getter)
    {
        var buffer = ArrayPool<char>.Shared.Rent(bufferLength);
        try
        {
            fixed (char* ptr = buffer)
            {
                getter(ptr, bufferLength);
                return new string(buffer, 0, Array.IndexOf(buffer, '\0'));
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}
