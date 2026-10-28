param([string]$PublishDirectory = 'artifacts/publish/m2-win-x64', [int]$ExistingPid = 0)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class IslandSmokeNative {
 public delegate bool EnumProc(IntPtr hwnd,IntPtr state);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc fn,IntPtr state);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd,StringBuilder value,int max);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
 public static IntPtr Find(uint pid) {
  IntPtr result=IntPtr.Zero;
  EnumWindows((hwnd,state)=>{uint current;GetWindowThreadProcessId(hwnd,out current); var text=new StringBuilder(100);GetWindowText(hwnd,text,100);
   if(current==pid && text.ToString()=="IsLet-Todo" && IsWindowVisible(hwnd)){result=hwnd;return false;} return true;},IntPtr.Zero);
  return result;
 }
}
'@
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path (Join-Path $root $PublishDirectory) 'TaskIsland.exe'
$primary = $null
$secondary = $null
try {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $primary = if ($ExistingPid) { Get-Process -Id $ExistingPid } else { Start-Process -FilePath $exe -WindowStyle Hidden -PassThru }
    $window = [IntPtr]::Zero
    while ($watch.Elapsed.TotalSeconds -lt 20) {
        $primary.Refresh()
        if ($primary.HasExited) { throw "Primary exited: $($primary.ExitCode)" }
        $window = [IslandSmokeNative]::Find($primary.Id)
        if ($window -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 50
    }
    if ($window -eq [IntPtr]::Zero) { throw 'No visible island window within 20 seconds' }
    "Visible window after $($watch.ElapsedMilliseconds) ms (process launch to visible HWND, not interaction or cold-start acceptance)"
    $secondary = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    if (-not $secondary.WaitForExit(10000)) { throw 'Secondary did not exit within 10 seconds' }
    if ($secondary.ExitCode -ne 0) { throw "Secondary failed: $($secondary.ExitCode)" }
    $primary.Refresh()
    if ($primary.HasExited) { throw 'Primary exited during forwarding' }
    'Second launch forwarding: PASS'
    [void][IslandSmokeNative]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    if (-not $primary.WaitForExit(5000)) { throw 'Closing island did not terminate primary within 5 seconds' }
    if ($null -ne $primary.ExitCode -and $primary.ExitCode -ne 0) { throw "Primary failed on exit: $($primary.ExitCode)" }
    'Window close and process cleanup: PASS'
} finally {
    foreach ($process in @($secondary, $primary)) {
        if ($process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                $remainingWindow = [IslandSmokeNative]::Find($process.Id)
                if ($remainingWindow -ne [IntPtr]::Zero) {
                    [void][IslandSmokeNative]::PostMessage($remainingWindow, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
                    [void]$process.WaitForExit(5000)
                }
                $process.Refresh()
                if (-not $process.HasExited) { Write-Warning "Process $($process.Id) remains running; exit through the tray after pending writes finish." }
            }
        }
    }
}
