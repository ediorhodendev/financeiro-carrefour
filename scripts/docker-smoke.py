"""End-to-end checks against disposable Compose services; invoked by test-docker.sh."""
import base64
import json
import os
import subprocess
import time
import urllib.error
import urllib.request
import uuid

BASE = 'http://localhost:' + os.environ.get('API_PORT', '15080')
BROKER = 'http://localhost:' + os.environ.get('RABBITMQ_MANAGEMENT_PORT', '15673')

def request(path, data=None, token=None, key=None, broker=False):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if broker:
        credentials = 'financial_local:' + os.environ['RABBITMQ_PASSWORD']
        headers['Authorization'] = 'Basic ' + base64.b64encode(credentials.encode()).decode()
    if key:
        headers['Idempotency-Key'] = key
    req = urllib.request.Request((BROKER if broker else BASE) + path,
        data=json.dumps(data).encode() if data is not None else None, headers=headers)
    with urllib.request.urlopen(req, timeout=20) as response:
        return response.status, json.load(response)

def eventually(action, predicate, label, attempts=90):
    for _ in range(attempts):
        try:
            value = action()
            if predicate(value):
                return value
        except (OSError, urllib.error.URLError):
            pass
        time.sleep(1)
    raise AssertionError('Timed out: ' + label)

eventually(lambda: request('/health/ready'), lambda result: result[0] == 200, 'API readiness')
_, demo = request('/demo/access', {})
account, token = demo['accountId'], demo['operatorToken']
path = '/api/accounts/' + account
prefix = str(uuid.uuid4())
status, credit = request(path + '/movements', {'type': 1, 'amount': 100}, token, prefix+'-credit')
assert status == 201
status, replay = request(path + '/movements', {'type': 1, 'amount': 100}, token, prefix+'-credit')
assert status == 200 and credit['id'] == replay['id']
status, debit = request(path + '/movements', {'type': 2, 'amount': 30}, token, prefix+'-debit')
assert status == 201
assert request(path + '/balance', token=token)[1]['balance'] == 70
expected = {credit['id'], debit['id']}
eventually(lambda: request(path + '/receipts', token=token)[1],
    lambda rows: expected.issubset({row['eventId'] for row in rows}), 'consumer receipts')

# Publish a second copy of the same event, then prove exactly one observable effect.
payload = json.dumps({'Version': 1, 'EventId': credit['id'], 'MovementId': credit['id'],
    'AccountId': account, 'Type': 0, 'Amount': 100, 'RegisteredAt': credit['registeredAt']})
request('/api/exchanges/%2F/financial.events.v1/publish', {
    'properties': {'delivery_mode': 2}, 'routing_key': '', 'payload': payload,
    'payload_encoding': 'string'}, broker=True)
# Also prove malformed deliveries reach the dead-letter queue.
request('/api/exchanges/%2F/financial.events.v1/publish', {
    'properties': {'delivery_mode': 2}, 'routing_key': '', 'payload': '{invalid-json',
    'payload_encoding': 'string'}, broker=True)
eventually(lambda: request('/api/queues/%2F/financial.receipts.dead.v1', broker=True)[1],
    lambda q: q.get('messages_ready', 0) >= 1, 'poison message DLQ')
assert sum(r['eventId'] == credit['id'] for r in request(path + '/receipts', token=token)[1]) == 1

# Financial writes must remain available while the message broker is unavailable.
subprocess.run(['docker', 'compose', '-p', 'carrefour-ci', 'stop', 'rabbitmq'], check=True)
status, offline = request(path + '/movements', {'type': 1, 'amount': 5}, token, prefix+'-offline')
assert status == 201
assert request(path + '/balance', token=token)[1]['balance'] == 75
subprocess.run(['docker', 'compose', '-p', 'carrefour-ci', 'start', 'rabbitmq'], check=True)
eventually(lambda: request(path + '/receipts', token=token)[1],
    lambda rows: any(r['eventId'] == offline['id'] for r in rows), 'outbox recovery after broker restart', 120)
assert request(path + '/balance', token=token)[1]['balance'] == 75
# Reverse the 5 BRL credit exactly once and wait for its integration receipt.
reversal_path = path + '/movements/' + offline['id'] + '/reversal'
status, reversal = request(reversal_path, {'reason': 'Incorrect demo credit'}, token, prefix+'-reversal')
assert status == 201 and reversal['type'] == 2 and reversal['amount'] == 5
assert reversal['reversalOfMovementId'] == offline['id']
status, replay = request(reversal_path, {'reason': 'Incorrect demo credit'}, token, prefix+'-reversal')
assert status == 200 and replay['replayed'] and replay['id'] == reversal['id']
for target, key in [(reversal_path, prefix+'-second-reversal'),
                    (path+'/movements/'+reversal['id']+'/reversal', prefix+'-reverse-reversal')]:
    try:
        request(target, {'reason': 'Another correction'}, token, key)
        raise AssertionError('Forbidden reversal unexpectedly succeeded')
    except urllib.error.HTTPError as error:
        assert error.code == 409
assert request(path + '/balance', token=token)[1]['balance'] == 70
eventually(lambda: request(path + '/receipts', token=token)[1],
    lambda rows: any(r['eventId'] == reversal['id'] for r in rows), 'reversal event receipt')
print('PASS: financial operations, HTTP idempotency, consumer deduplication, DLQ, broker outage recovery and full reversal; final balance 70 BRL.')
