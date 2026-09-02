@echo off
pushd "%~dp0"

\\This section sends a Server Chat message for all servers on you cluster. -H is the hostIP, -P is the RCON port, -p is the server admin password

"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxxx -p xxxxx "ServerChat Mod Update in 10 min."
timeout /t 300 /nobreak >nul
"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxxx -p xxxxx "ServerChat Mod Update in 5 min."
timeout /t 60 /nobreak >nul
"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxxx -p xxxxx "ServerChat Mod Update in 4 min."
timeout /t 60 /nobreak >nul
"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxxx -p xxxxx "ServerChat Mod Update in 3 min."
timeout /t 60 /nobreak >nul
"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxxx -p xxxxx "ServerChat Mod Update in 2 min."
timeout /t 60 /nobreak >nul
"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxxx -p xxxxx "ServerChat Mod Update in 1 min."
timeout /t 60 /nobreak >nul

\\This section does the World Save right before sending the DoExit command. Check your terminal window on the server to see how long a world save takes to make sure the timeout is longer than the Save operation. You do NOT want to interrupt a world save with a DoExit 

"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxxx -p xxxxx "SaveWorld"
timeout /t 10 /nobreak >nul
"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxxx -p xxxxx "DoExit"

"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxx -p xxxxx "SaveWorld"
timeout /t 10 /nobreak >nul
"mcrcon.exe" -H xxx.xxx.xxx.xxx -P xxxx -p xxxxx "DoExit"

\\This timer is how long it takes for your total amount of servers to shut down and should be set for the time that takes before starting your first server to make sure all servers are offline before sequentially restarting them again. This will help space out disk write IO for your servers.

timeout /t 240 /nobreak >nul
popd

\\This section is where you place your server command line entries to re-start the servers after shutdown. Set the timer after the server's command line to the time it takes to call your world init before starting the next one. 

Start "Aberration" /normal /min "D:\servers\AberrationASM\ShooterGame\Binaries\Win64\AsaApiLoader.exe" Aberration_WP?listen?Port=xxxx -ServerKey=Aberration -EnableIdlePlayerKick -UnstasisDinoObstructionCheck -MULTIHOME=xxx.xxx.xxx.xxx -WinLiveMaxPlayers=70 -servergamelog -clusterid= -ClusterDirOverride="D:\" -NoTransferFromFiltering -OldConsole -NoGameAnalytics -mods= -ForceRecreateSaveGameDatabaseOnFirstSave -SaveFolder="E:/aberration/Saved/SavedArks/Aberration_WP" -RconIp=xxx.xxx.xxx.xxx -LANPLAY -QueryPort=22419
timeout /t 60 /nobreak >nul

exit