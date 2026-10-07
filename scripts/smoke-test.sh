#!/usr/bin/env bash
set -euo pipefail
base="${1:-http://localhost:18080}"

echo "Waiting for $base ..."
for i in $(seq 1 30); do
  if curl --silent --fail --output /dev/null "$base/"; then
    echo "App is up (attempt $i)"
    break
  fi
  if [ "$i" -eq 30 ]; then
    echo "App did not become ready in time" >&2
    exit 1
  fi
  sleep 2
done

echo "POST /api/tasks"
created=$(curl --fail-with-body --silent --show-error -X POST "$base/api/tasks" \
  -H "Content-Type: application/json" \
  -d '{"title":"ci smoke test","priority":"High"}')
echo "$created"

echo "GET /api/tasks"
list=$(curl --fail-with-body --silent --show-error "$base/api/tasks")
echo "$list"

echo "$list" | grep -q "ci smoke test" || { echo "Created task not found in list" >&2; exit 1; }
echo "Smoke test passed"
