(() => {
  const TOTAL_TIME = 30;
  const NEED = 3;
  const FLIP_MS = 600;
  const HOLD_MS = 700;      // how long a tapped card stays face-up before turning back
  const MAX_TAPS = 7;
  const ROT = { up: 0, right: 90, down: 180, left: 270 };
  const LABEL = { up: "上", right: "右", down: "下", left: "左" };
  const ARROW_SVG = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="M50 8 L88 50 H64 V92 H36 V50 H12 Z"/></svg>';

  const $ = id => document.getElementById(id);
  const board = $("board"), timeVal = $("timeVal"), timeBar = $("timeBar"), timeMeter = $("timeMeter");
  const scoreVal = $("scoreVal"), pips = document.querySelectorAll("#scorePips b");
  const tapVal = $("tapVal"), tapPips = document.querySelectorAll("#tapPips b"), tapMeter = $("tapMeter");

  let deck = [], score = 0, taps = 0, busy = false, state = "idle", startAt = 0, raf = 0, endTimer = 0;
  let flipTimers = [];
  let pendingBad = null, pausedElapsed = 0;

  /* ---------- deck ---------- */
  function shuffle(a) {
    for (let i = a.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [a[i], a[j]] = [a[j], a[i]];
    }
    return a;
  }
  function makeDeck() {
    const others = ["down", "left", "right"];
    const wrong = shuffle([...others, ...others, others[Math.floor(Math.random() * 3)]]); // 7, all three kinds present
    return shuffle([...Array(5).fill("up"), ...wrong]);
  }

  function buildBoard() {
    board.innerHTML = "";
    deck.forEach((dir, i) => {
      const b = document.createElement("button");
      b.type = "button";
      b.className = "card";
      b.id = "card-" + i;
      b.setAttribute("aria-label", "カード" + (i + 1) + "（裏）");
      b.innerHTML =
        '<div class="inner">' +
          '<div class="side cover"><span>?</span></div>' +
          '<div class="side face"></div>' +
        '</div>';
      b.addEventListener("click", () => flip(i, b));
      board.appendChild(b);
    });
  }

  function reveal(btn, dir) {
    const face = btn.querySelector(".face");
    face.innerHTML = ARROW_SVG;
    face.firstChild.style.transform = "rotate(" + ROT[dir] + "deg)";
    btn.classList.add("flipped");
    btn.disabled = true;
    btn.setAttribute("aria-label", LABEL[dir] + "矢印");
  }

  /* ---------- play ---------- */
  function hide(btn, i) {
    btn.classList.remove("flipped");
    // clear the arrow and glow once the card is face-down again
    flipTimers.push(setTimeout(() => {
      btn.classList.remove("good", "bad");
      btn.querySelector(".face").innerHTML = "";
    }, FLIP_MS));
    // looks exactly like an untouched card and can be tapped again
    btn.disabled = false;
    btn.setAttribute("aria-label", "カード" + (i + 1) + "（裏）");
  }

  function flip(i, btn) {
    if (state !== "playing" || busy || btn.disabled) return;
    const dir = deck[i];
    taps++;
    renderTaps();
    reveal(btn, dir);
    if (dir === "up") {
      score++;
      btn.classList.add("good");
      sfx.good();
      renderScore();
      if (score >= NEED) { finish("win"); return; }
      if (taps >= MAX_TAPS) finish("taps");
      return; // correct cards stay face-up
    } else {
      btn.classList.add("bad");
      sfx.bad();
    }
    if (taps >= MAX_TAPS) { finish("taps"); return; }
    busy = true;
    pendingBad = { btn, i };
    flipTimers.push(setTimeout(() => {
      if (state !== "playing") return;
      hide(btn, i);
      pendingBad = null;
      busy = false;
    }, FLIP_MS + HOLD_MS));
  }

  function renderTaps() {
    const left = MAX_TAPS - taps;
    tapVal.innerHTML = left + "<small>/" + MAX_TAPS + "</small>";
    tapPips.forEach((p, k) => p.classList.toggle("on", k < left));
    tapMeter.classList.toggle("danger", left <= 2);
  }

  function renderScore() {
    scoreVal.innerHTML = score + "<small>/3</small>";
    pips.forEach((p, k) => p.classList.toggle("on", k < score));
  }

  function renderTime(left) {
    const t = Math.max(0, left);
    timeVal.innerHTML = t.toFixed(1) + "<small>秒</small>";
    timeBar.style.transform = "scaleX(" + (t / TOTAL_TIME) + ")";
    timeMeter.classList.toggle("danger", t < 10);
  }

  function tick() {
    const left = TOTAL_TIME - (performance.now() - startAt) / 1000;
    renderTime(left);
    if (left <= 0) { finish("time"); return; }
    raf = requestAnimationFrame(tick);
  }

  function start() {
    clearTimeout(endTimer);
    cancelAnimationFrame(raf);
    confetti.stop();
    ["startScreen", "winScreen", "loseScreen", "passScreen"].forEach(id => $(id).hidden = true);
    flipTimers.forEach(clearTimeout);
    flipTimers = [];
    pendingBad = null;
    deck = makeDeck();
    score = 0;
    taps = 0;
    busy = false;
    buildBoard();
    renderScore();
    renderTaps();
    renderTime(TOTAL_TIME);
    sfx.unlock();
    state = "playing";
    $("passBtn").disabled = false;
    $("restartPlayBtn").disabled = false;
    startAt = performance.now();
    raf = requestAnimationFrame(tick);
  }

  /* ---------- pass = pause, then continue from the same spot ---------- */
  function pause() {
    if (state !== "playing") return;
    state = "paused";
    cancelAnimationFrame(raf);
    pausedElapsed = performance.now() - startAt;
    flipTimers.forEach(clearTimeout);
    flipTimers = [];
    if (pendingBad) { hide(pendingBad.btn, pendingBad.i); pendingBad = null; }
    busy = false;
    $("passBtn").disabled = true;
    $("passTime").textContent = Math.max(0, TOTAL_TIME - pausedElapsed / 1000).toFixed(1) + "秒";
    $("passScore").textContent = score + "/3";
    $("passTaps").textContent = (MAX_TAPS - taps) + "/" + MAX_TAPS;
    $("passScreen").hidden = false;
    $("passNextBtn").focus();
  }

  function resume() {
    if (state !== "paused") return;
    $("passScreen").hidden = true;
    state = "playing";
    $("passBtn").disabled = false;
    startAt = performance.now() - pausedElapsed;
    raf = requestAnimationFrame(tick);
  }

  function finish(result) {
    if (state !== "playing") return;
    state = "over";
    $("passBtn").disabled = true;
    $("restartPlayBtn").disabled = true;
    cancelAnimationFrame(raf);
    flipTimers.forEach(clearTimeout);
    flipTimers = [];
    const elapsed = Math.min(TOTAL_TIME, (performance.now() - startAt) / 1000);
    renderTime(TOTAL_TIME - elapsed);
    board.querySelectorAll(".card").forEach(c => { if (!c.classList.contains("flipped")) c.disabled = true; });

    if (result === "win") {
      $("winTime").textContent = elapsed.toFixed(1) + "秒";
      endTimer = setTimeout(() => {
        $("winScreen").hidden = false;
        sfx.win();
        confetti.burst();
        $("againBtn").focus();
      }, FLIP_MS + 250);
    } else {
      if (result === "time") sfx.bad();
      $("loseReason").textContent = result === "time" ? "時間切れ" : "回数切れ";
      $("loseScore").textContent = score + "/3";
      $("loseNote").textContent = result === "time"
        ? "30秒が経過しました。もう一度挑戦しよう！"
        : "7回押し切りました。もう一度挑戦しよう！";
      // show where every arrow was
      endTimer = setTimeout(() => {
        board.querySelectorAll(".card").forEach((c, i) => {
          if (!c.classList.contains("flipped")) {
            c.classList.remove("used", "good", "bad");
            reveal(c, deck[i]);
            c.classList.add("ghost");
          }
        });
        endTimer = setTimeout(() => {
          $("loseScreen").hidden = false;
          $("restartBtn").focus();
        }, FLIP_MS + 500);
      }, result === "time" ? 150 : FLIP_MS + 350);
    }
  }

  /* ---------- sound (Web Audio, no files) ---------- */
  const sfx = (() => {
    let ctx = null;
    function ac() {
      if (!ctx) { try { ctx = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) { ctx = null; } }
      if (ctx && ctx.state === "suspended") ctx.resume().catch(() => {});
      return ctx;
    }
    function tone(freq, at, dur, type, vol, slideTo) {
      const c = ac(); if (!c) return;
      const t = c.currentTime + at;
      const o = c.createOscillator(), g = c.createGain();
      o.type = type;
      o.frequency.setValueAtTime(freq, t);
      if (slideTo) o.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
      g.gain.setValueAtTime(0.0001, t);
      g.gain.exponentialRampToValueAtTime(vol, t + 0.01);
      g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
      o.connect(g).connect(c.destination);
      o.start(t); o.stop(t + dur + 0.02);
    }
    return {
      unlock: ac,
      good() { tone(660, 0, .12, "square", .12); tone(990, .09, .2, "square", .12); },
      bad() { tone(220, 0, .35, "sawtooth", .14, 90); tone(150, .05, .4, "square", .08, 70); },
      win() { [523, 659, 784, 1047].forEach((f, i) => tone(f, i * .11, .22, "square", .11)); tone(1047, .45, .5, "triangle", .12); }
    };
  })();

  /* ---------- confetti ---------- */
  const confetti = (() => {
    const cv = $("confetti"), ctx = cv.getContext("2d");
    const colors = ["#ffb526", "#ff5fa2", "#3ef08a", "#5ec8ff", "#ffffff", "#a98bff"];
    let parts = [], id = 0;
    function size() {
      const d = window.devicePixelRatio || 1;
      cv.width = innerWidth * d; cv.height = innerHeight * d;
      ctx.setTransform(d, 0, 0, d, 0, 0);
    }
    function burst() {
      if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
      size();
      parts = [];
      for (let k = 0; k < 180; k++) {
        const fromLeft = k % 2 === 0;
        parts.push({
          x: fromLeft ? -10 : innerWidth + 10,
          y: innerHeight * (0.55 + Math.random() * 0.3),
          vx: (fromLeft ? 1 : -1) * (4 + Math.random() * 7),
          vy: -(9 + Math.random() * 9),
          w: 6 + Math.random() * 6, h: 8 + Math.random() * 8,
          r: Math.random() * Math.PI, vr: (Math.random() - .5) * .3,
          c: colors[k % colors.length], life: 0
        });
      }
      cancelAnimationFrame(id);
      loop();
    }
    function loop() {
      ctx.clearRect(0, 0, innerWidth, innerHeight);
      let alive = 0;
      for (const p of parts) {
        p.life++;
        p.vy += 0.28; p.vx *= 0.99; p.vy *= 0.99;
        p.x += p.vx; p.y += p.vy; p.r += p.vr;
        if (p.y < innerHeight + 30) alive++;
        ctx.save();
        ctx.translate(p.x, p.y); ctx.rotate(p.r);
        ctx.fillStyle = p.c;
        ctx.fillRect(-p.w / 2, -p.h / 2 * Math.abs(Math.cos(p.life * .15)), p.w, p.h * Math.abs(Math.cos(p.life * .15)) + 1);
        ctx.restore();
      }
      if (alive) id = requestAnimationFrame(loop); else ctx.clearRect(0, 0, innerWidth, innerHeight);
    }
    function stop() { cancelAnimationFrame(id); parts = []; ctx.clearRect(0, 0, cv.width, cv.height); }
    addEventListener("resize", () => { if (parts.length) size(); });
    return { burst, stop };
  })();

  /* ---------- wire up ---------- */
  $("startBtn").addEventListener("click", start);
  $("againBtn").addEventListener("click", start);
  $("restartBtn").addEventListener("click", start);
  // パス: pause; 続ける: resume from the same spot
  $("passBtn").addEventListener("click", pause);
  $("passNextBtn").addEventListener("click", resume);
  // リスタート: new shuffle, timer and taps reset
  $("restartPlayBtn").addEventListener("click", start);
  $("pauseRestartBtn").addEventListener("click", start);

  // idle board behind the start screen
  deck = makeDeck();
  buildBoard();
  board.querySelectorAll(".card").forEach(c => c.disabled = true);
  renderScore();
  renderTaps();
  renderTime(TOTAL_TIME);
})();
