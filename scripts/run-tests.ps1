# run-tests.ps1 — Ejecuta la suite xUnit completa en Windows.
param()
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
dotnet test src/GameRouteOptimizer.Tests/GameRouteOptimizer.Tests.csproj --verbosity minimal @args
