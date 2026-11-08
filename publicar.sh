#!/usr/bin/env bash
# Gera publish/SetupSwitch.exe e publish/SetupSwitchCli.exe.
# Cada projeto publica em pasta própria: na mesma pasta, um apaga o executável do outro.
set -e

for projeto in Cli App; do
  dotnet publish "src/SetupSwitch.$projeto/SetupSwitch.$projeto.csproj" -c Release -r win-x64 \
    --self-contained false -p:PublishSingleFile=true -p:DebugType=none \
    -o "publish-$projeto" --nologo -v q
done

mkdir -p publish
cp publish-Cli/SetupSwitchCli.exe publish-App/SetupSwitch.exe publish/
rm -rf publish-Cli publish-App
