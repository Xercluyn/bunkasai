(()=>{
const W=800,H=520,GROUND=118,PX=400,PY=96;
const cv=document.getElementById('c'),ctx=cv.getContext('2d');
const $=id=>document.getElementById(id);
const reduce=matchMedia('(prefers-reduced-motion: reduce)').matches;
const yen=n=>'¥'+Math.floor(n).toLocaleString('ja-JP');

function fit(){
  const r=cv.getBoundingClientRect(),d=Math.min(window.devicePixelRatio||1,2);
  cv.width=Math.round(r.width*d);cv.height=Math.round(r.width*H/W*d);
}
addEventListener('resize',fit);

const goal=3000;
let bestTime=0;try{bestTime=+localStorage.getItem('yen-best-2r')||0}catch(e){}
/* ---- celebration: falling money + confetti, and a short fanfare ---- */
let party=[];
function celebrate(silent){
  party=[];
  const kinds=['c500','c100','c10','c5','b1k','b10k'];
  const cols=['#f2c14e','#d94a3d','#7fd3b5','#9fb4f2','#f4e6c4','#f2a0b5'];
  for(let i=0;i<110;i++){
    const money=i%3===0;
    party.push({money,t:kinds[i%kinds.length],c:cols[i%cols.length],
      x:Math.random()*W,y:-20-Math.random()*H*1.2,vy:60+Math.random()*120,vx:(Math.random()-.5)*40,
      rot:Math.random()*6,vr:(Math.random()-.5)*4,w:6+Math.random()*6,h:10+Math.random()*8,ph:Math.random()*6});
  }
  if(reduce)party.forEach(p=>{p.y=Math.random()*H;p.vy=0;p.vr=0;p.vx=0});
  if(!silent)fanfare();
}
function stepParty(dt){
  for(const p of party){p.y+=p.vy*dt;p.x+=p.vx*dt+Math.sin(p.ph+p.y*.02)*20*dt;p.rot+=p.vr*dt;if(p.y>H+40&&p.money!==null){p.y=-30;p.x=Math.random()*W}}
}
function drawParty(t){
  for(const p of party){
    if(p.money){ctx.save();ctx.globalAlpha=.95;drawItem({t:p.t,x:p.x,y:p.y,r:TYPES[p.t].r,rot:p.rot,ph:p.ph},t);ctx.restore()}
    else{ctx.save();ctx.translate(p.x,p.y);ctx.rotate(p.rot);ctx.scale(1,Math.cos(t*6+p.ph));ctx.fillStyle=p.c;ctx.fillRect(-p.w/2,-p.h/2,p.w,p.h);ctx.restore()}
  }
}
let actx=null;
function fanfare(){
  try{
    actx=actx||new (window.AudioContext||window.webkitAudioContext)();
    const notes=[[523.25,0,.14],[659.25,.14,.14],[783.99,.28,.14],[1046.5,.42,.5],[783.99,.95,.14],[1046.5,1.1,.7]];
    const now=actx.currentTime+.05;
    for(const [f,s,d] of notes){
      const o=actx.createOscillator(),g=actx.createGain();
      o.type='triangle';o.frequency.value=f;
      g.gain.setValueAtTime(0,now+s);g.gain.linearRampToValueAtTime(.18,now+s+.02);g.gain.exponentialRampToValueAtTime(.001,now+s+d);
      o.connect(g).connect(actx.destination);o.start(now+s);o.stop(now+s+d+.05);
    }
  }catch(e){}
}
function jackpotSound(){
  try{
    actx=actx||new (window.AudioContext||window.webkitAudioContext)();
    const now=actx.currentTime+.02;
    const run=[523.25,659.25,783.99,1046.5,1318.5,1568,2093];
    run.forEach((f,i)=>{
      const o=actx.createOscillator(),g=actx.createGain(),s=now+i*.085;
      o.type='square';o.frequency.value=f;
      g.gain.setValueAtTime(0,s);g.gain.linearRampToValueAtTime(.09,s+.01);g.gain.exponentialRampToValueAtTime(.001,s+.18);
      o.connect(g).connect(actx.destination);o.start(s);o.stop(s+.2);
    });
    [523.25,659.25,783.99,1046.5].forEach(f=>{
      const o=actx.createOscillator(),g=actx.createGain(),s=now+.65;
      o.type='triangle';o.frequency.value=f;
      g.gain.setValueAtTime(0,s);g.gain.linearRampToValueAtTime(.12,s+.03);g.gain.exponentialRampToValueAtTime(.001,s+1.2);
      o.connect(g).connect(actx.destination);o.start(s);o.stop(s+1.25);
    });
    for(let i=0;i<14;i++){
      const o=actx.createOscillator(),g=actx.createGain(),s=now+.2+i*.095;
      o.type='sine';o.frequency.value=1800+Math.random()*1800;
      g.gain.setValueAtTime(0,s);g.gain.linearRampToValueAtTime(.07,s+.005);g.gain.exponentialRampToValueAtTime(.001,s+.12);
      o.connect(g).connect(actx.destination);o.start(s);o.stop(s+.15);
    }
  }catch(e){}
}
function showBest(){$('bestStat').textContent=bestTime?bestTime.toFixed(1)+'秒':'—'}

/* kind: coin | bill | bundle | rock | toy | purse */
const TYPES={
  c1:   {kind:'coin',r:14,val:1,wt:1.0,face:'#dfe3e8',edge:'#9aa1aa',txt:'1',ink:'#5b6470'},
  c5:   {kind:'coin',r:15,val:5,wt:1.0,face:'#d9b24a',edge:'#9c7a22',txt:'5',ink:'#6b4f12',hole:true},
  c10:  {kind:'coin',r:16,val:10,wt:1.1,face:'#c27a48',edge:'#8a4d26',txt:'10',ink:'#5c3015'},
  c50:  {kind:'coin',r:16,val:50,wt:1.1,face:'#cfd4da',edge:'#8d949c',txt:'50',ink:'#4d5560',hole:true},
  c100: {kind:'coin',r:17,val:100,wt:1.2,face:'#c8ced6',edge:'#858c96',txt:'100',ink:'#3f4752'},
  c500: {kind:'coin',r:19,val:500,wt:1.3,face:'#e0c46c',edge:'#a6872e',txt:'500',ink:'#5e4710'},
  b1k:  {kind:'bill',r:25,w:70,h:34,val:1000,wt:2.4,color:'#9cc0d6',ink:'#24516b',txt:'1000'},
  b10k: {kind:'bill',r:27,w:76,h:36,val:10000,wt:2.8,color:'#d9c595',ink:'#5d4620',txt:'10000'},
  bundle:{kind:'bundle',r:36,w:78,h:40,val:30000,wt:4.8,color:'#d9c595',ink:'#5d4620'},
  rock: {kind:'rock',r:34,val:0,wt:5.5},
  toy:  {kind:'bill',r:30,w:70,h:34,val:0,wt:3.2,color:'#a8d8a0',ink:'#2f5a2a',txt:'子ども銀行',toy:true},
  purse:{kind:'purse',r:24,val:null,wt:2.2}
};
const JUNK=new Set(['rock','toy']);

let S;
const TIME=50;
const GOALS=[3000];
let round=0,totalUsed=0;
function newGame(){S={goal:GOALS[round],money:0,time:TIME,scis:Infinity,running:false}}
/* Build the field so the valuables on it are always worth well over the goal. */
function layout(g){
  const items=[],pool=[];
  const add=(t,n)=>{for(let i=0;i<n;i++)pool.push(t)};
  add('c1',3);add('c5',2);add('c10',3);add('c50',2);add('c100',2);add('c500',2);
  add('b1k',4);add('b10k',1);add('bundle',g>=15000?1:0);
  add('rock',6);add('toy',2);add('purse',2);
  const sum=()=>pool.reduce((s,t)=>s+(TYPES[t].val||0),0);
  while(sum()<g*1.6){
    if(g-sum()>40000&&pool.filter(t=>t==='bundle').length<4)pool.push('bundle');
    else pool.push('b1k');
  }
  const deep=new Set(['b10k','bundle']);
  pool.sort((a,b)=>TYPES[b].r-TYPES[a].r); // place big things first so they fit
  for(const t of pool){
    const T=TYPES[t];
    for(let k=0;k<400;k++){
      const minY=deep.has(t)?350:160;
      let x=24+T.r+Math.random()*(W-48-2*T.r);
      if(t==='b10k')x=170+Math.random()*460;
      const y=minY+Math.random()*(H-14-T.r-minY);
      if(items.every(o=>Math.hypot(o.x-x,o.y-y)>o.r+T.r+(k<300?8:2))){
        items.push({t,x,y,r:T.r,rot:(Math.random()-.5)*(T.kind==='bill'?.6:.3),ph:Math.random()*6});break;
      }
    }
  }
  return items;
}
function startGame(){round=0;totalUsed=0;S=null;beginRound()}
function beginRound(){
  newGame();
  S.items=layout(S.goal);
  S.hook={a:0,dir:1,len:30,state:'swing',held:null};
  S.pops=[];S.running=true;S.last=performance.now();
  party=[];$('ov').classList.remove('win');
  $('pauseBtn').textContent='一時停止';
  $('ov').hidden=true;hud();
}
function hud(){
  $('score').textContent=yen(S.money);$('goal').textContent=yen(S.goal);
  $('prog').style.width=Math.min(100,S.money/S.goal*100)+'%';
  $('time').textContent=Math.ceil(S.time);
  $('timeBox').classList.toggle('low',S.time<=10);
  $('pauseBtn').disabled=!S.running;
}
function fire(){if(S&&S.running&&!S.paused&&S.hook.state==='swing')S.hook.state='out'}
/* キャンセル: drop what the hook holds, or pull the hook back right away if it is still going down */
function cut(){
  const h=S&&S.hook;if(!S||!S.running||S.paused||S.jackpot)return;
  if(h.held){h.held=null;hud()}
  else if(h.state==='out'){h.state='back'}
}
function tip(){const h=S.hook;return{x:PX+Math.sin(h.a)*h.len,y:PY+Math.cos(h.a)*h.len}}

function startJackpot(o){
  const h=S.hook;
  h.state='jackpot';
  o.x=tip().x;o.y=tip().y;
  S.jackpot={t:0};
  collect(o);
  party=[];
  celebrate(true);
  jackpotSound();
  hud();
}
function update(dt){
  const h=S.hook;
  if(S.jackpot){
    S.jackpot.t+=dt;
    for(const p of S.pops){p.life-=dt*.8;p.y-=30*dt}
    S.pops=S.pops.filter(p=>p.life>0);
    if(S.jackpot.t>=1.9){h.held=null;hud();return endGame(true)}
    return;
  }
  S.time-=dt;
  if(h.state==='swing'){
    h.a+=h.dir*dt*1.8;
    if(h.a>1.25){h.a=1.25;h.dir=-1}if(h.a<-1.25){h.a=-1.25;h.dir=1}
  }else if(h.state==='out'){
    h.len+=520*dt;const p=tip();
    if(p.x<0||p.x>W||p.y>H)h.state='back';
    for(let i=0;i<S.items.length;i++){
      const o=S.items[i];
      if(Math.hypot(o.x-p.x,o.y-p.y)<o.r){h.held=o;S.items.splice(i,1);h.state='back';if(o.t==='b10k')startJackpot(o);break}
    }
  }else if(h.state==='back'){
    const sp=h.held?560/TYPES[h.held.t].wt:620;
    h.len-=sp*dt;
    if(h.held){const p=tip();h.held.x=p.x+Math.sin(h.a)*h.held.r*.8;h.held.y=p.y+Math.cos(h.a)*h.held.r*.8}
    if(h.len<=30){
      h.len=30;h.state='swing';
      if(h.held){collect(h.held);h.held=null;if(S.money>=S.goal){hud();return endGame(true)}}
    }
  }
  for(const p of S.pops){p.life-=dt*.8;p.y-=30*dt}
  S.pops=S.pops.filter(p=>p.life>0);
  const left=S.items.some(o=>!JUNK.has(o.t));
  if(S.time<=0||(!left&&h.state==='swing'&&!h.held)){S.time=Math.max(0,S.time);hud();return endGame(false)}
  hud();
}
function collect(o){
  let v,txt,c='#f2c14e';
  if(o.t==='purse'){
    const r=Math.random();
    if(r<.2){S.time+=10;txt='時間 +10秒';c='#9fb4f2';v=0}
    else v=[200,300,500,1000,2000][Math.floor(Math.random()*5)];
  }else v=TYPES[o.t].val;
  if(v){S.money+=v;txt='+'+yen(v);if(v<100)c='#dfe3e8'}
  else if(!txt){txt='¥0';c='#8d8292'}
  S.pops.push({x:PX+60,y:PY-18,txt,life:1,c});
}
function endGame(ok){
  S.running=false;
  S.paused=false;$('pauseBtn').disabled=true;$('pauseBtn').textContent='一時停止';
  $('legend').hidden=true;$('ov').hidden=false;
  if(ok){
    const used=Math.max(0,TIME-S.time);totalUsed+=used;
    if(round<GOALS.length-1){
      $('ovTitle').textContent='ラウンド'+(round+1)+' クリア！';
      $('ovText').textContent=used.toFixed(1)+' 秒で '+yen(S.money)+' 集めました。次のラウンド'+(round+2)+'の目標は '+yen(GOALS[round+1])+' です。';
      $('ovBtn').textContent='ラウンド'+(round+2)+'へ';
      $('ovBtn').onclick=()=>{round++;beginRound()};
    }else{
      const isBest=!bestTime||totalUsed<bestTime;
      if(isBest){bestTime=totalUsed;try{localStorage.setItem('yen-best-2r',totalUsed)}catch(e){}}
      showBest();
      $('ovTitle').textContent='おめでとうございます！';
      $('ovText').textContent='目標の '+yen(S.goal)+' を達成しました！ '+totalUsed.toFixed(1)+' 秒で '+yen(S.money)+' 集めました。'+(isBest?'ベストタイム更新！':'');
      $('ovBtn').textContent='次のステージへ';
      $('ovBtn').onclick=startGame;
      $('ov').classList.add('win');celebrate();
    }
  }else{
    $('ovTitle').textContent=S.time<=0?'タイムアップ':'お金が尽きました';
    $('ovText').textContent='目標 '+yen(S.goal)+' まであと '+yen(S.goal-S.money)+'でした。もう一回挑戦しよう！';
    $('ovBtn').textContent='もう一回';
    $('ovBtn').onclick=startGame;
  }
  $('ovBtn').focus();
}

/* ---------- pause ---------- */
function pauseGame(){
  if(!S||!S.running||S.paused)return;
  S.paused=true;
  $('pauseBtn').textContent='再開';
  $('ovTitle').textContent='一時停止中';
  $('ovText').textContent='ゲームは止まっています。再開を押すと続きから始まります。';
  $('legend').hidden=true;
  $('ovBtn').textContent='再開';
  $('ovBtn').onclick=resumeGame;
  $('ov').classList.remove('win');
  $('ov').hidden=false;
  $('ovBtn').focus();
}
function resumeGame(){
  if(!S||!S.running||!S.paused)return;
  S.paused=false;
  S.last=performance.now();
  $('pauseBtn').textContent='一時停止';
  $('ov').hidden=true;
}
function togglePause(){
  if(!S||!S.running)return;
  if(S.paused){resumeGame()}else{pauseGame()}
}

/* ---------- drawing ---------- */
function drawJackpot(){
  const j=S.jackpot,k=Math.min(1,j.t/.25);
  ctx.save();
  ctx.fillStyle='rgba(20,10,27,'+(0.6*k)+')';ctx.fillRect(0,0,W,H);
  ctx.translate(W/2,250);
  if(!reduce)ctx.rotate(j.t*.8);
  for(let i=0;i<16;i++){
    ctx.rotate(Math.PI*2/16);
    ctx.fillStyle=i%2?'rgba(242,193,78,0.30)':'rgba(255,255,255,0.12)';
    ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(900,-45);ctx.lineTo(900,45);ctx.closePath();ctx.fill();
  }
  ctx.restore();
  if(!reduce&&j.t<.25){ctx.fillStyle='rgba(255,255,255,'+(0.8*(1-j.t/.25))+')';ctx.fillRect(0,0,W,H)}
  const pop=Math.min(1,j.t/.3),ease=1+Math.sin(pop*Math.PI)*.25*(1-pop+.2);
  const pulse=reduce?1:1+Math.sin(j.t*14)*.05;
  const sc=(reduce?1:pop*ease)*pulse;
  ctx.save();ctx.translate(W/2,235);ctx.scale(sc,sc);
  ctx.textAlign='center';ctx.textBaseline='middle';ctx.lineJoin='round';
  ctx.font='92px '+DISP;ctx.lineWidth=12;ctx.strokeStyle='#a9771f';ctx.strokeText('JACKPOT!',0,0);
  ctx.fillStyle='#f2c14e';ctx.fillText('JACKPOT!',0,0);
  ctx.font='46px '+DISP;ctx.lineWidth=8;ctx.strokeStyle='#2b1832';ctx.strokeText('¥10,000',0,86);
  ctx.fillStyle='#f4e6c4';ctx.fillText('¥10,000',0,86);
  ctx.restore();
}
function rr(x,y,w,h,r){ctx.beginPath();ctx.roundRect(x,y,w,h,r)}
function txt(s,x,y,size,font,color){ctx.fillStyle=color;ctx.font=`${size}px ${font}`;ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(s,x,y)}
const DISP='"Dela Gothic One",sans-serif',DOT='"DotGothic16",monospace';

function drawCoin(o,T){
  const r=T.r;
  ctx.fillStyle='#00000045';ctx.beginPath();ctx.arc(2,3,r,0,7);ctx.fill();
  ctx.fillStyle=T.edge;ctx.beginPath();ctx.arc(0,0,r,0,7);ctx.fill();
  ctx.fillStyle=T.face;ctx.beginPath();ctx.arc(0,0,r-2.5,0,7);ctx.fill();
  ctx.strokeStyle=T.edge;ctx.lineWidth=1;ctx.beginPath();ctx.arc(0,0,r-5,0,7);ctx.stroke();
  if(T.hole){
    ctx.fillStyle='#2b1832';ctx.beginPath();ctx.arc(0,0,r*.28,0,7);ctx.fill();
    txt(T.txt,0,r*.6,Math.round(r*.55),DISP,T.ink);
  }else txt(T.txt,0,1,Math.round(r*(T.txt.length>2?.62:.85)),DISP,T.ink);
  ctx.fillStyle='#ffffff55';ctx.beginPath();ctx.ellipse(-r*.35,-r*.4,r*.28,r*.14,-.6,0,7);ctx.fill();
}
function billBody(T,w,h){
  rr(-w/2,-h/2,w,h,3);ctx.fillStyle=T.color;ctx.fill();
  ctx.strokeStyle=T.ink+'88';ctx.lineWidth=1;ctx.stroke();
  rr(-w/2+3,-h/2+3,w-6,h-6,2);ctx.strokeStyle=T.ink+'55';ctx.stroke();
  // portrait oval + watermark circle
  ctx.fillStyle=T.ink+'40';ctx.beginPath();ctx.ellipse(w*.24,0,h*.26,h*.32,0,0,7);ctx.fill();
  ctx.strokeStyle=T.ink+'50';ctx.beginPath();ctx.arc(-w*.3,0,h*.22,0,7);ctx.stroke();
}
function drawBill(o,T,t){
  const w=T.w,h=T.h;
  ctx.fillStyle='#00000040';rr(-w/2+2,-h/2+3,w,h,3);ctx.fill();
  billBody(T,w,h);
  if(T.toy){
    txt('子ども銀行',-w*.06,-h*.12,Math.round(h*.24),DISP,T.ink);
    txt('ニセ札',-w*.06,h*.22,Math.round(h*.22),DOT,T.ink);
  }else{
    txt(T.txt,-w*.08,-h*.06,Math.round(h*(T.txt.length>4?.34:.4)),DISP,T.ink);
    txt('日本銀行券',-w*.08,h*.3,Math.round(h*.19),DOT,T.ink);
  }
  if(o.t==='b10k'&&!reduce){
    const s=(Math.sin(t*3+o.ph)+1)/2;ctx.fillStyle=`rgba(255,255,255,${.35+s*.6})`;
    star(w/2-4,-h/2+2,3+s*4);star(-w/2+6,h/2-3,2+(1-s)*3);
  }
}
function drawBundle(o,T,t){
  const w=T.w,h=T.h;
  ctx.fillStyle='#00000045';rr(-w/2+3,-h/2+5,w,h+8,4);ctx.fill();
  for(let i=4;i>=1;i--){ctx.save();ctx.translate(0,i*2.2);rr(-w/2,-h/2,w,h,3);ctx.fillStyle=i%2?'#cbb682':'#e3d2a6';ctx.fill();ctx.strokeStyle='#5d462060';ctx.stroke();ctx.restore()}
  billBody(T,w,h);
  txt('10000',-w*.08,-h*.12,Math.round(h*.32),DISP,T.ink);
  // paper band
  ctx.fillStyle='#f4e6c4';ctx.fillRect(-7,-h/2-1,14,h+12);
  ctx.strokeStyle='#d94a3d';ctx.lineWidth=1.5;ctx.strokeRect(-7,-h/2-1,14,h+12);
  ctx.save();ctx.translate(0,4);ctx.rotate(Math.PI/2);txt('¥30,000',0,0,10,DOT,'#d94a3d');ctx.restore();
  if(!reduce){const s=(Math.sin(t*2.4+o.ph)+1)/2;ctx.fillStyle=`rgba(255,240,180,${.3+s*.6})`;star(w/2-2,-h/2,3+s*4)}
}
function drawRock(o,T){
  const r=T.r,n=9;
  ctx.fillStyle='#00000045';ctx.beginPath();
  for(let i=0;i<n;i++){const a=i/n*Math.PI*2,k=.82+((i*37+Math.floor(o.ph*10))%7)/30;ctx.lineTo(2+Math.cos(a)*r*k,4+Math.sin(a)*r*k*.85)}ctx.fill();
  const g=ctx.createLinearGradient(-r,-r,r,r);g.addColorStop(0,'#8a7f8f');g.addColorStop(1,'#4b4250');
  ctx.fillStyle=g;ctx.beginPath();
  for(let i=0;i<n;i++){const a=i/n*Math.PI*2,k=.82+((i*37+Math.floor(o.ph*10))%7)/30;ctx.lineTo(Math.cos(a)*r*k,Math.sin(a)*r*k*.85)}
  ctx.closePath();ctx.fill();ctx.strokeStyle='#2b1832';ctx.lineWidth=1.5;ctx.stroke();
  ctx.strokeStyle='#ffffff25';ctx.beginPath();ctx.moveTo(-r*.4,-r*.3);ctx.lineTo(-r*.05,-r*.05);ctx.lineTo(r*.2,-r*.35);ctx.stroke();
}
function drawPurse(o,T){
  const r=T.r;
  ctx.fillStyle='#00000045';ctx.beginPath();ctx.ellipse(2,8,r,r*.8,0,0,7);ctx.fill();
  ctx.fillStyle='#c0503f';ctx.beginPath();ctx.moveTo(-r,0);ctx.quadraticCurveTo(-r,r*.95,0,r*.95);ctx.quadraticCurveTo(r,r*.95,r,0);ctx.closePath();ctx.fill();
  ctx.fillStyle='#d8685a';ctx.beginPath();ctx.moveTo(-r,0);ctx.quadraticCurveTo(0,-r*.55,r,0);ctx.closePath();ctx.fill();
  ctx.strokeStyle='#e0c46c';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(-r,0);ctx.quadraticCurveTo(0,-r*.55,r,0);ctx.stroke();
  ctx.fillStyle='#e0c46c';ctx.beginPath();ctx.arc(-4,-r*.32,4,0,7);ctx.arc(4,-r*.32,4,0,7);ctx.fill();
  txt('?',0,r*.42,18,DISP,'#f4e6c4');
}
function star(x,y,r){ctx.beginPath();for(let i=0;i<8;i++){const a=i*Math.PI/4,q=i%2?r*.3:r;ctx.lineTo(x+Math.cos(a)*q,y+Math.sin(a)*q)}ctx.closePath();ctx.fill()}
function drawItem(o,t){
  const T=TYPES[o.t];ctx.save();ctx.translate(o.x,o.y);ctx.rotate(o.rot);ctx.lineWidth=1.2;
  ({coin:drawCoin,bill:drawBill,bundle:drawBundle,rock:drawRock,purse:drawPurse})[T.kind](o,T,t);
  ctx.restore();
}
let speck=null;
function bg(){
  if(!speck){speck=[];for(let i=0;i<140;i++)speck.push([Math.random()*W,GROUND+Math.random()*(H-GROUND),Math.random()*2+.5,Math.random()])}
  const g=ctx.createLinearGradient(0,0,0,GROUND);g.addColorStop(0,'#3a2147');g.addColorStop(1,'#5a2f52');
  ctx.fillStyle=g;ctx.fillRect(0,0,W,GROUND);
  const d=ctx.createLinearGradient(0,GROUND,0,H);d.addColorStop(0,'#3b2140');d.addColorStop(1,'#1f1026');
  ctx.fillStyle=d;ctx.fillRect(0,GROUND,W,H-GROUND);
  for(const s of speck){ctx.fillStyle=s[3]>.8?'#f2c14e30':'#ffffff10';ctx.fillRect(s[0],s[1],s[2],s[2])}
  ctx.strokeStyle='#ffffff0d';ctx.lineWidth=1;
  for(let y=GROUND+60;y<H;y+=90){ctx.beginPath();for(let x=0;x<=W;x+=20)ctx.lineTo(x,y+Math.sin(x*.03+y)*4);ctx.stroke()}
  ctx.fillStyle='#f4e6c4';ctx.fillRect(0,GROUND-4,W,4);
  ctx.fillStyle='#d94a3d';for(let x=0;x<W;x+=24)ctx.fillRect(x,GROUND-4,12,4);
  for(let i=0;i<W/40;i++){ctx.fillStyle=i%2?'#f4e6c4':'#d94a3d';ctx.fillRect(i*40,0,40,26);ctx.beginPath();ctx.arc(i*40+20,26,20,0,Math.PI);ctx.fill()}
  ctx.fillStyle='#2b1832';rr(30,56,170,40,6);ctx.fill();ctx.strokeStyle='#f2c14e';ctx.lineWidth=2;ctx.stroke();
  txt('お金を掘ろう',115,77,18,DISP,'#f2c14e');
  ctx.fillStyle='#2b1832';rr(W-210,56,180,40,6);ctx.fill();ctx.strokeStyle='#7fd3b5';ctx.stroke();
  txt('目標 '+yen(S?S.goal:goal),W-120,77,16,DOT,'#7fd3b5');
}
function drawMachine(){
  ctx.fillStyle='#2b1832';rr(PX-36,PY-46,72,40,8);ctx.fill();ctx.strokeStyle='#f2c14e';ctx.lineWidth=2;ctx.stroke();
  const h=S&&S.hook,spin=h?h.len*.05:0;
  ctx.save();ctx.translate(PX,PY-26);ctx.rotate(spin);ctx.strokeStyle='#f4e6c4';ctx.lineWidth=2;
  ctx.beginPath();ctx.arc(0,0,12,0,7);ctx.stroke();
  for(let i=0;i<4;i++){ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(Math.cos(i*1.57)*12,Math.sin(i*1.57)*12);ctx.stroke()}
  ctx.restore();
  ctx.fillStyle='#f2c14e';ctx.beginPath();ctx.arc(PX,PY,5,0,7);ctx.fill();
}
function drawHook(){
  const h=S.hook,p=tip();
  ctx.strokeStyle='#f4e6c4';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(PX,PY);ctx.lineTo(p.x,p.y);ctx.stroke();
  ctx.save();ctx.translate(p.x,p.y);ctx.rotate(-h.a);
  ctx.strokeStyle='#f2c14e';ctx.lineWidth=4;ctx.lineCap='round';
  const open=h.held?.25:.7;
  ctx.beginPath();ctx.moveTo(0,0);ctx.quadraticCurveTo(-14*open-6,8,-6,20);ctx.stroke();
  ctx.beginPath();ctx.moveTo(0,0);ctx.quadraticCurveTo(14*open+6,8,6,20);ctx.stroke();
  ctx.fillStyle='#f2c14e';ctx.fillRect(-6,-4,12,6);
  ctx.restore();
}
const DEMO=[['b1k',170,250],['bundle',570,410],['c500',650,240],['c100',300,420],['rock',120,430],['purse',450,300],['b10k',360,200],['c5',520,200],['c10',700,340]];
function draw(t){
  const sc=cv.width/W;ctx.setTransform(sc,0,0,sc,0,0);
  bg();
  if(S&&S.items){
    for(const o of S.items)drawItem(o,t);
    drawMachine();drawHook();
    if(S.hook.held)drawItem(S.hook.held,t);
    for(const p of S.pops){ctx.globalAlpha=Math.max(0,p.life);ctx.fillStyle=p.c;ctx.font='26px '+DISP;ctx.textAlign='left';ctx.textBaseline='middle';ctx.fillText(p.txt,p.x,p.y);ctx.globalAlpha=1}
    if(S.jackpot)drawJackpot();
  }else{
    drawMachine();
    for(const [tt,x,y] of DEMO)drawItem({t:tt,x,y,r:TYPES[tt].r,rot:(x%7-3)*.08,ph:x},t);
  }
  if(party.length)drawParty(t);
}
let lastF=0;
function loop(now){
  if(S&&S.running&&!S.paused){const dt=Math.min(.05,(now-S.last)/1000);S.last=now;update(dt)}
  const fdt=Math.min(.05,(now-(lastF||now))/1000);lastF=now;
  if(party.length)stepParty(fdt);
  draw(now/1000);requestAnimationFrame(loop);
}

addEventListener('keydown',e=>{
  if(!S||!S.running)return;
  if(e.code==='KeyP'||e.code==='Escape'){e.preventDefault();togglePause();return}
  if(S.paused)return;
  if(e.code==='ArrowDown'||e.code==='Space'){e.preventDefault();fire()}
  if(e.code==='ArrowUp'){e.preventDefault();cut()}
});
cv.addEventListener('pointerdown',fire);
$('fireBtn').onclick=()=>{if(!S||!S.running){if(!$('ov').hidden)$('ovBtn').click()}else fire()};
$('pauseBtn').onclick=function(){togglePause();$('pauseBtn').blur()};
function restartGame(){
  if(party.length)party=[];
  startGame();
  $('restartBtn').blur();
}
$('restartBtn').onclick=restartGame;
addEventListener('keydown',e=>{if(e.code==='KeyR'&&!e.repeat){e.preventDefault();restartGame()}});
document.addEventListener('visibilitychange',()=>{if(document.hidden)pauseGame()});
let resetTimer=null;
$('resetBest').onclick=function(){
  const btn=$('resetBest');
  if(!btn.classList.contains('sure')){
    btn.classList.add('sure');btn.textContent='もう一度押す';
    resetTimer=setTimeout(()=>{btn.classList.remove('sure');btn.textContent='リセット'},3000);
    return;
  }
  clearTimeout(resetTimer);
  bestTime=0;try{localStorage.removeItem('yen-best-2r')}catch(e){}
  showBest();btn.classList.remove('sure');btn.textContent='リセット';
};
$('ovBtn').onclick=startGame;

function start(){newGame();hud();showBest();fit();requestAnimationFrame(loop)}
start();
startGame();
})();
