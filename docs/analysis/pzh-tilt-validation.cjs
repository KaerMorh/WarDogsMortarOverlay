// Run: node docs/analysis/pzh-tilt-validation.cjs
// Screenshot data; pair order is 2 -> 1 and 4 -> 3.
// No application changes. No external dependencies.
const rows = [
  [100.03,120.91,101.48,120.16,1303],
  [101.71,119.96,103.39,119.01,1315],
  [88.00,103.71,88.82,101.71,1302],
  [88.81,101.86,89.62,100.01,1292],
];
const data = rows.map(([tx,ty,lx,ly,mil]) => ({
  t:[100*(tx-97.97),100*(ty-109.57)],
  l:[100*(lx-97.97),100*(ly-109.57)], mil,
}));
const wrap = a => Math.atan2(Math.sin(a),Math.cos(a));
const deg = a => a*180/Math.PI;
const bearing = v => Math.atan2(v[0],v[1]);
const rms = a => Math.sqrt(a.reduce((s,x)=>s+x*x,0)/a.length);

// A single rigid rotation of the nominal firing vector. The two parameters
// tilt the reference plane's normal toward world east/north. No vehicle yaw.
// MIL conversion is an explicit hypothesis, not established by application code.
function tiltedBearing(d,p,k) {
  const az=bearing(d.t), e=d.mil*k;
  const v=[Math.cos(e)*Math.sin(az),Math.cos(e)*Math.cos(az),Math.sin(e)];
  const w=[-p[1],p[0],0], a=Math.hypot(...w), c=Math.cos(a);
  const s=a?Math.sin(a)/a:1, b=a?(1-c)/(a*a):0.5;
  const dot=w[0]*v[0]+w[1]*v[1];
  const cross=[w[1]*v[2],-w[0]*v[2],w[0]*v[1]-w[1]*v[0]];
  const z=v.map((n,i)=>c*n+s*cross[i]+b*w[i]*dot);
  return bearing(z);
}

// Only azimuth is fitted. Each shot's unknown height/range remains a nuisance
// quantity, unrestricted here. Requires no sideways trajectory deflection.
// This tests a necessary angular prediction, not full 3D ballistic feasibility.
function fit(ids,predict,scale=1,samples=data) {
  const loss=p=>ids.reduce((s,i)=>s+wrap(predict(samples[i],p)-bearing(samples[i].l))**2,0);
  let p=[0,0];
  for(let step=.04*scale;step>1e-9*scale;step*=.5) {
    for(let iteration=0;iteration<2000;iteration++) {
      let best=p, value=loss(p);
      for(const dx of [-step,0,step]) for(const dy of [-step,0,step]) {
        const q=[p[0]+dx,p[1]+dy];
        if(Math.hypot(...q)>.3*scale) continue;
        const next=loss(q);
        if(next<value) { best=q;value=next; }
      }
      if(best===p) break;
      p=best;
    }
  }
  const residual=samples.map(d=>deg(wrap(predict(d,p)-bearing(d.l))));
  const heldOut=samples.map((_,i)=>i).filter(i=>!ids.includes(i));
  return {
    trainingImages:ids.map(i=>i+1), parameters:p,
    predictedBiasDegrees:samples.map(d=>deg(wrap(predict(d,p)-bearing(d.t)))),
    residualDegrees:residual,
    trainingRmsDegrees:rms(ids.map(i=>residual[i])),
    heldOutRmsDegrees:heldOut.length?rms(heldOut.map(i=>residual[i])):null,
    heldOutTransverseMissMeters:heldOut.map(i=>({image:i+1,
      miss:Math.hypot(...samples[i].l)*Math.sin(residual[i]*Math.PI/180)})),
  };
}
const observed=data.map(d=>deg(wrap(bearing(d.l)-bearing(d.t))));
const output={observedBiasDegrees:observed,models:[]};
for(const k of [.001,2*Math.PI/6400]) {
  for(const ids of [[0,1],[2,3],[0,1,2,3]]) {
    const result=fit(ids,(d,p)=>tiltedBearing(d,p,k));
    output.models.push({model:'rigid tilt',radiansPerMil:k,...result,
      equivalentTiltDegrees:deg(Math.hypot(...result.parameters))});
  }
}
// Alternative: shifted effective origin / target vector, plus unrestricted
// per-shot travel distance. This is not a claim that the recorded origin is wrong.
for(const ids of [[0,1],[2,3],[0,1,2,3]]) {
  output.models.push({model:'shifted horizontal vector',...fit(ids,
    (d,p)=>bearing([d.t[0]+p[0],d.t[1]+p[1]]),1000)});
}
const mean=observed.reduce((s,x)=>s+x,0)/4;
output.constantYaw={biasDegrees:mean,rmsDegrees:rms(observed.map(x=>x-mean))};
// New user-supplied shots. Evaluate the previously fitted parameters BEFORE
// adding these shots to data. The nominal 1165 m is the actual 1300 MIL table node.
const table=require('../../src/Data/weapons.json').weapons.find(w=>w.id==='spg').ballistics.high;
const nominalRange=table.find(row=>row[1]===1300)[0];
const recent=[
  {t:[nominalRange,0],l:[(111.45-97.97)*100,(108.04-109.57)*100],mil:1300},
  {t:[-nominalRange,0],l:[(86.91-97.97)*100,(108.30-109.57)*100],mil:1300},
];
output.newShots=recent.map(d=>({commandDegrees:(deg(bearing(d.t))+360)%360,
  impactVectorMeters:d.l,rangeMeters:Math.hypot(...d.l),
  observedBiasDegrees:deg(wrap(bearing(d.l)-bearing(d.t)))}));
output.frozenPredictions=output.models.filter(m=>m.trainingImages.length===4).map(m=>{
  const predict=m.model==='rigid tilt'?
    d=>tiltedBearing(d,m.parameters,m.radiansPerMil):
    d=>bearing(d.t.map((x,i)=>x+m.parameters[i]));
  return {model:m.model,radiansPerMil:m.radiansPerMil,parameters:m.parameters,
    predictions:recent.map(d=>({biasDegrees:deg(wrap(predict(d)-bearing(d.t))),
      residualDegrees:deg(wrap(predict(d)-bearing(d.l))),
      transverseResidualMeters:Math.hypot(...d.l)*Math.sin(predict(d)-bearing(d.l))}))};
});
output.oppositeShotMidpointMeters=[0,1].map(j=>(recent[0].l[j]+recent[1].l[j])/2);
// Diagnostic refits, explicitly separate from the frozen prospective check.
data.push(...recent);
output.refits=[];
for(const k of [.001,2*Math.PI/6400]) for(const ids of [[4,5],[0,1,2,3,4,5]]) {
  const result=fit(ids,(d,p)=>tiltedBearing(d,p,k));
  output.refits.push({model:'rigid tilt',radiansPerMil:k,...result,
    equivalentTiltDegrees:deg(Math.hypot(...result.parameters))});
}
output.refits.push({model:'shifted horizontal vector',...fit([0,1,2,3,4,5],
  (d,p)=>bearing(d.t.map((x,i)=>x+p[i])),1000)});
// Predictions made now for a possible future independent test, not observations.
const futureRange=table.find(row=>row[1]===1000)[0];
output.future1000Mil=output.refits.filter(m=>m.trainingImages.length===6).map(m=>{
  const predict=m.model==='rigid tilt'?
    d=>tiltedBearing(d,m.parameters,m.radiansPerMil):
    d=>bearing(d.t.map((x,i)=>x+m.parameters[i]));
  return {model:m.model,radiansPerMil:m.radiansPerMil,
    predictions:[1,-1].map(sign=>{
      const d={t:[sign*futureRange,0],mil:1000};
      return {commandDegrees:sign===1?90:270,
        predictedWorldBearingDegrees:(deg(predict(d))+360)%360};
    })};
});
// Separate parking state: two screenshots, same two targets as old images 1/2.
// The 05:48:55 image was supplied twice; it is only one observation.
// Assumes each impact resulted from the displayed high-arc firing solution.
const movedOrigin=[97.06,109.11];
const movedRows=[
  {time:'05:47:13',target:[100.03,120.91],impact:[99.59,122.69],mil:1287},
  {time:'05:48:55',target:[101.71,119.96],impact:[101.51,122.02],mil:1296},
];
const movedData=movedRows.map(row=>({
  t:row.target.map((x,i)=>(x-movedOrigin[i])*100),
  l:row.impact.map((x,i)=>(x-movedOrigin[i])*100),mil:row.mil,
}));
output.movedParkingState={origin:movedOrigin,
  shots:movedData.map((moved,index)=>({ ...movedRows[index],
    nominalBearingDegrees:deg(bearing(moved.t)),actualBearingDegrees:deg(bearing(moved.l)),
    observedBiasDegrees:deg(wrap(bearing(moved.l)-bearing(moved.t))),
    targetDistanceMeters:Math.hypot(...moved.t),impactDistanceMeters:Math.hypot(...moved.l),
    errorVectorMeters:moved.l.map((x,i)=>x-moved.t[i]),
    // Counterfactual persistence checks, NOT fixed-state validation shots.
    ifOldParametersPersisted:output.refits.filter(m=>m.trainingImages.length===6).map(m=>{
      const predicted=m.model==='rigid tilt'?tiltedBearing(moved,m.parameters,m.radiansPerMil):
        bearing(moved.t.map((x,i)=>x+m.parameters[i]));
      return {model:m.model,radiansPerMil:m.radiansPerMil,
        predictedBiasDegrees:deg(wrap(predicted-bearing(moved.t))),
        residualDegrees:deg(wrap(predicted-bearing(moved.l)))};
    })})),
  // Two scalar angular observations, two parameters: interpolation, no validation DOF.
  descriptiveFits:[.001,2*Math.PI/6400].map(k=>{
    const result=fit([0,1],(d,p)=>tiltedBearing(d,p,k),1,movedData);
    return {radiansPerMil:k,...result,
      equivalentTiltDegrees:deg(Math.hypot(...result.parameters))};
  })};
console.log(JSON.stringify(output,null,2));
