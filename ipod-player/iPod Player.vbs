' iPod Player - double-click launcher
' Starts the built-in server hidden and opens the player in its own app window.
' Closing the app window shuts everything down.
' Tip: for an iPod Touch, drag the folder that CONTAINS iTunes_Control onto this file.
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh  = CreateObject("WScript.Shell")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
sh.CurrentDirectory = scriptDir

extra = ""
If WScript.Arguments.Count > 0 Then
  extra = " -Root """ & WScript.Arguments(0) & """"
End If

sh.Run "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & scriptDir & "\ipod-server.ps1""" & extra, 0, False
