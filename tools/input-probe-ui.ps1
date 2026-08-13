Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$nativePath = Join-Path $PSScriptRoot 'input-probe.ps1'

$form = New-Object Windows.Forms.Form
$form.Text = '枫叶视觉助手 · SendInput 单键测试'
$form.Size = New-Object Drawing.Size(460, 280)
$form.StartPosition = 'CenterScreen'
$form.BackColor = [Drawing.Color]::FromArgb(24, 35, 33)
$form.ForeColor = [Drawing.Color]::White

$titleLabel = New-Object Windows.Forms.Label
$titleLabel.Text = '目标游戏窗口标题片段（必填）'
$titleLabel.Location = New-Object Drawing.Point(18, 18)
$titleLabel.AutoSize = $true
$form.Controls.Add($titleLabel)

$titleBox = New-Object Windows.Forms.TextBox
$titleBox.Location = New-Object Drawing.Point(18, 42)
$titleBox.Size = New-Object Drawing.Size(405, 24)
$titleBox.BackColor = [Drawing.Color]::FromArgb(14, 24, 22)
$titleBox.ForeColor = [Drawing.Color]::White
$form.Controls.Add($titleBox)

$arm = New-Object Windows.Forms.CheckBox
$arm.Text = '我确认这是授权测试，允许发送单次按键'
$arm.Location = New-Object Drawing.Point(18, 80)
$arm.AutoSize = $true
$form.Controls.Add($arm)

$status = New-Object Windows.Forms.Label
$status.Text = '未发送。程序不会后台注入，也不会连续按键。'
$status.Location = New-Object Drawing.Point(18, 112)
$status.Size = New-Object Drawing.Size(405, 38)
$status.ForeColor = [Drawing.Color]::FromArgb(150, 220, 200)
$form.Controls.Add($status)

function Add-KeyButton([string]$Text, [int]$X, [int]$Y, [string]$Key) {
  $button = New-Object Windows.Forms.Button
  $button.Text = $Text
  $button.Tag = $Key
  $button.Location = New-Object Drawing.Point($X, $Y)
  $button.Size = New-Object Drawing.Size(92, 34)
  $button.BackColor = [Drawing.Color]::FromArgb(31, 94, 79)
  $button.ForeColor = [Drawing.Color]::White
  $button.FlatStyle = 'Flat'
  $button.Add_Click({
    if (-not $arm.Checked) { $status.Text = '已拒绝：请先勾选授权测试。'; return }
    if ([string]::IsNullOrWhiteSpace($titleBox.Text)) { $status.Text = '已拒绝：请填写窗口标题片段。'; return }
    try {
      $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $nativePath -Armed -Key $this.Tag -WindowTitle $titleBox.Text 2>&1
      $status.Text = ($output -join ' ')
    } catch {
      $status.Text = "发送失败：$($_.Exception.Message)"
    }
  })
  $form.Controls.Add($button)
}

Add-KeyButton '发送 J' 18 164 'J'
Add-KeyButton '发送 A' 120 164 'A'
Add-KeyButton '发送 D' 222 164 'D'
Add-KeyButton '发送 空格' 324 164 'SPACE'

$close = New-Object Windows.Forms.Button
$close.Text = '关闭'
$close.Location = New-Object Drawing.Point(18, 210)
$close.Size = New-Object Drawing.Size(398, 30)
$close.Add_Click({ $form.Close() })
$form.Controls.Add($close)

[Windows.Forms.Application]::Run($form)
