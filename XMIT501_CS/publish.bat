@echo off
echo Publishing XMIT501...
dotnet dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

echo Signing executable...
:: Replace with your actual paths
set SIGNTOOL="C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe"
set CERT="D:\Media\XMIT501\proj\XMIT501_CS\XMIT_Cert.pfx"
set EXE="bin\Release\net8.0-windows\win-x64\publish\XMIT501.exe"

%SIGNTOOL% sign /fd SHA256 /f %CERT% /p 974974 %EXE%
echo Done!
pause