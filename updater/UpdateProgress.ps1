function Initialize-UpdateProgress([bool]$Arabic) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [Windows.Forms.Application]::EnableVisualStyles()
    $script:UpdateArabic = $Arabic
    $script:UpdateCancel = $false
    $script:UpdateForm = [Windows.Forms.Form]::new()
    $script:UpdateForm.Text = 'Wavebreak'
    $script:UpdateForm.Size = [Drawing.Size]::new(500,220)
    $script:UpdateForm.StartPosition = 'CenterScreen'
    $script:UpdateForm.FormBorderStyle = 'FixedDialog'
    $script:UpdateForm.MaximizeBox = $false
    $script:UpdateForm.BackColor = [Drawing.Color]::FromArgb(20,25,38)
    $script:UpdateForm.ForeColor = [Drawing.Color]::White
    if ($Arabic) { $script:UpdateForm.RightToLeft = 'Yes'; $script:UpdateForm.RightToLeftLayout = $true }
    $label = [Windows.Forms.Label]::new()
    $label.Text = if ($Arabic) { 'جارٍ تثبيت تحديث Wavebreak…' } else { 'Installing the Wavebreak update…' }
    $label.Location = [Drawing.Point]::new(24,20); $label.Size = [Drawing.Size]::new(440,35)
    $script:UpdateForm.Controls.Add($label)
    $script:UpdateBar = [Windows.Forms.ProgressBar]::new()
    $script:UpdateBar.Location = [Drawing.Point]::new(24,65); $script:UpdateBar.Size = [Drawing.Size]::new(440,24)
    $script:UpdateForm.Controls.Add($script:UpdateBar)
    $cancel = [Windows.Forms.Button]::new()
    $cancel.Text = if ($Arabic) { 'إلغاء' } else { 'Cancel' }
    $cancel.Location = [Drawing.Point]::new(340,110); $cancel.Size = [Drawing.Size]::new(124,32)
    $cancel.Add_Click({ $script:UpdateCancel = $true })
    $script:UpdateForm.Controls.Add($cancel)
    $script:UpdateForm.Add_FormClosing({ param($sender,$event) if (-not $script:UpdateFinished) { $script:UpdateCancel = $true; $event.Cancel = $true } })
}
function Show-UpdateProgress { $script:UpdateForm.Show(); [Windows.Forms.Application]::DoEvents() }
function Advance-UpdateProgress([int]$Percent) { $script:UpdateBar.Value = [Math]::Max(0,[Math]::Min(100,$Percent)); [Windows.Forms.Application]::DoEvents() }
function Test-UpdateCancelled { [Windows.Forms.Application]::DoEvents(); return $script:UpdateCancel }
function Complete-UpdateProgress([bool]$Success, [string]$Detail, [string]$Backup) {
    $script:UpdateFinished = $true
    if (-not $Success) {
        $text = if ($script:UpdateArabic) { 'لم يكتمل التحديث. احتُفظ بالنسخة السابقة للاستعادة.' } else { 'The update did not complete. The previous version was kept for recovery.' }
        if ($Backup) { $text += "`r`n" + $Backup }
        if ($Detail) { $text += "`r`n" + $Detail }
        [Windows.Forms.MessageBox]::Show($text, 'Wavebreak') | Out-Null
    }
    $script:UpdateForm.Close(); $script:UpdateForm.Dispose()
}
