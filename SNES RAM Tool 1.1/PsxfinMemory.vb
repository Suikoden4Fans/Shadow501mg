' PsxfinMemory.vb
Imports System.Diagnostics
Imports System.Runtime.InteropServices

Public Class PsxfinMemory
    ' --- Windows API Functions for Memory Reading/Writing ---
    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function OpenProcess(processAccess As Integer, bInheritHandle As Boolean, processId As Integer) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function ReadProcessMemory(hProcess As IntPtr, lpBaseAddress As IntPtr, lpBuffer As Byte(), dwSize As Integer, ByRef lpNumberOfBytesRead As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WriteProcessMemory(hProcess As IntPtr, lpBaseAddress As IntPtr, lpBuffer As Byte(), nSize As Integer, ByRef lpNumberOfBytesWritten As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(hObject As IntPtr) As Boolean
    End Function

    Private Const PROCESS_ALL_ACCESS As Integer = &H1F0FFF
    Private processHandle As IntPtr = IntPtr.Zero

    Public Property RamBaseAddress As ULong = 0
    Public Const PSX_RAM_SIZE As Integer = 2097152 ' 2MB

    Public Function Attach() As Boolean
        If processHandle <> IntPtr.Zero Then
            CloseHandle(processHandle)
            processHandle = IntPtr.Zero
        End If

        Dim processes As Process() = Process.GetProcessesByName("psxfin")
        If processes.Length = 0 Then Return False

        Dim targetProcess As Process = processes(0)
        processHandle = OpenProcess(PROCESS_ALL_ACCESS, False, targetProcess.Id)
        If processHandle = IntPtr.Zero Then Return False

        ' AUTOMATICALLY ASSIGN YOUR CONFIRMED BASE ADDRESS
        ' No input box needed! It locks straight onto the RAM.
        RamBaseAddress = &HCA8E100UL

        Return True
    End Function

    ' --- ROBUST MEMORY READER (Optimized for Speed) ---
    Public Function ReadRAM(offset As Integer, size As Integer) As Byte()
        If processHandle = IntPtr.Zero OrElse RamBaseAddress = 0 Then Return Nothing

        Dim buffer(size - 1) As Byte
        Dim bytesRead As Integer = 0
        Dim targetAddress As IntPtr = New IntPtr(CLng(RamBaseAddress) + offset)

        If ReadProcessMemory(processHandle, targetAddress, buffer, size, bytesRead) Then

        End If

        Dim pageSize As Integer = 4096
        For i As Integer = 0 To size - 1 Step pageSize
            Dim chunkSize As Integer = Math.Min(pageSize, size - i)
            Dim chunkTarget As IntPtr = New IntPtr(targetAddress.ToInt64() + i)
            Dim tempBuf(chunkSize - 1) As Byte

            If ReadProcessMemory(processHandle, chunkTarget, tempBuf, chunkSize, bytesRead) Then
                Array.Copy(tempBuf, 0, buffer, i, chunkSize)
            End If
        Next
        Return buffer
    End Function



    Public Sub WriteRAMByte(offset As Integer, val As Byte)
        If processHandle = IntPtr.Zero OrElse RamBaseAddress = 0 Then Return

        Dim buffer(0) As Byte
        buffer(0) = val
        Dim bytesWritten As Integer = 0
        Dim targetAddress As IntPtr = New IntPtr(CLng(RamBaseAddress) + offset)

        WriteProcessMemory(processHandle, targetAddress, buffer, 1, bytesWritten)
    End Sub

    Protected Overrides Sub Finalize()
        If processHandle <> IntPtr.Zero Then
            CloseHandle(processHandle)
        End If
        MyBase.Finalize()
    End Sub
End Class