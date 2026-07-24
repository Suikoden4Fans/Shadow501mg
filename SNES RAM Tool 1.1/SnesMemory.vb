' SnesMemory.vb
Imports System.Diagnostics
Imports System.Runtime.InteropServices

Public Class SnesMemory
    Private Const PROCESS_ALL_ACCESS As Integer = &H1F0FFF
    Private processHandle As IntPtr = IntPtr.Zero
    Private targetProcess As Process = Nothing

    Public Property WramBaseAddress As ULong = 0

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function OpenProcess(dwDesiredAccess As Integer, bInheritHandle As Boolean, dwProcessId As Integer) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function ReadProcessMemory(hProcess As IntPtr, lpBaseAddress As IntPtr, buffer As Byte(), size As Integer, ByRef lpNumberOfBytesRead As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WriteProcessMemory(hProcess As IntPtr, lpBaseAddress As IntPtr, lpBuffer As Byte(), size As Integer, ByRef lpNumberOfBytesWritten As Integer) As Boolean
    End Function

    Public Function Attach(processName As String) As Boolean
        Dim processes = Process.GetProcessesByName(processName)
        If processes.Length > 0 Then
            targetProcess = processes(0)
            processHandle = OpenProcess(PROCESS_ALL_ACCESS, False, targetProcess.Id)
            Return processHandle <> IntPtr.Zero
        End If
        Return False
    End Function

    Public Function ReadWRAM(offset As Integer, length As Integer) As Byte()
        If processHandle = IntPtr.Zero Then Return New Byte(length - 1) {}

        Dim buffer(length - 1) As Byte
        Dim bytesRead As Integer = 0
        Dim targetAddress As IntPtr

        ' Standard SNES WRAM offset calculation
        Dim finalAddress As ULong = WramBaseAddress + CULng(offset)
        targetAddress = New IntPtr(CType(finalAddress, Long))

        ReadProcessMemory(processHandle, targetAddress, buffer, length, bytesRead)
        Return buffer
    End Function

    Public Sub WriteWRAMByte(offset As Integer, value As Byte)
        If processHandle = IntPtr.Zero Then Return

        Dim buffer() As Byte = {value}
        Dim bytesWritten As Integer = 0
        Dim targetAddress As IntPtr

        ' Standard SNES WRAM offset calculation
        Dim finalAddress As ULong = WramBaseAddress + CULng(offset)
        targetAddress = New IntPtr(CType(finalAddress, Long))

        WriteProcessMemory(processHandle, targetAddress, buffer, 1, bytesWritten)
    End Sub
End Class