' MemorySearcher.vb
Imports System.Collections.Generic

Public Class SearchResult
    Public Property Offset As Integer
End Class

Public Class MemorySearcher
    Private snesMem As SnesMemory
    Private psxMem As PsxfinMemory

    ' Toggle this flag from Form1 to switch emulator modes
    Public Property IsPsxMode As Boolean = False

    Public Property MatchingResults As List(Of SearchResult) = New List(Of SearchResult)()
    Public Property FrozenAddresses As Dictionary(Of Integer, Byte) = New Dictionary(Of Integer, Byte)()
    Private snapshotData As Byte() = Nothing

    Public Sub New(snesInstance As SnesMemory, psxInstance As PsxfinMemory)
        snesMem = snesInstance
        psxMem = psxInstance
    End Sub

    ' Automatically returns 2MB for PSX or 128KB for SNES
    Private Function GetRamSize() As Integer
        Return If(IsPsxMode, 2097152, 131072)
    End Function

    ' Wrapper to read from the correct emulator
    Private Function ReadMem(offset As Integer, size As Integer) As Byte()
        Return If(IsPsxMode, psxMem.ReadRAM(offset, size), snesMem.ReadWRAM(offset, size))
    End Function

    ' Wrapper to write to the correct emulator
    Private Sub WriteMemByte(offset As Integer, val As Byte)
        If IsPsxMode Then
            psxMem.WriteRAMByte(offset, val)
        Else
            snesMem.WriteWRAMByte(offset, val)
        End If
    End Sub

    Public Sub ApplyFreezes()
        For Each kvp In FrozenAddresses
            WriteMemByte(kvp.Key, kvp.Value)
        Next
    End Sub

    Public Function IsFrozen(offset As Integer) As Boolean
        Return FrozenAddresses.ContainsKey(offset)
    End Function

    Public Function FirstScanUnknown() As Integer
        snapshotData = ReadMem(0, GetRamSize())
        If snapshotData Is Nothing OrElse snapshotData.Length = 0 Then Return -1

        MatchingResults.Clear()
        For i As Integer = 0 To snapshotData.Length - 1
            MatchingResults.Add(New SearchResult() With {.Offset = i})
        Next
        Return MatchingResults.Count
    End Function

    Public Function FirstScan(val As UInteger, size As Integer) As Integer
        snapshotData = ReadMem(0, GetRamSize())
        If snapshotData Is Nothing OrElse snapshotData.Length = 0 Then Return 0

        MatchingResults.Clear()
        For i As Integer = 0 To snapshotData.Length - size
            If ReadValue(snapshotData, i, size) = val Then
                MatchingResults.Add(New SearchResult() With {.Offset = i})
            End If
        Next
        Return MatchingResults.Count
    End Function

    Public Function NextScanExact(val As UInteger, size As Integer) As Integer
        Dim newSnap = ReadMem(0, GetRamSize())
        If newSnap Is Nothing OrElse newSnap.Length = 0 Then Return MatchingResults.Count

        Dim filtered As New List(Of SearchResult)()
        For Each res In MatchingResults
            If res.Offset + size <= newSnap.Length AndAlso ReadValue(newSnap, res.Offset, size) = val Then
                filtered.Add(res)
            End If
        Next

        MatchingResults = filtered
        snapshotData = newSnap
        Return MatchingResults.Count
    End Function

    Public Function NextScanGreaterThan(size As Integer) As Integer
        Dim newSnap = ReadMem(0, GetRamSize())
        If newSnap Is Nothing OrElse newSnap.Length = 0 Then Return MatchingResults.Count

        Dim filtered As New List(Of SearchResult)()
        For Each res In MatchingResults
            If res.Offset + size <= newSnap.Length AndAlso ReadValue(newSnap, res.Offset, size) > ReadValue(snapshotData, res.Offset, size) Then
                filtered.Add(res)
            End If
        Next

        MatchingResults = filtered
        snapshotData = newSnap
        Return MatchingResults.Count
    End Function

    Public Function NextScanLessThanOrEqual(size As Integer) As Integer
        Dim newSnap = ReadMem(0, GetRamSize())
        If newSnap Is Nothing OrElse newSnap.Length = 0 Then Return MatchingResults.Count

        Dim filtered As New List(Of SearchResult)()
        For Each res In MatchingResults
            If res.Offset + size <= newSnap.Length AndAlso ReadValue(newSnap, res.Offset, size) <= ReadValue(snapshotData, res.Offset, size) Then
                filtered.Add(res)
            End If
        Next

        MatchingResults = filtered
        snapshotData = newSnap
        Return MatchingResults.Count
    End Function

    Private Function ReadValue(buf As Byte(), idx As Integer, sz As Integer) As UInteger
        If idx + sz > buf.Length Then Return 0
        Select Case sz
            Case 1 : Return buf(idx)
            Case 2 : Return BitConverter.ToUInt16(buf, idx)
            Case 3 : Return CType(buf(idx), UInteger) Or (CType(buf(idx + 1), UInteger) << 8) Or (CType(buf(idx + 2), UInteger) << 16)
            Case 4 : Return BitConverter.ToUInt32(buf, idx)
            Case Else : Return buf(idx)
        End Select
    End Function
End Class