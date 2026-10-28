(function(){
  var POOL=[
    {w:'Cow',e:'🐄'},{w:'Cat',e:'🐱'},{w:'Mouse',e:'🐭'},{w:'Dog',e:'🐶'},
    {w:'Chicken',e:'🐔'},{w:'Pig',e:'🐷'},{w:'Horse',e:'🐴'},{w:'Sheep',e:'🐑'},
    {w:'Duck',e:'🦆'},{w:'Rabbit',e:'🐰'}
  ];
  var C=163.36,q=[],idx=0,score=0,locked=false,voice=null,muted=false,tm=null,pressT=null,longDone=false;
  var $=function(id){return document.getElementById(id)};
  var ring=$('ring'),menu=$('menu');

  function pickVoice(){try{var vs=speechSynthesis.getVoices();voice=vs.filter(function(v){return /^en/i.test(v.lang)})[0]||null;}catch(e){}}
  try{pickVoice();speechSynthesis.onvoiceschanged=pickVoice;}catch(e){}
  function say(t,force){
    if(muted&&!force)return;
    try{speechSynthesis.cancel();var u=new SpeechSynthesisUtterance(t);u.lang='en-US';u.rate=.8;u.pitch=1.1;if(voice)u.voice=voice;speechSynthesis.speak(u);}catch(e){}
  }
  function sayJa(t){
    if(muted)return;
    try{var u=new SpeechSynthesisUtterance(t);u.lang='ja-JP';u.rate=.9;speechSynthesis.speak(u);}catch(e){}
  }
  function shuffle(a){a=a.slice();for(var i=a.length-1;i>0;i--){var j=Math.floor(Math.random()*(i+1));var t=a[i];a[i]=a[j];a[j]=t;}return a;}

  function dots(){
    var h='';for(var i=0;i<5;i++){var c='dot';if(q[i]&&q[i].res)c+=' '+q[i].res;else if(i===idx)c+=' now';h+='<div class="'+c+'"></div>';}
    $('dots').innerHTML=h;
  }

  function stopTimer(){
    if(tm){clearInterval(tm);tm=null;
      var cs=parseFloat(getComputedStyle(ring).strokeDashoffset)||0;
      ring.style.transition='none';ring.style.strokeDashoffset=cs;}
  }
  function startTimer(cur){
    stopTimer();var n=5;
    $('tnum').textContent=n;$('timer').classList.remove('low');
    ring.style.transition='none';ring.style.strokeDashoffset=0;void ring.getBoundingClientRect();
    ring.style.transition='stroke-dashoffset 5s linear';ring.style.strokeDashoffset=C;
    tm=setInterval(function(){
      n--;$('tnum').textContent=Math.max(n,0);$('timer').classList.toggle('low',n<=2);
      if(n<=0){stopTimer();timeUp(cur);}
    },1000);
  }
  function timeUp(cur){
    if(locked)return;locked=true;
    $('opts').querySelectorAll('.opt').forEach(function(b){b.disabled=true;if(b.textContent===cur.w)b.classList.add('ok');});
    q[idx].res='bad';say('Time is up! It is a '+cur.w);dots();
    setTimeout(next,1900);
  }
  function next(){idx++;if(idx>=5)finish();else show();}

  function start(){
    stopTimer();
    q=shuffle(POOL).slice(0,5).map(function(a){return {a:a,res:''}});
    idx=0;score=0;locked=false;
    $('end').classList.add('hidden');$('quiz').style.display='contents';
    show();
  }

  function show(){
    locked=false;dots();
    var cur=q[idx].a;
    startTimer(cur);
    $('emoji').textContent=cur.e;
    var pic=$('pic');pic.classList.remove('pop');void pic.offsetWidth;pic.classList.add('pop');
    var wrong=shuffle(POOL.filter(function(p){return p.w!==cur.w})).slice(0,2);
    var box=$('opts');box.innerHTML='';
    shuffle([cur].concat(wrong)).forEach(function(o){
      var b=document.createElement('button');b.className='opt';b.textContent=o.w;
      b.addEventListener('click',function(){choose(b,o,cur)});
      box.appendChild(b);
    });
    setTimeout(function(){say(cur.w)},250);
  }

  function choose(btn,o,cur){
    if(locked)return;locked=true;stopTimer();
    $('opts').querySelectorAll('.opt').forEach(function(b){b.disabled=true;if(b.textContent===cur.w)b.classList.add('ok');});
    if(o.w===cur.w){score++;q[idx].res='ok';say('Correct! '+cur.w);}
    else{btn.classList.add('bad');q[idx].res='bad';say('It is a '+cur.w);}
    dots();
    setTimeout(next,1900);
  }

  function confetti(){
    var em=['🎉','⭐','🎊','✨','🌟'];
    for(var i=0;i<28;i++){(function(i){
      var s=document.createElement('div');s.className='fall';s.textContent=em[i%em.length];
      s.style.left=Math.random()*96+'vw';s.style.animationDuration=(2.5+Math.random()*2.5)+'s';s.style.animationDelay=(Math.random()*1.2)+'s';
      document.body.appendChild(s);setTimeout(function(){s.remove()},7000);
    })(i);}
  }

  function finish(){
    dots();
    $('quiz').style.display='none';$('end').classList.remove('hidden');
    if(score===5){
      $('trophy').textContent='🏆';
      $('endTitle').textContent='Congratulations!';
      $('endMsg').textContent='おめでとう！5問すべて正解したよ (5 / 5)';
      confetti();say('Congratulations!');sayJa('おめでとう。全問正解です。');
    }else{
      $('trophy').textContent='🌱';
      $('endTitle').textContent='Failed';
      $('endMsg').textContent='5問中'+score+'問正解。全問正解でトロフィーがもらえるよ。もう一度がんばろう！';
      say('Failed.');sayJa('もう一度がんばろう。');
    }
  }

  function playSound(){if(q[idx])say(q[idx].a.w,true)}

  /* context menu */
  function openMenu(x,y){
    menu.classList.remove('hidden');
    var r=menu.getBoundingClientRect();
    menu.style.left=Math.max(8,Math.min(x,innerWidth-r.width-8))+'px';
    menu.style.top=Math.max(8,Math.min(y,innerHeight-r.height-8))+'px';
    $('menuBtn').setAttribute('aria-expanded','true');
    var f=menu.querySelector('button');if(f)f.focus();
  }
  function closeMenu(){menu.classList.add('hidden');$('menuBtn').setAttribute('aria-expanded','false');}
  function toast(msg){var t=$('toast');t.textContent=msg;t.classList.remove('hidden');clearTimeout(toast.h);toast.h=setTimeout(function(){t.classList.add('hidden')},4000);}

  $('menuBtn').addEventListener('click',function(e){
    e.stopPropagation();
    if(!menu.classList.contains('hidden')){closeMenu();return;}
    var r=this.getBoundingClientRect();openMenu(r.right-210,r.bottom+8);
  });
  document.addEventListener('contextmenu',function(e){e.preventDefault();openMenu(e.clientX,e.clientY);});
  document.addEventListener('click',function(e){if(!menu.contains(e.target))closeMenu();});
  document.addEventListener('keydown',function(e){if(e.key==='Escape')closeMenu();});
  menu.addEventListener('click',function(e){
    var b=e.target.closest('button');if(!b)return;
    var act=b.getAttribute('data-act');closeMenu();
    if(act==='hear')playSound();
    else if(act==='mute'){muted=!muted;$('muteLbl').textContent=muted?'おとを ONにする':'おとを OFFにする';if(muted){try{speechSynthesis.cancel()}catch(x){}}toast(muted?'おとを けしたよ':'おとを ONにしたよ');}
    else if(act==='restart'){try{speechSynthesis.cancel()}catch(x){}start();}
    else if(act==='help')toast('絵をタップして音を聞こう。5秒いないに正しい答えを選んでね。5問ぜんぶ正解でトロフィー！');
  });

  var pic=$('pic');
  pic.addEventListener('click',function(){if(longDone){longDone=false;return;}playSound();});
  pic.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();playSound();}});
  pic.addEventListener('pointerdown',function(e){
    if(e.pointerType!=='touch')return;
    clearTimeout(pressT);
    pressT=setTimeout(function(){longDone=true;openMenu(e.clientX,e.clientY);},550);
  });
  ['pointerup','pointerleave','pointercancel'].forEach(function(n){pic.addEventListener(n,function(){clearTimeout(pressT);});});

  $('again').addEventListener('click',start);
  start();
})();
