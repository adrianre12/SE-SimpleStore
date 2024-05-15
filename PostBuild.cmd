set REPLACE_IN_PATH=%APPDATA%\SpaceEngineers\Mods\SimpleStore

copy /y "%REPLACE_IN_PATH%"\metadata.mod .
copy /y "%REPLACE_IN_PATH%"\modinfo.sbmi .

rmdir "%REPLACE_IN_PATH%" /S /Q

robocopy.exe .\ "%REPLACE_IN_PATH%" *.* /S /xd .git bin obj .vs ignored /xf *.exe *.dll *.lnk *.git* *.bat *.zip *.7z *.blend* *.png *.md *.log *.sln *.csproj *.csproj.user *.ruleset desktop.ini *.fbx *.hkt *.xml *.txt

echo done
