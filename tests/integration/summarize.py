import json, os

with open('mcp-output.json', encoding='utf-8') as f:
    data = json.load(f)

print('=' * 70)
print('MCP SERVER END-TO-END VERIFICATION')
print('=' * 70)

tools = data['_tools_list']
print(f'\nTools discovered: {len(tools)}')

ls = data['LoadSolution']
print(f"\nLoadSolution: success={ls['success']}, projects={ls['projectCount']}")
for p in ls.get('projects', []):
    print(f"  - {p.get('name','?')}: {p.get('documentCount',0)} docs")

m = data['GetMediatRMappings']
print(f"\nGetMediatRMappings: totalMappings={m['totalMappings']}")
loc_populated = sum(1 for x in m['mappings'] if (x.get('handlerLocation') or {}).get('filePath'))
print(f"  ({loc_populated}/{m['totalMappings']} have handler locations)")

print()
for k in ('FindImplementations:IUserService',
         'FindImplementations:INotificationService',
         'FindImplementations:IPaymentProcessor'):
    v = data[k]
    print(f"{k}: count={v['implementationCount']}")
    for impl in v['implementations']:
        cls = impl.get('implementingClass', '?')
        ns = impl.get('namespace', '?')
        print(f"  - {ns}.{cls}")

def summarize_callees(label, c):
    print()
    print('-' * 70)
    print(f"FindCalleesFromLocation: {label}")
    print('-' * 70)
    print(f"  SourceMethod: {c.get('SourceMethod','?')}")
    print(f"  TotalCallees: {c.get('TotalCallees',0)}")
    print(f"  Callees: {len(c.get('Callees',[]))}, DB Ops: {len(c.get('DatabaseOperations',[]))}")

    handlers = set()
    unresolved = 0
    mediatr_calls = []
    for cl in c.get('Callees', []):
        ct = cl.get('CallType','')
        method = cl.get('Method','')
        target = cl.get('TargetHandler')
        if ct == 'MediatR':
            mediatr_calls.append((method, target))
            if target:
                handlers.add(target)
            else:
                unresolved += 1

    print(f"  MediatR sends: {len(mediatr_calls)} (unresolved: {unresolved})")
    print(f"  Distinct handlers reached: {len(handlers)}")
    for h in sorted(handlers):
        print(f"    - {h}")

    print(f"  Database Operations:")
    for op in c.get('DatabaseOperations', []):
        otype = op.get('Type','?')
        opn = op.get('Operation','?')
        table = op.get('Table','?')
        method = op.get('Method','?')
        loc = op.get('Location','')
        print(f"    {otype:6} {opn:8} {table:20} {method:25} {loc}")

for label in ('Index', 'ProcessUserAction', 'GetUserDetails', 'ManageUser'):
    key = f'FindCalleesFromLocation:{label}'
    if key in data:
        summarize_callees(f'HomeController.{label}', data[key])
