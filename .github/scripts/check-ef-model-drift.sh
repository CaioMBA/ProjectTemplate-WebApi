#!/usr/bin/env bash
# Fails when an EF Core model change has no migration for one or more SQL providers.
# Runs as the shared pipeline's verify step (run-verify / verify-command), after the
# Release build and before the tests, from the repository root.
set -euo pipefail

dotnet tool restore

drift=0
for context in Postgresql SqlServer Mysql Oracle Firebird Sqlite; do
  if dotnet ef migrations has-pending-model-changes \
       --project Source/Infrastructure/Data/Data.Sql \
       --startup-project Source/WebApi \
       --context "${context}AppDbContext" \
       --configuration Release --no-build; then
    echo "ok    ${context}"
  else
    echo "DRIFT ${context}"
    drift=1
  fi
done

if [ "$drift" -ne 0 ]; then
  echo "::error::A model change is missing migrations for one or more providers."
  exit 1
fi
