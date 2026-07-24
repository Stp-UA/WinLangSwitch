# Build the project using .NET CLI with specified release parameters
dotnet publish WinLangSwitch.Daemon.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Ensure the destination directory exists to prevent the file from being renamed inadvertently
if (!(Test-Path build)) { 
    New-Item -ItemType Directory build 
}

# Copy the generated executable to the build folder, overwriting any existing version
Copy-Item bin/Release/net8.0/win-x64/publish/WinLangSwitch.Daemon.exe -Destination build/ -Force
