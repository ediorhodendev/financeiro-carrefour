#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export API_PORT=15080 POSTGRES_PORT=15432 RABBITMQ_MANAGEMENT_PORT=15673
export POSTGRES_PASSWORD=ci-docker-demo-only RABBITMQ_PASSWORD=ci-rabbit-demo-only
cleanup() { docker compose -p carrefour-ci logs --no-color > TestResults/compose.log 2>&1; docker compose -p carrefour-ci down -v; }
mkdir -p TestResults
trap cleanup EXIT
docker compose -p carrefour-ci up --build -d
python3 scripts/docker-smoke.py
