' Creates (or refreshes) a "W2 Monitor" shortcut on the Desktop that launches this app.
' Run this once after extracting the W2Monitor folder anywhere on your PC.
Dim shell, fso, here, desktop, target, lnk
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
here = fso.GetParentFolderName(WScript.ScriptFullName)
desktop = shell.SpecialFolders("Desktop")
target = here & "\W2Monitor.exe"

If Not fso.FileExists(target) Then
  MsgBox "W2Monitor.exe was not found next to this script." & vbCrLf & _
         "Keep this script inside the extracted W2Monitor folder and run it again.", _
         48, "W2 Monitor"
  WScript.Quit 1
End If

Set lnk = shell.CreateShortcut(desktop & "\W2 Monitor.lnk")
lnk.TargetPath = target
lnk.WorkingDirectory = here
lnk.IconLocation = target & ", 0"   ' use the icon embedded in the executable
lnk.Description = "W2 Monitor - Elecraft W2 wattmeter monitor"
lnk.Save

MsgBox "Desktop shortcut 'W2 Monitor' created.", 64, "W2 Monitor"
