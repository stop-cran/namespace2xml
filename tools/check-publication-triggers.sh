#!/usr/bin/env bash
# A workflow that can publish from a branch can publish an unreviewed artifact under a trusted
# package name. namespace2xml 2.x did exactly that: it pushed to the registry on every push to
# master. This script fails the build if that is ever true again.
#
# It lives outside .github/workflows on purpose. Inlined in a workflow, the check's own pattern
# text matches the file it is written in, and the gate reports a violation against itself.
set -euo pipefail

# Assembled from fragments so this file cannot match its own pattern either.
registry='nuget'"\\."'org'
secret='NUGET''_API_KEY'
push='dotnet nuget ''push'
pattern="${registry}|${secret}|${push}"

status=0

for workflow in .github/workflows/*.yml; do
    if ! grep -qE "$pattern" "$workflow"; then
        continue
    fi

    echo "$workflow can publish packages; checking its triggers."

    if ! grep -qE '^\s{4}tags:' "$workflow"; then
        echo "::error file=$workflow::publishes packages but is not restricted to tag pushes"
        status=1
    fi

    if grep -qE '^\s{4}branches:' "$workflow"; then
        echo "::error file=$workflow::publishes packages and also triggers on branches"
        status=1
    fi
done

if [ "$status" -eq 0 ]; then
    echo "Publication triggers are tag-only."
fi

exit "$status"
