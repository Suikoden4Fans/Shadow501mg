<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        components = New ComponentModel.Container()
        DataGridViewHex = New DataGridView()
        ComboBoxProcesses = New ComboBox()
        ButtonRefreshProcs = New Button()
        ButtonAttach = New Button()
        TimerRefresh = New Timer(components)
        TextBoxSearchValue = New TextBox()
        ButtonFirstScan = New Button()
        ButtonNextScan = New Button()
        ListBoxSearchResults = New ListBox()
        LabelSearchCount = New Label()
        ButtonGreaterThan = New Button()
        ButtonLessThan = New Button()
        ButtonUnknownValue = New Button()
        LabelByteInfo = New Label()
        GroupBoxDataSize = New GroupBox()
        RadioButton4 = New RadioButton()
        RadioButton3 = New RadioButton()
        RadioButton2 = New RadioButton()
        RadioButton1 = New RadioButton()
        RichTextBoxHex = New RichTextBox()
        CType(DataGridViewHex, ComponentModel.ISupportInitialize).BeginInit()
        GroupBoxDataSize.SuspendLayout()
        SuspendLayout()
        ' 
        ' DataGridViewHex
        ' 
        DataGridViewHex.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        DataGridViewHex.Location = New Point(1057, 288)
        DataGridViewHex.Name = "DataGridViewHex"
        DataGridViewHex.RowHeadersWidth = 51
        DataGridViewHex.Size = New Size(85, 67)
        DataGridViewHex.TabIndex = 0
        ' 
        ' ComboBoxProcesses
        ' 
        ComboBoxProcesses.FormattingEnabled = True
        ComboBoxProcesses.Location = New Point(12, 47)
        ComboBoxProcesses.Name = "ComboBoxProcesses"
        ComboBoxProcesses.Size = New Size(151, 28)
        ComboBoxProcesses.TabIndex = 1
        ' 
        ' ButtonRefreshProcs
        ' 
        ButtonRefreshProcs.Location = New Point(12, 12)
        ButtonRefreshProcs.Name = "ButtonRefreshProcs"
        ButtonRefreshProcs.Size = New Size(151, 29)
        ButtonRefreshProcs.TabIndex = 2
        ButtonRefreshProcs.Text = "Refresh"
        ButtonRefreshProcs.UseVisualStyleBackColor = True
        ' 
        ' ButtonAttach
        ' 
        ButtonAttach.Location = New Point(169, 12)
        ButtonAttach.Name = "ButtonAttach"
        ButtonAttach.Size = New Size(151, 29)
        ButtonAttach.TabIndex = 4
        ButtonAttach.Text = "Attach"
        ButtonAttach.UseVisualStyleBackColor = True
        ' 
        ' TimerRefresh
        ' 
        TimerRefresh.Interval = 200
        ' 
        ' TextBoxSearchValue
        ' 
        TextBoxSearchValue.Location = New Point(637, 13)
        TextBoxSearchValue.Name = "TextBoxSearchValue"
        TextBoxSearchValue.Size = New Size(126, 27)
        TextBoxSearchValue.TabIndex = 5
        ' 
        ' ButtonFirstScan
        ' 
        ButtonFirstScan.Location = New Point(326, 12)
        ButtonFirstScan.Name = "ButtonFirstScan"
        ButtonFirstScan.Size = New Size(130, 29)
        ButtonFirstScan.TabIndex = 6
        ButtonFirstScan.Text = "First Scan"
        ButtonFirstScan.UseVisualStyleBackColor = True
        ' 
        ' ButtonNextScan
        ' 
        ButtonNextScan.Location = New Point(462, 12)
        ButtonNextScan.Name = "ButtonNextScan"
        ButtonNextScan.Size = New Size(152, 29)
        ButtonNextScan.TabIndex = 7
        ButtonNextScan.Text = "Next Scan"
        ButtonNextScan.UseVisualStyleBackColor = True
        ' 
        ' ListBoxSearchResults
        ' 
        ListBoxSearchResults.FormattingEnabled = True
        ListBoxSearchResults.Location = New Point(819, 186)
        ListBoxSearchResults.Name = "ListBoxSearchResults"
        ListBoxSearchResults.Size = New Size(238, 424)
        ListBoxSearchResults.TabIndex = 8
        ' 
        ' LabelSearchCount
        ' 
        LabelSearchCount.AutoSize = True
        LabelSearchCount.Location = New Point(813, 163)
        LabelSearchCount.Name = "LabelSearchCount"
        LabelSearchCount.Size = New Size(100, 20)
        LabelSearchCount.TabIndex = 9
        LabelSearchCount.Text = " Search Count"
        ' 
        ' ButtonGreaterThan
        ' 
        ButtonGreaterThan.Location = New Point(326, 47)
        ButtonGreaterThan.Name = "ButtonGreaterThan"
        ButtonGreaterThan.Size = New Size(130, 29)
        ButtonGreaterThan.TabIndex = 10
        ButtonGreaterThan.Text = "Greater Than"
        ButtonGreaterThan.UseVisualStyleBackColor = True
        ' 
        ' ButtonLessThan
        ' 
        ButtonLessThan.Location = New Point(462, 47)
        ButtonLessThan.Name = "ButtonLessThan"
        ButtonLessThan.Size = New Size(152, 29)
        ButtonLessThan.TabIndex = 11
        ButtonLessThan.Text = "Less Than"
        ButtonLessThan.UseVisualStyleBackColor = True
        ' 
        ' ButtonUnknownValue
        ' 
        ButtonUnknownValue.Location = New Point(169, 46)
        ButtonUnknownValue.Name = "ButtonUnknownValue"
        ButtonUnknownValue.Size = New Size(151, 29)
        ButtonUnknownValue.TabIndex = 12
        ButtonUnknownValue.Text = "Unknown Value"
        ButtonUnknownValue.UseVisualStyleBackColor = True
        ' 
        ' LabelByteInfo
        ' 
        LabelByteInfo.AutoSize = True
        LabelByteInfo.Location = New Point(12, 163)
        LabelByteInfo.Name = "LabelByteInfo"
        LabelByteInfo.Size = New Size(85, 20)
        LabelByteInfo.TabIndex = 13
        LabelByteInfo.Text = "RAM Editor"
        ' 
        ' GroupBoxDataSize
        ' 
        GroupBoxDataSize.Controls.Add(RadioButton4)
        GroupBoxDataSize.Controls.Add(RadioButton3)
        GroupBoxDataSize.Controls.Add(RadioButton2)
        GroupBoxDataSize.Controls.Add(RadioButton1)
        GroupBoxDataSize.Location = New Point(813, 8)
        GroupBoxDataSize.Name = "GroupBoxDataSize"
        GroupBoxDataSize.Size = New Size(108, 152)
        GroupBoxDataSize.TabIndex = 14
        GroupBoxDataSize.TabStop = False
        GroupBoxDataSize.Text = "Data Size"
        ' 
        ' RadioButton4
        ' 
        RadioButton4.AutoSize = True
        RadioButton4.Checked = True
        RadioButton4.Location = New Point(6, 116)
        RadioButton4.Name = "RadioButton4"
        RadioButton4.Size = New Size(71, 24)
        RadioButton4.TabIndex = 3
        RadioButton4.TabStop = True
        RadioButton4.Text = "4 byte"
        RadioButton4.UseVisualStyleBackColor = True
        ' 
        ' RadioButton3
        ' 
        RadioButton3.AutoSize = True
        RadioButton3.Location = New Point(6, 86)
        RadioButton3.Name = "RadioButton3"
        RadioButton3.Size = New Size(71, 24)
        RadioButton3.TabIndex = 2
        RadioButton3.Text = "3 byte"
        RadioButton3.UseVisualStyleBackColor = True
        ' 
        ' RadioButton2
        ' 
        RadioButton2.AutoSize = True
        RadioButton2.Location = New Point(6, 56)
        RadioButton2.Name = "RadioButton2"
        RadioButton2.Size = New Size(71, 24)
        RadioButton2.TabIndex = 1
        RadioButton2.Text = "2 byte"
        RadioButton2.UseVisualStyleBackColor = True
        ' 
        ' RadioButton1
        ' 
        RadioButton1.AutoSize = True
        RadioButton1.Location = New Point(6, 26)
        RadioButton1.Name = "RadioButton1"
        RadioButton1.Size = New Size(71, 24)
        RadioButton1.TabIndex = 0
        RadioButton1.Text = "1 byte"
        RadioButton1.UseVisualStyleBackColor = True
        ' 
        ' RichTextBoxHex
        ' 
        RichTextBoxHex.Location = New Point(12, 186)
        RichTextBoxHex.Name = "RichTextBoxHex"
        RichTextBoxHex.Size = New Size(795, 424)
        RichTextBoxHex.TabIndex = 15
        RichTextBoxHex.Text = ""
        ' 
        ' Form1
        ' 
        AutoScaleDimensions = New SizeF(8F, 20F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1176, 658)
        Controls.Add(RichTextBoxHex)
        Controls.Add(GroupBoxDataSize)
        Controls.Add(LabelByteInfo)
        Controls.Add(ButtonUnknownValue)
        Controls.Add(ButtonLessThan)
        Controls.Add(ButtonGreaterThan)
        Controls.Add(LabelSearchCount)
        Controls.Add(ListBoxSearchResults)
        Controls.Add(ButtonNextScan)
        Controls.Add(ButtonFirstScan)
        Controls.Add(TextBoxSearchValue)
        Controls.Add(ButtonAttach)
        Controls.Add(ButtonRefreshProcs)
        Controls.Add(ComboBoxProcesses)
        Controls.Add(DataGridViewHex)
        Name = "Form1"
        Text = "Form1"
        CType(DataGridViewHex, ComponentModel.ISupportInitialize).EndInit()
        GroupBoxDataSize.ResumeLayout(False)
        GroupBoxDataSize.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents DataGridViewHex As DataGridView
    Friend WithEvents ComboBoxProcesses As ComboBox
    Friend WithEvents ButtonRefreshProcs As Button
    Friend WithEvents ButtonAttach As Button
    Friend WithEvents TimerRefresh As Timer
    Friend WithEvents TextBoxSearchValue As TextBox
    Friend WithEvents ButtonFirstScan As Button
    Friend WithEvents ButtonNextScan As Button
    Friend WithEvents ListBoxSearchResults As ListBox
    Friend WithEvents LabelSearchCount As Label
    Friend WithEvents ButtonGreaterThan As Button
    Friend WithEvents ButtonLessThan As Button
    Friend WithEvents ButtonUnknownValue As Button
    Friend WithEvents LabelByteInfo As Label
    Friend WithEvents GroupBoxDataSize As GroupBox
    Friend WithEvents RadioButton4 As RadioButton
    Friend WithEvents RadioButton3 As RadioButton
    Friend WithEvents RadioButton2 As RadioButton
    Friend WithEvents RadioButton1 As RadioButton
    Friend WithEvents RichTextBoxHex As RichTextBox

End Class
