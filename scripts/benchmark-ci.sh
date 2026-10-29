#!/usr/bin/env bash
# Bounded exercise against the ephemeral CI PostgreSQL database, never production.
set -euo pipefail
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Financial="${FINANCIAL_TEST_CONNECTION:?Isolated test connection required}"
mkdir -p TestResults
dotnet tool restore
dotnet ef database update --project src/Financial.Infrastructure --startup-project src/Financial.Api --configuration Release --no-build
SEED_OUTPUT=$(dotnet run --project src/Financial.Api --no-launch-profile --configuration Release --no-build -- --seed-demo)
ACCOUNT_ID=$(printf '%s\n' "$SEED_OUTPUT" | sed -n 's/^DEMO_ACCOUNT_ID=//p')
test -n "$ACCOUNT_ID"
FINANCIAL_LOAD_TOKEN=$(dotnet user-jwts create --project src/Financial.Api --name demo-operator --role operator --audience http://localhost:5080 --valid-for 1h --output token)
export FINANCIAL_LOAD_TOKEN
printf '::add-mask::%s\n' "$FINANCIAL_LOAD_TOKEN"
dotnet run --project src/Financial.Api --no-launch-profile --configuration Release --no-build --urls http://localhost:5080 > TestResults/benchmark-api.log 2>&1 &
API_PID=$!
trap 'kill "$API_PID" 2>/dev/null || true' EXIT
READY=0
for attempt in $(seq 1 30); do
  if curl --fail --silent http://localhost:5080/health/ready > /dev/null; then READY=1; break; fi
  sleep 1
done
test "$READY" -eq 1
python3 scripts/load-test.py --account-id "$ACCOUNT_ID" --requests 100 --concurrency 8 > TestResults/performance-read.json
python3 scripts/load-test.py --account-id "$ACCOUNT_ID" --requests 100 --concurrency 8 --write-demo > TestResults/performance-write.json
export FINANCIAL_BENCHMARK_ACCOUNT_ID="$ACCOUNT_ID"
python3 - <<'PY'
import json,os,urllib.request
from decimal import Decimal
account=os.environ['FINANCIAL_BENCHMARK_ACCOUNT_ID']
headers={'Authorization':'Bearer '+os.environ['FINANCIAL_LOAD_TOKEN']}
request=urllib.request.Request('http://localhost:5080/api/accounts/'+account+'/balance',headers=headers)
with urllib.request.urlopen(request,timeout=10) as response: balance=json.load(response,parse_float=Decimal)
assert balance['balance']==Decimal('1.00'), '100 successful 0.01 credits must produce exactly 1.00 BRL'
print('Post-load balance verified: 1.00 BRL, 100 credits at 0.01 BRL.')
for name in ('performance-read','performance-write'):
    with open('TestResults/'+name+'.json') as f: report=json.load(f)
    print(name+': '+json.dumps(report))
PY
