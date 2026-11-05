@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /win32manifest:app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:DnsToggle.exe DnsToggle.cs
