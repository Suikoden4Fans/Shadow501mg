' Form1.vb
Imports System.Diagnostics
Imports System.Drawing
Imports System.Linq
Imports System.Collections.Generic
Imports System.Windows.Forms

Public Class Form1
    Private snesMem As New SnesMemory()
    Private psxMem As New PsxfinMemory()
    Private searcher As MemorySearcher
    Private isPsxMode As Boolean = False

    Private hexContextMenu As New ContextMenuStrip()
    Private lastSearchQuery As String = String.Empty
    Private lastSearchIndex As Integer = 0
    Private originalRamSnapshot As Byte() = Nothing

    Private Function GetRamSize() As Integer
        Return If(isPsxMode, 2097152, 131072)
    End Function

    Private Function ReadMemory(offset As Integer, size As Integer) As Byte()
        Return If(isPsxMode, psxMem.ReadRAM(offset, size), snesMem.ReadWRAM(offset, size))
    End Function

    Private Sub WriteMemoryByte(offset As Integer, val As Byte)
        If isPsxMode Then
            psxMem.WriteRAMByte(offset, val)
        Else
            snesMem.WriteWRAMByte(offset, val)
        End If
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Me.KeyPreview = True
        searcher = New MemorySearcher(snesMem, psxMem)

        SetupRichTextBoxStyle()
        SetupContextMenu()
        RefreshProcessList()

        Dim rb1 = Me.Controls.Find("RadioButton1Byte", True).FirstOrDefault()
        If rb1 Is Nothing Then rb1 = Me.Controls.Find("RadioButton1", True).FirstOrDefault()
        If rb1 IsNot Nothing Then CType(rb1, RadioButton).Checked = True

        For Each ctrl As Control In Me.Controls
            AttachRadioButtonRecursive(ctrl)
        Next

        PopulateDefaultAddressList()
        TimerRefresh.Interval = 500
    End Sub

    Private Sub SetupRichTextBoxStyle()
        RichTextBoxHex.Font = New Font("Consolas", 10, FontStyle.Regular)
        RichTextBoxHex.DetectUrls = False
        RichTextBoxHex.WordWrap = False
        RichTextBoxHex.BackColor = Color.White
        RichTextBoxHex.HideSelection = False
        RichTextBoxHex.Modified = False
    End Sub

    Private Sub PopulateDefaultAddressList()
        ListBoxSearchResults.Items.Clear()
        ListBoxSearchResults.BeginUpdate()
        ListBoxSearchResults.Items.Add($"Address".PadRight(16) & $"HEX".PadRight(12) & "DEC")
        ListBoxSearchResults.Items.Add(New String("-"c, 38))
        For offset As Integer = 0 To 65535 Step 16
            Dim absAddr As UInteger = &H80000000UI + CUInt(offset)
            ListBoxSearchResults.Items.Add($"{absAddr.ToString("X8").PadRight(16)}{"??".PadRight(12)}{"??"}")
        Next
        ListBoxSearchResults.EndUpdate()
    End Sub

    Private Sub AttachRadioButtonRecursive(parent As Control)
        For Each child As Control In parent.Controls
            If TypeOf child Is RadioButton Then
                AddHandler CType(child, RadioButton).CheckedChanged, AddressOf OnDataSizeChanged
            ElseIf child.HasChildren Then
                AttachRadioButtonRecursive(child)
            End If
        Next
    End Sub

    Private Function GetSelectedDataSize() As Integer
        Dim rb1 = Me.Controls.Find("RadioButton1Byte", True).FirstOrDefault()
        If rb1 Is Nothing Then rb1 = Me.Controls.Find("RadioButton1", True).FirstOrDefault()
        If rb1 IsNot Nothing AndAlso CType(rb1, RadioButton).Checked Then Return 1

        Dim rb2 = Me.Controls.Find("RadioButton2Bytes", True).FirstOrDefault()
        If rb2 Is Nothing Then rb2 = Me.Controls.Find("RadioButton2", True).FirstOrDefault()
        If rb2 IsNot Nothing AndAlso CType(rb2, RadioButton).Checked Then Return 2

        Dim rb3 = Me.Controls.Find("RadioButton3Bytes", True).FirstOrDefault()
        If rb3 Is Nothing Then rb3 = Me.Controls.Find("RadioButton3", True).FirstOrDefault()
        If rb3 IsNot Nothing AndAlso CType(rb3, RadioButton).Checked Then Return 3

        Dim rb4 = Me.Controls.Find("RadioButton4Bytes", True).FirstOrDefault()
        If rb4 Is Nothing Then rb4 = Me.Controls.Find("RadioButton4", True).FirstOrDefault()
        If rb4 IsNot Nothing AndAlso CType(rb4, RadioButton).Checked Then Return 4
        Return 1
    End Function

    Private Sub Form1_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        If e.Control AndAlso e.KeyCode = Keys.G Then
            e.SuppressKeyPress = True
            ShowGotoAddressDialog()
        ElseIf e.KeyCode = Keys.F1 Then
            e.SuppressKeyPress = True
            FindNextInRAM()
        End If
    End Sub

    Private Sub ShowGotoAddressDialog()
        Dim inputAddr As String = InputBox("Enter Address to jump to (e.g. 80000000 or 80012345):", "Go to RAM Address", "80000000")
        If String.IsNullOrWhiteSpace(inputAddr) Then Return

        Dim cleanHex As String = inputAddr.Trim().Replace("$", "").Replace("0x", "").Replace("0X", "").Replace(" ", "")
        Dim targetAddress As UInteger

        If UInteger.TryParse(cleanHex, System.Globalization.NumberStyles.HexNumber, Nothing, targetAddress) Then
            AddressOfShowGoto(targetAddress)
        Else
            MessageBox.Show("Invalid Hexadecimal address format!", "Parse Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

    Private Sub AddressOfShowGoto(targetAddress As UInteger)
        Dim offset As Integer = CInt(targetAddress - &H80000000UI)
        Dim targetRow As Integer = offset \ 16

        If targetRow >= 0 AndAlso targetRow < (GetRamSize() \ 16) Then
            Dim targetLineIndex As Integer = Math.Min(targetRow, RichTextBoxHex.Lines.Length - 1)
            If targetLineIndex >= 0 Then
                Dim charIndex As Integer = RichTextBoxHex.GetFirstCharIndexFromLine(targetLineIndex)
                If charIndex >= 0 Then
                    RichTextBoxHex.Select(charIndex, 48)
                    RichTextBoxHex.ScrollToCaret()
                End If
            End If
        Else
            MessageBox.Show("Address out of range for current Emulator!", "Invalid Address", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    Private Sub SetupContextMenu()
        hexContextMenu.Items.Clear()
        hexContextMenu.Items.Add(New ToolStripMenuItem("Copy", Nothing, AddressOf OnContextMenuCopyClick))
        hexContextMenu.Items.Add(New ToolStripMenuItem("Paste", Nothing, AddressOf OnContextMenuPasteClick))
        hexContextMenu.Items.Add(New ToolStripSeparator())
        hexContextMenu.Items.Add(New ToolStripMenuItem("Find Hex / Text in RAM... (F1 for Next)", Nothing, AddressOf OnContextMenuFindClick))
        hexContextMenu.Items.Add(New ToolStripMenuItem("Replace in RAM...", Nothing, AddressOf OnContextMenuReplaceClick))
        hexContextMenu.Items.Add(New ToolStripMenuItem("Replace All in RAM...", Nothing, AddressOf OnContextMenuReplaceAllClick))
        hexContextMenu.Items.Add(New ToolStripMenuItem("Undo All (Restore Original RAM)", Nothing, AddressOf OnContextMenuUndoAllClick))
        hexContextMenu.Items.Add(New ToolStripSeparator())
        hexContextMenu.Items.Add(New ToolStripMenuItem("Search Value in RAM...", Nothing, AddressOf OnContextMenuSearchValueClick))
        hexContextMenu.Items.Add(New ToolStripSeparator())
        hexContextMenu.Items.Add(New ToolStripMenuItem("Freeze Value", Nothing, AddressOf OnContextMenuFreezeClick))
        hexContextMenu.Items.Add(New ToolStripMenuItem("Unfreeze Value", Nothing, AddressOf OnContextMenuUnfreezeClick))
        hexContextMenu.Items.Add(New ToolStripSeparator())
        hexContextMenu.Items.Add(New ToolStripMenuItem("Show Info", Nothing, AddressOf OnContextMenuShowInfoClick))
    End Sub

    Private Sub RefreshProcessList()
        ComboBoxProcesses.Items.Clear()
        Dim allProcesses() As Process = Process.GetProcesses()
        Dim procList As List(Of String) = allProcesses.Where(Function(p) Not String.IsNullOrEmpty(p.ProcessName)).Select(Function(p) p.ProcessName & ".exe").Distinct().OrderBy(Function(name) name).ToList()

        Dim priorityEmulators As String() = {"psxfin.exe", "snes9x-x64.exe", "snes9x.exe"}
        For Each emu As String In priorityEmulators
            If procList.Contains(emu, StringComparer.OrdinalIgnoreCase) Then
                procList.RemoveAll(Function(x) x.Equals(emu, StringComparer.OrdinalIgnoreCase))
                ComboBoxProcesses.Items.Add(emu)
            End If
        Next
        For Each procName As String In procList
            ComboBoxProcesses.Items.Add(procName)
        Next

        ComboBoxProcesses.SelectedIndex = If(ComboBoxProcesses.Items.Count > 0, 0, -1)
    End Sub

    Private Sub ButtonRefreshProcs_Click(sender As Object, e As EventArgs) Handles ButtonRefreshProcs.Click
        RefreshProcessList()
    End Sub

    Private Sub InitializeRichTextBoxGrid(ramSize As Integer)
        TimerRefresh.Stop()
        RichTextBoxHex.Clear()

        Dim sb As New System.Text.StringBuilder()
        Dim totalRows As Integer = ramSize \ 16
        Dim initialRam As Byte() = ReadMemory(0, ramSize)

        For row As Integer = 0 To totalRows - 1
            Dim absAddr As UInteger = &H80000000UI + CUInt(row * 16)
            sb.Append($"{absAddr.ToString("X8")}  ")

            Dim asciiChars(15) As Char
            For c As Integer = 0 To 15
                Dim byteIdx As Integer = (row * 16) + c
                Dim currentByte As Byte = If(initialRam IsNot Nothing AndAlso byteIdx < initialRam.Length, initialRam(byteIdx), 0)
                sb.Append($"{currentByte.ToString("X2")} ")
                asciiChars(c) = If(currentByte >= 32 AndAlso currentByte <= 126, Chr(currentByte), "."c)
            Next
            sb.AppendLine($"  {New String(asciiChars)}")
        Next

        RichTextBoxHex.Text = sb.ToString()
        RichTextBoxHex.Modified = False
        If Not isPsxMode Then TimerRefresh.Start()
    End Sub

    Private Sub ButtonAttach_Click(sender As Object, e As EventArgs) Handles ButtonAttach.Click
        If ComboBoxProcesses.SelectedItem Is Nothing Then
            MessageBox.Show("Please select an .exe process from the list!", "Selection Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        TimerRefresh.Stop()
        Dim cleanProcName As String = ComboBoxProcesses.SelectedItem.ToString().ToLower().Replace(".exe", "")

        If cleanProcName.Contains("psxfin") Then
            isPsxMode = True
            searcher.IsPsxMode = True

            If psxMem.Attach() Then
                InitializeRichTextBoxGrid(2097152)
                MessageBox.Show($"Attached to pSX v1.13! RAM Base locked at: 0x{psxMem.RamBaseAddress:X8}", "Connected", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Else
                MessageBox.Show("Could not attach to psxfin.exe!", "Attach Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If

        Else
            isPsxMode = False
            searcher.IsPsxMode = False

            If snesMem.Attach(cleanProcName) Then
                If cleanProcName.Contains("snes9x") Then
                    snesMem.WramBaseAddress = &H140B78000UL
                Else
                    snesMem.WramBaseAddress = 0
                End If
                InitializeRichTextBoxGrid(131072)
                MessageBox.Show("Attached to SNES process successfully!", "Connected", MessageBoxButtons.OK, MessageBoxIcon.Information)
                originalRamSnapshot = ReadMemory(0, GetRamSize())
            Else
                MessageBox.Show($"Could not attach to process '{cleanProcName}'. Ensure it is running!", "Attach Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
        End If
    End Sub

    Private Sub RichTextBoxHex_MouseDown(sender As Object, e As MouseEventArgs) Handles RichTextBoxHex.MouseDown
        If e.Button = MouseButtons.Right Then
            hexContextMenu.Show(RichTextBoxHex, e.Location)
        End If
    End Sub

    ' --- ROBUST COPY / PLAIN TEXT PASTE & KEY LOCKS ---
    Private Sub ExecuteCopy()
        Dim selStart As Integer = RichTextBoxHex.SelectionStart
        Dim selLength As Integer = RichTextBoxHex.SelectionLength
        If selLength > 0 Then
            Dim startOffset As Integer = GetMemoryOffsetFromCharIndex(selStart)
            Dim endOffset As Integer = GetMemoryOffsetFromCharIndex(selStart + selLength)
            If startOffset >= 0 AndAlso endOffset >= startOffset Then
                Dim byteCount As Integer = (endOffset - startOffset) + 1
                Dim bytesToCopy As Byte() = ReadMemory(startOffset, byteCount)
                If bytesToCopy IsNot Nothing AndAlso bytesToCopy.Length > 0 Then
                    Dim hexString As String = BitConverter.ToString(bytesToCopy).Replace("-", " ")
                    Clipboard.Clear()
                    Clipboard.SetText(hexString, TextDataFormat.Text)
                End If
            End If
        End If
    End Sub

    Private Sub ExecutePaste()
        If Clipboard.ContainsText() Then
            Dim clipText As String = Clipboard.GetText().Trim()
            Dim cleanClipHex As String = New String(clipText.Where(Function(ch) "0123456789ABCDEFabcdef".Contains(ch)).ToArray())
            If cleanClipHex.Length Mod 2 <> 0 Then cleanClipHex = "0" & cleanClipHex

            Dim clipBytes As New List(Of Byte)()
            For idx As Integer = 0 To cleanClipHex.Length - 1 Step 2
                Dim b As Byte
                If Byte.TryParse(cleanClipHex.Substring(idx, 2), System.Globalization.NumberStyles.HexNumber, Nothing, b) Then
                    clipBytes.Add(b)
                End If
            Next

            If clipBytes.Count > 0 Then
                Dim selStart As Integer = RichTextBoxHex.SelectionStart
                Dim targetOffset As Integer = GetMemoryOffsetFromCharIndex(selStart)

                If targetOffset < 0 Then
                    Dim lineIdx As Integer = RichTextBoxHex.GetLineFromCharIndex(selStart)
                    targetOffset = lineIdx * 16
                End If

                Dim selLength As Integer = RichTextBoxHex.SelectionLength
                Dim allowedCount As Integer = clipBytes.Count

                If selLength > 0 Then
                    Dim selEndOffset As Integer = GetMemoryOffsetFromCharIndex(selStart + selLength)
                    If selEndOffset < targetOffset Then selEndOffset = targetOffset + (selLength \ 3)
                    Dim selectionByteSpan As Integer = If(selEndOffset >= targetOffset, selEndOffset - targetOffset + 1, clipBytes.Count)

                    If clipBytes.Count > selectionByteSpan Then
                        Dim result As DialogResult = MessageBox.Show("Are you sure you want to replace the current selection?", "Warring", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                        If result = DialogResult.No Then
                            Return
                        End If
                        allowedCount = selectionByteSpan
                    End If
                End If

                For bIdx As Integer = 0 To allowedCount - 1
                    Dim writeAddr As Integer = targetOffset + bIdx
                    If writeAddr < GetRamSize() Then
                        WriteMemoryByte(writeAddr, clipBytes(bIdx))
                    End If
                Next
            End If
        End If
    End Sub

    Private Sub OnContextMenuCopyClick(sender As Object, e As EventArgs)
        ExecuteCopy()
    End Sub

    Private Sub OnContextMenuPasteClick(sender As Object, e As EventArgs)
        ExecutePaste()
    End Sub

    Private Sub RichTextBoxHex_KeyDown(sender As Object, e As KeyEventArgs) Handles RichTextBoxHex.KeyDown
        If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Return OrElse
           e.KeyCode = Keys.Back OrElse e.KeyCode = Keys.Delete OrElse
           e.KeyCode = Keys.Insert OrElse
           (e.Control AndAlso e.KeyCode = Keys.X) Then
            e.SuppressKeyPress = True
            e.Handled = True
            Return
        End If

        If e.Control AndAlso e.KeyCode = Keys.C Then
            e.SuppressKeyPress = True
            e.Handled = True
            ExecuteCopy()
            Return
        End If

        If e.Control AndAlso e.KeyCode = Keys.V Then
            e.SuppressKeyPress = True
            e.Handled = True
            ExecutePaste()
            Return
        End If
    End Sub

    Private Function GetMemoryOffsetFromCharIndex(charIndex As Integer) As Integer
        Try
            Dim lineIdx As Integer = RichTextBoxHex.GetLineFromCharIndex(charIndex)
            If lineIdx < 0 OrElse lineIdx >= RichTextBoxHex.Lines.Length Then Return -1

            Dim lineStartIndex As Integer = RichTextBoxHex.GetFirstCharIndexFromLine(lineIdx)
            Dim colCharIndex As Integer = charIndex - lineStartIndex

            If colCharIndex >= 8 AndAlso colCharIndex < 58 Then
                Dim adjustedCol As Integer = Math.Max(0, colCharIndex - 10)
                Dim byteOffsetInLine As Integer = adjustedCol \ 3
                If byteOffsetInLine >= 0 AndAlso byteOffsetInLine <= 15 Then
                    Return (lineIdx * 16) + byteOffsetInLine
                End If
            End If
        Catch
        End Try
        Return -1
    End Function

    Private Sub RichTextBoxHex_KeyPress(sender As Object, e As KeyPressEventArgs) Handles RichTextBoxHex.KeyPress
        If Char.IsControl(e.KeyChar) OrElse Asc(e.KeyChar) = 13 OrElse Asc(e.KeyChar) = 10 OrElse Asc(e.KeyChar) = 8 Then
            e.Handled = True
            Return
        End If

        Dim charUpper As Char = Char.ToUpper(e.KeyChar)
        If "0123456789ABCDEF".Contains(charUpper) Then
            Dim selStart As Integer = RichTextBoxHex.SelectionStart
            Dim lineIdx As Integer = RichTextBoxHex.GetLineFromCharIndex(selStart)
            Dim lineStartIndex As Integer = RichTextBoxHex.GetFirstCharIndexFromLine(lineIdx)
            Dim colCharIndex As Integer = selStart - lineStartIndex

            If colCharIndex >= 10 AndAlso colCharIndex < 58 Then
                Dim adjustedCol As Integer = colCharIndex - 10
                Dim byteOffsetInLine As Integer = adjustedCol \ 3
                Dim subPosition As Integer = adjustedCol Mod 3

                If subPosition < 2 AndAlso byteOffsetInLine >= 0 AndAlso byteOffsetInLine <= 15 Then
                    Dim memoryOffset As Integer = (lineIdx * 16) + byteOffsetInLine
                    Dim currentBytes As Byte() = ReadMemory(memoryOffset, 1)

                    If currentBytes IsNot Nothing Then
                        Dim currentVal As Byte = currentBytes(0)
                        Dim newByte As Byte = currentVal
                        Dim typedVal As Integer = Convert.ToInt32(charUpper.ToString(), 16)

                        If subPosition = 0 Then
                            Dim lowNibble As Integer = currentVal And &HF
                            newByte = CByte((typedVal << 4) Or lowNibble)
                        Else
                            Dim highNibble As Integer = currentVal >> 4
                            newByte = CByte((highNibble << 4) Or typedVal)
                        End If

                        WriteMemoryByte(memoryOffset, newByte)

                        Dim targetCharIndex As Integer = lineStartIndex + 10 + (byteOffsetInLine * 3) + subPosition
                        RichTextBoxHex.Select(targetCharIndex, 1)
                        RichTextBoxHex.SelectedText = charUpper.ToString()

                        Dim nextCursorPos As Integer = selStart + 1
                        If subPosition = 1 Then nextCursorPos += 1
                        RichTextBoxHex.SelectionStart = Math.Min(nextCursorPos, RichTextBoxHex.Text.Length)
                    End If
                End If
            End If
        End If

        e.Handled = True
    End Sub

    Private Function ParseHexOrTextQuery(cleanInput As String) As List(Of Byte)
        Dim searchBytes As New List(Of Byte)()
        Dim isPureHex As Boolean = System.Text.RegularExpressions.Regex.IsMatch(cleanInput, "^[0-9A-Fa-f\s]+$")

        If isPureHex AndAlso Not cleanInput.Contains(" ") AndAlso (cleanInput.Length Mod 2 = 0) Then
            For i As Integer = 0 To cleanInput.Length - 1 Step 2
                Dim b As Byte
                If Byte.TryParse(cleanInput.Substring(i, 2), System.Globalization.NumberStyles.HexNumber, Nothing, b) Then searchBytes.Add(b)
            Next
        ElseIf cleanInput.Contains(" ") Then
            Dim parts As String() = cleanInput.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            For Each p As String In parts
                Dim b As Byte
                If Byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, Nothing, b) Then searchBytes.Add(b)
            Next
        Else
            searchBytes.AddRange(System.Text.Encoding.ASCII.GetBytes(cleanInput))
        End If
        Return searchBytes
    End Function

    Private Sub OnContextMenuFindClick(sender As Object, e As EventArgs)
        Dim inputQuery As String = InputBox("Enter Find pattern (Hex bytes like FFFF or text):", "Find in RAM", lastSearchQuery)
        If String.IsNullOrWhiteSpace(inputQuery) Then Return

        lastSearchQuery = inputQuery.Trim()
        lastSearchIndex = 0
        FindNextInRAM()
    End Sub

    Private Sub FindNextInRAM()
        If String.IsNullOrWhiteSpace(lastSearchQuery) Then Return
        Dim ramBuffer As Byte() = ReadMemory(0, GetRamSize())
        If ramBuffer Is Nothing OrElse ramBuffer.Length = 0 Then Return

        Dim searchBytes As List(Of Byte) = ParseHexOrTextQuery(lastSearchQuery)
        If searchBytes.Count = 0 Then Return

        Dim foundOffset As Integer = -1
        Dim startIndex As Integer = Math.Min(lastSearchIndex + 1, ramBuffer.Length - searchBytes.Count)

        For pass As Integer = 0 To 1
            For i As Integer = startIndex To ramBuffer.Length - searchBytes.Count
                Dim matched As Boolean = True
                For j As Integer = 0 To searchBytes.Count - 1
                    If ramBuffer(i + j) <> searchBytes(j) Then
                        matched = False
                        Exit For
                    End If
                Next
                If matched Then
                    foundOffset = i
                    Exit For
                End If
            Next
            If foundOffset >= 0 Then Exit For
            startIndex = 0
        Next

        If foundOffset >= 0 Then
            lastSearchIndex = foundOffset
            Dim targetRow As Integer = foundOffset \ 16
            Dim charIndex As Integer = RichTextBoxHex.GetFirstCharIndexFromLine(targetRow)
            If charIndex >= 0 Then
                RichTextBoxHex.Select(charIndex, 48)
                RichTextBoxHex.ScrollToCaret()
            End If
        Else
            MessageBox.Show("Pattern not found.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    Private Sub OnContextMenuReplaceClick(sender As Object, e As EventArgs)
        Dim findQuery As String = InputBox("Enter Find pattern:", "Replace - Find What", lastSearchQuery)
        If String.IsNullOrWhiteSpace(findQuery) Then Return

        Dim replaceQuery As String = InputBox("Enter Replace pattern:", "Replace With", "")
        If replaceQuery Is Nothing Then Return

        Dim findBytes As List(Of Byte) = ParseHexOrTextQuery(findQuery.Trim())
        Dim replaceBytes As List(Of Byte) = ParseHexOrTextQuery(replaceQuery.Trim())
        If findBytes.Count = 0 OrElse replaceBytes.Count = 0 OrElse findBytes.Count <> replaceBytes.Count Then Return

        Dim ramBuffer As Byte() = ReadMemory(0, GetRamSize())
        If ramBuffer Is Nothing OrElse ramBuffer.Length = 0 Then Return

        Dim foundOffset As Integer = -1
        For i As Integer = 0 To ramBuffer.Length - findBytes.Count
            Dim matched As Boolean = True
            For j As Integer = 0 To findBytes.Count - 1
                If ramBuffer(i + j) <> findBytes(j) Then
                    matched = False
                    Exit For
                End If
            Next
            If matched Then
                foundOffset = i
                Exit For
            End If
        Next

        If foundOffset >= 0 Then
            For j As Integer = 0 To replaceBytes.Count - 1
                WriteMemoryByte(foundOffset + j, replaceBytes(j))
            Next
            MessageBox.Show("Successfully replaced!", "Replace Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            MessageBox.Show("Pattern not found.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    Private Sub OnContextMenuReplaceAllClick(sender As Object, e As EventArgs)
        Dim findQuery As String = InputBox("Enter Find pattern:", "Replace All - Find What", lastSearchQuery)
        If String.IsNullOrWhiteSpace(findQuery) Then Return

        Dim replaceQuery As String = InputBox("Enter Replace pattern:", "Replace All With", "")
        If replaceQuery Is Nothing Then Return

        Dim findBytes As List(Of Byte) = ParseHexOrTextQuery(findQuery.Trim())
        Dim replaceBytes As List(Of Byte) = ParseHexOrTextQuery(replaceQuery.Trim())
        If findBytes.Count = 0 OrElse replaceBytes.Count = 0 OrElse findBytes.Count <> replaceBytes.Count Then Return

        Dim ramBuffer As Byte() = ReadMemory(0, GetRamSize())
        If ramBuffer Is Nothing OrElse ramBuffer.Length = 0 Then Return

        Dim replaceCount As Integer = 0
        Dim i As Integer = 0
        While i <= ramBuffer.Length - findBytes.Count
            Dim matched As Boolean = True
            For j As Integer = 0 To findBytes.Count - 1
                If ramBuffer(i + j) <> findBytes(j) Then
                    matched = False
                    Exit For
                End If
            Next
            If matched Then
                For j As Integer = 0 To replaceBytes.Count - 1
                    WriteMemoryByte(i + j, replaceBytes(j))
                    ramBuffer(i + j) = replaceBytes(j)
                Next
                replaceCount += 1
                i += findBytes.Count
            Else
                i += 1
            End If
        End While
        MessageBox.Show($"Replaced {replaceCount} occurrence(s) in RAM.", "Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub OnContextMenuUndoAllClick(sender As Object, e As EventArgs)
        If originalRamSnapshot Is Nothing OrElse originalRamSnapshot.Length = 0 Then Return
        If MessageBox.Show("Restore entire RAM state?", "Confirm Undo All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
            For i As Integer = 0 To originalRamSnapshot.Length - 1
                WriteMemoryByte(i, originalRamSnapshot(i))
            Next
            MessageBox.Show("RAM successfully restored!", "Undo All Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    Private Sub OnContextMenuSearchValueClick(sender As Object, e As EventArgs)
        Dim inputValStr As String = InputBox("Enter value to scan in live RAM:", "Search RAM Value", "0")
        If String.IsNullOrWhiteSpace(inputValStr) Then Return

        Dim searchVal As UInteger = 0
        Dim tempLong As Long = 0
        If inputValStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) OrElse inputValStr.StartsWith("$") Then
            Long.TryParse(inputValStr.Replace("0x", "").Replace("0X", "").Replace("$", ""), System.Globalization.NumberStyles.HexNumber, Nothing, tempLong)
        Else
            Long.TryParse(inputValStr, System.Globalization.NumberStyles.Integer, Nothing, tempLong)
        End If

        searchVal = CType(tempLong, UInteger)
        TextBoxSearchValue.Text = searchVal.ToString()
        Dim count As Integer = searcher.FirstScan(searchVal, GetSelectedDataSize())
        UpdateSearchResultsList()
        LabelSearchCount.Text = $"RAM Scan Found: {count} addresses"
    End Sub

    Private Sub OnContextMenuFreezeClick(sender As Object, e As EventArgs)
        Dim charPos As Integer = RichTextBoxHex.SelectionStart
        Dim lineIdx As Integer = RichTextBoxHex.GetLineFromCharIndex(charPos)
        Dim colOffset As Integer = (charPos - RichTextBoxHex.GetFirstCharIndexFromLine(lineIdx) - 10) \ 3

        If colOffset >= 0 AndAlso colOffset <= 15 Then
            Dim memoryOffset As Integer = (lineIdx * 16) + colOffset
            Dim rawBytes As Byte() = ReadMemory(memoryOffset, 1)
            If rawBytes IsNot Nothing Then
                searcher.FrozenAddresses(memoryOffset) = rawBytes(0)
                MessageBox.Show($"Frozen address offset: 0x{memoryOffset:X4}", "Value Frozen", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
        End If
    End Sub

    Private Sub OnContextMenuUnfreezeClick(sender As Object, e As EventArgs)
        Dim charPos As Integer = RichTextBoxHex.SelectionStart
        Dim lineIdx As Integer = RichTextBoxHex.GetLineFromCharIndex(charPos)
        Dim colOffset As Integer = (charPos - RichTextBoxHex.GetFirstCharIndexFromLine(lineIdx) - 10) \ 3

        If colOffset >= 0 AndAlso colOffset <= 15 Then
            Dim memoryOffset As Integer = (lineIdx * 16) + colOffset
            If searcher.IsFrozen(memoryOffset) Then
                searcher.FrozenAddresses.Remove(memoryOffset)
                MessageBox.Show($"Unfrozen address offset: 0x{memoryOffset:X4}", "Value Unfrozen", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
        End If
    End Sub

    Private Sub OnContextMenuShowInfoClick(sender As Object, e As EventArgs)
        MessageBox.Show("RichTextBox mode active. Use Find (F1) or Go To (Ctrl+G) to navigate addresses.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub ButtonFirstScan_Click(sender As Object, e As EventArgs) Handles ButtonFirstScan.Click
        Dim inputStr As String = TextBoxSearchValue.Text.Trim()
        Dim dataSize As Integer = GetSelectedDataSize()

        If String.IsNullOrWhiteSpace(inputStr) Then
            Dim unkCount As Integer = searcher.FirstScanUnknown()
            If unkCount <> -1 Then
                UpdateSearchResultsList()
                LabelSearchCount.Text = $"Snapshot Created: {unkCount} addresses"
            End If
            Return
        End If

        Dim tempLong As Long = 0
        If inputStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) OrElse inputStr.StartsWith("$") Then
            Long.TryParse(inputStr.Replace("0x", "").Replace("0X", "").Replace("$", ""), System.Globalization.NumberStyles.HexNumber, Nothing, tempLong)
        Else
            Long.TryParse(inputStr, System.Globalization.NumberStyles.Integer, Nothing, tempLong)
        End If

        Dim count As Integer = searcher.FirstScan(CType(tempLong, UInteger), dataSize)
        UpdateSearchResultsList()
        LabelSearchCount.Text = $"RAM Scan Found: {count} addresses"
    End Sub

    Private Sub ButtonUnknownValue_Click(sender As Object, e As EventArgs) Handles ButtonUnknownValue.Click
        Dim unkCount As Integer = searcher.FirstScanUnknown()
        If unkCount <> -1 Then
            UpdateSearchResultsList()
            LabelSearchCount.Text = $"Snapshot Created: {unkCount} addresses"
        End If
    End Sub

    Private Sub ButtonNextScan_Click(sender As Object, e As EventArgs) Handles ButtonNextScan.Click
        Dim inputStr As String = TextBoxSearchValue.Text.Trim()
        If String.IsNullOrWhiteSpace(inputStr) Then Return
        Dim dataSize As Integer = GetSelectedDataSize()

        Dim tempLong As Long = 0
        If inputStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) OrElse inputStr.StartsWith("$") Then
            Long.TryParse(inputStr.Replace("0x", "").Replace("0X", "").Replace("$", ""), System.Globalization.NumberStyles.HexNumber, Nothing, tempLong)
        Else
            Long.TryParse(inputStr, System.Globalization.NumberStyles.Integer, Nothing, tempLong)
        End If

        Dim count As Integer = searcher.NextScanExact(CType(tempLong, UInteger), dataSize)
        UpdateSearchResultsList()
        LabelSearchCount.Text = $"Filtered (Exact): {count} addresses"
    End Sub

    Private Sub ButtonGreaterThan_Click(sender As Object, e As EventArgs) Handles ButtonGreaterThan.Click
        Dim count As Integer = searcher.NextScanGreaterThan(GetSelectedDataSize())
        UpdateSearchResultsList()
        LabelSearchCount.Text = $"Filtered (Greater): {count} addresses"
    End Sub

    Private Sub ButtonLessThan_Click(sender As Object, e As EventArgs) Handles ButtonLessThan.Click
        Dim count As Integer = searcher.NextScanLessThanOrEqual(GetSelectedDataSize())
        UpdateSearchResultsList()
        LabelSearchCount.Text = $"Filtered (Less/Equal): {count} addresses"
    End Sub

    Private Sub UpdateSearchResultsList()
        ListBoxSearchResults.Items.Clear()
        ListBoxSearchResults.BeginUpdate()
        ListBoxSearchResults.Items.Add(String.Format("{0,-12} {1,-10} {2,-10}", "Address", "HEX", "DEC"))
        ListBoxSearchResults.Items.Add(New String("-"c, 32))

        If searcher IsNot Nothing AndAlso searcher.MatchingResults IsNot Nothing Then
            Dim displayLimit As Integer = Math.Min(searcher.MatchingResults.Count, 500)
            Dim dataSize As Integer = GetSelectedDataSize()

            For i As Integer = 0 To displayLimit - 1
                Dim offset As Integer = searcher.MatchingResults(i).Offset
                Dim absAddr As UInteger = If(isPsxMode, &H80000000UI + CUInt(offset), CUInt(offset))
                Dim hexText As String = "??"
                Dim decText As String = "??"
                Dim rawBytes As Byte() = ReadMemory(offset, dataSize)

                If rawBytes IsNot Nothing AndAlso rawBytes.Length >= dataSize Then
                    Select Case dataSize
                        Case 1
                            hexText = rawBytes(0).ToString("X2")
                            decText = rawBytes(0).ToString()
                        Case 2
                            Dim val16 As UShort = BitConverter.ToUInt16(rawBytes, 0)
                            hexText = val16.ToString("X4")
                            decText = val16.ToString()
                        Case 3
                            Dim val24 As UInteger = CType(rawBytes(0), UInteger) Or (CType(rawBytes(1), UInteger) << 8) Or (CType(rawBytes(2), UInteger) << 16)
                            hexText = val24.ToString("X6")
                            decText = val24.ToString()
                        Case 4
                            Dim val32 As UInteger = BitConverter.ToUInt32(rawBytes, 0)
                            hexText = val32.ToString("X8")
                            decText = val32.ToString()
                    End Select
                End If

                ListBoxSearchResults.Items.Add(String.Format("{0,-12} {1,-10} {2,-10}", absAddr.ToString("X8"), hexText, decText))
            Next
        End If
        ListBoxSearchResults.EndUpdate()
    End Sub

    Private Sub ListBoxSearchResults_DoubleClick(sender As Object, e As EventArgs) Handles ListBoxSearchResults.DoubleClick
        If ListBoxSearchResults.SelectedIndex < 0 Then Return
        Dim selectedItemStr As String = ListBoxSearchResults.SelectedItem.ToString()
        If selectedItemStr.Contains("Address") OrElse selectedItemStr.Contains("---") Then Return

        Dim addrStr As String = selectedItemStr.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)(0).Trim()
        Dim offset As Integer = CInt(Convert.ToUInt32(addrStr, 16) - If(isPsxMode, &H80000000UI, 0))
        Dim targetRow As Integer = offset \ 16

        Dim charIndex As Integer = RichTextBoxHex.GetFirstCharIndexFromLine(targetRow)
        If charIndex >= 0 Then
            RichTextBoxHex.Select(charIndex, 48)
            RichTextBoxHex.ScrollToCaret()
        End If
    End Sub

    Private Sub OnDataSizeChanged(sender As Object, e As EventArgs)
        Dim rb As RadioButton = TryCast(sender, RadioButton)
        If rb IsNot Nothing AndAlso rb.Checked AndAlso searcher?.MatchingResults?.Count > 0 Then
            UpdateSearchResultsList()
        End If
    End Sub

    Private Sub TimerRefresh_Tick(sender As Object, e As EventArgs) Handles TimerRefresh.Tick
        If isPsxMode OrElse RichTextBoxHex.Lines.Length = 0 Then Return
        searcher.ApplyFreezes()
    End Sub
End Class