const fs=require('fs'),path=require('path'),vm=require('vm');
const dir=process.argv[2];const ctx=vm.createContext({});
vm.runInContext(fs.readFileSync(path.join(dir,'weapons.js'),'utf8'),ctx);
const raw=JSON.parse(fs.readFileSync(path.join(dir,'weapons.json'),'utf8'));
const fixtures=JSON.parse(fs.readFileSync(path.join(dir,'parity-fixtures.json'),'utf8'));
let count=0;
for(const f of fixtures){ctx.raw=raw.weapons.find(w=>w.id===f.id);ctx.d=f.distance;const expected=vm.runInContext('getWeaponElevationSolutions(normalizeWeapon(raw),d)',ctx);
 for(const arc of ['single','low','high']){const a=f[arc],b=expected[arc];if((a===null)!=(b===null)||a&&(Math.abs(a.Min-b.minMil)>1e-6||Math.abs(a.Max-b.maxMil)>1e-6))throw new Error(JSON.stringify({f,arc,expected}));count++}
 if(f.inRange!==expected.inRange)throw new Error('Range mismatch '+JSON.stringify(f));
}
console.log(`PASS: ${fixtures.length} distances / ${count} branch comparisons against pinned upstream JavaScript`);
