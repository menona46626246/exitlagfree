#!/usr/bin/env bash
# build.sh — Compilación en Linux/CI: Core, Service, Cli y Tests.
# NOTA: la App WPF (net8.0-windows) solo compila en Windows; usa scripts/build.ps1 allí.
set -e
cd "$(dirname "$0")/.."

CONFIG="${1:-Debug}"
echo "=== GameRoute Optimizer — build (no-Windows) $CONFIG ==="

for proj in Core Service Cli Tests; do
    echo "--- GameRouteOptimizer.$proj ---"
    dotnet build "src/GameRouteOptimizer.$proj/GameRouteOptimizer.$proj.csproj" -c "$CONFIG"
done

echo "--- Tests ---"
dotnet test src/GameRouteOptimizer.Tests/GameRouteOptimizer.Tests.csproj -c "$CONFIG" --no-build --verbosity minimal
echo "✔ Compilación y tests OK (Core/Service/Cli/Tests)."
