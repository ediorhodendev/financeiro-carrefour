#!/usr/bin/env python3
"""Bounded, dependency-free HTTP exercise; use an isolated local/demo account only."""
import argparse, concurrent.futures, json, os, statistics, time, urllib.request, urllib.error, uuid

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--base-url',default='http://localhost:5080')
    parser.add_argument('--account-id',required=True)
    parser.add_argument('--requests',type=int,default=100)
    parser.add_argument('--concurrency',type=int,default=8)
    parser.add_argument('--write-demo',action='store_true',help='Credit 0.01 BRL per request; creates real demo movements.')
    args=parser.parse_args()
    if not 1<=args.requests<=10000 or not 1<=args.concurrency<=64:parser.error('requests 1..10000; concurrency 1..64')
    uuid.UUID(args.account_id)
    token=os.environ.get('FINANCIAL_LOAD_TOKEN')
    if not token:parser.error('Set FINANCIAL_LOAD_TOKEN to a valid local JWT')
    # Default reads do not mutate. Writes require an explicit flag and unique keys.
    path=f'/api/accounts/{args.account_id}/'+('movements' if args.write_demo else 'balance')
    def call(_):
        headers={'Authorization':'Bearer '+token}
        data=None
        if args.write_demo:
            headers.update({'Content-Type':'application/json','Idempotency-Key':str(uuid.uuid4())})
            data=json.dumps({'type':'Credit','amount':0.01}).encode()
        start=time.perf_counter()
        try:
            with urllib.request.urlopen(urllib.request.Request(args.base_url.rstrip('/')+path,data=data,headers=headers),timeout=20) as response:
                response.read();code=response.status
        except urllib.error.HTTPError as error:code=error.code
        except (urllib.error.URLError,TimeoutError):code=0
        return code,(time.perf_counter()-start)*1000
    start=time.perf_counter()
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.concurrency) as pool:results=list(pool.map(call,range(args.requests)))
    elapsed=time.perf_counter()-start;latencies=sorted(latency for _,latency in results)
    counts={str(code):sum(1 for actual,_ in results if actual==code) for code in sorted({code for code,_ in results})}
    report={'requests':args.requests,'concurrency':args.concurrency,'mode':'credit-demo' if args.write_demo else 'read-balance','elapsedSeconds':round(elapsed,3),'throughputPerSecond':round(args.requests/elapsed,2),'latencyMs':{'median':round(statistics.median(latencies),2),'p95':round(latencies[min(len(latencies)-1,int(.95*len(latencies)))],2),'max':round(max(latencies),2)},'statusCodes':counts}
    print(json.dumps(report,indent=2))
    return 0 if all(200<=code<300 for code,_ in results) else 1
if __name__=='__main__':raise SystemExit(main())
