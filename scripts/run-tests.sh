#!/usr/bin/env bash
# run-tests.sh — Ejecuta la suite xUnit completa.
set -e
cd "$(dirname "$0")/.."
dotnet test src/GameRouteOptimizer.Tests/GameRouteOptimizer.Tests.csproj --verbosity minimal "$@"
