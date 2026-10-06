<#
  Borderless topmost millisecond clock used as a visual latency reference by latency-probe.ps1.
  Closes itself after the given number of seconds.
#>
param([int]$Seconds = 30)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Name Dpi -Namespace Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][Native.Dpi]::SetProcessDPIAware()

$form = New-Object System.Windows.Forms.Form
$form.Text = 'latency clock'
$form.TopMost = $true
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point(40, 40)
$form.Size = New-Object System.Drawing.Size(900, 180)
$form.BackColor = [System.Drawing.Color]::Black

$label = New-Object System.Windows.Forms.Label
$label.Dock = 'Fill'
$label.ForeColor = [System.Drawing.Color]::Lime
$label.Font = New-Object System.Drawing.Font('Consolas', 72, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$label.TextAlign = 'MiddleCenter'
$form.Controls.Add($label)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1
$timer.Add_Tick({
    $label.Text = $sw.Elapsed.ToString('mm\:ss\.fff')
    if ($sw.Elapsed.TotalSeconds -gt $Seconds) { $form.Close() }
})
$timer.Start()
[void]$form.ShowDialog()
