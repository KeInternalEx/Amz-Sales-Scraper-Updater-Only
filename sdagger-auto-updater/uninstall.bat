pushd %~dp0
set CURDIR=%CD%
popd
TASKKILL /F /IM "sdagger-auto-updater-[EXTENSION_ID].exe"
TASKKILL /F /IM "[EXTENSION_ID]_chrome.exe"
REG DELETE "HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "SDaggerUpdateService[EXTENSION_ID]" /f
RMDIR /Q /S %CURDIR%