(function () {
  "use strict";

  const DATA = window.FESTIVAL_DATA;
  if (!DATA) {
    document.body.innerHTML = "<p style='padding:2em;color:#fff'>data.js could not be loaded.</p>";
    return;
  }
  const SETTINGS = DATA.settings || {};
  const MSG = DATA.messages || {};

  const $ = (sel, root = document) => root.querySelector(sel);
  const prefersReducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  function shuffle(list) {
    const a = list.slice();
    for (let i = a.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [a[i], a[j]] = [a[j], a[i]];
    }
    return a;
  }

  function text(key, vars) {
    let s = MSG[key];
    if (s === undefined) return "";
    if (Array.isArray(s)) s = s[Math.floor(Math.random() * s.length)];
    if (vars) s = s.replace(/\{(\w+)\}/g, (m, k) => (vars[k] !== undefined ? vars[k] : m));
    return s;
  }

  const departments = (DATA.departments || []).filter((d) => d && d.id);
  const deptById = {};
  departments.forEach((d) => { deptById[d.id] = d; });

  function groupOf(deptId) {
    const d = deptById[deptId];
    return d ? (d.group || d.id) : null;
  }

  function groupName(deptId) {
    const g = groupOf(deptId);
    return departments.filter((d) => (d.group || d.id) === g).map((d) => d.name).join("・");
  }

  const foods = (DATA.foods || []).filter((f) => {
    if (!f || !f.id) return false;
    if (!deptById[f.departmentId]) {
      console.warn("[Food Match] Food \"" + f.id + "\" has unknown departmentId \"" + f.departmentId + "\" and was skipped.");
      return false;
    }
    return true;
  });

  const state = {
    cards: [],
    filled: {},
    total: 0,
    correct: 0,
    selected: null,
    wrongStreak: {},
    finished: false,
    touchDragArmed: false,
    congratsTimer: null
  };

  function buildArt(emoji, imageSrc) {
    const wrap = document.createElement("span");
    wrap.className = "art";
    const e = document.createElement("span");
    e.className = "art-emoji";
    e.textContent = emoji || "🍽️";
    wrap.appendChild(e);
    if (imageSrc) {
      const img = new Image();
      img.alt = "";
      img.draggable = false;
      img.addEventListener("load", () => wrap.classList.add("has-img"));
      img.addEventListener("error", () => img.remove());
      img.src = imageSrc;
      wrap.appendChild(img);
    }
    return wrap;
  }

  function renderBooths(deck) {
    const area = $("#booths");
    area.innerHTML = "";

    const used = new Set(deck.map((f) => groupOf(f.departmentId)));
    departments.filter((d) => used.has(groupOf(d.id))).forEach((dept) => {
      const booth = document.createElement("div");
      booth.className = "booth";
      booth.dataset.dept = dept.id;
      booth.tabIndex = 0;
      booth.setAttribute("role", "button");
      booth.setAttribute("aria-label", dept.name);
      if (dept.color) booth.style.setProperty("--c", dept.color);

      if (dept.image) {
        booth.classList.add("has-art");
        const art = new Image();
        art.className = "booth-art";
        art.alt = "";
        art.draggable = false;
        art.addEventListener("error", () => { art.remove(); booth.classList.remove("has-art"); });
        art.src = dept.image;
        booth.appendChild(art);
      }

      const sign = document.createElement("div");
      sign.className = "booth-sign";
      if (dept.icon || dept.image) sign.appendChild(buildArt(dept.icon, dept.image));
      if (dept.boothName) {
        const st = document.createElement("span");
        st.textContent = dept.boothName;
        sign.appendChild(st);
      }

      const noren = document.createElement("div");
      noren.className = "noren";
      noren.textContent = dept.name;

      const zone = document.createElement("div");
      zone.className = "dropzone";
      zone.dataset.emptyText = text("dropHere");

      booth.append(sign, noren, zone);
      booth.addEventListener("click", () => onBoothActivate(booth));
      booth.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onBoothActivate(booth); }
      });
      area.appendChild(booth);
    });
  }

  function layoutBooths() {
    const area = $("#booths");
    const n = area.children.length;
    if (!n) return;
    const gap = parseFloat(getComputedStyle(area).columnGap) || 16;
    const width = area.clientWidth;
    const minBooth = width < 720 ? 140 : 235;
    const maxCols = Math.max(1, Math.floor((width + gap) / (minBooth + gap)));
    const rows = Math.ceil(n / maxCols);
    area.style.setProperty("--cols", Math.ceil(n / rows));
  }

  function renderTray() {
    const tray = $("#tray");
    tray.innerHTML = "";
    tray.dataset.doneText = text("allPlaced");
    state.cards.forEach((card) => {
      card.el = createCardElement(card);
      tray.appendChild(card.el);
    });
  }

  function createCardElement(card) {
    const food = card.food;
    const el = document.createElement("div");
    el.className = "card";
    el.tabIndex = 0;
    el.dataset.id = food.id;
    el.setAttribute("role", "button");
    el.setAttribute("aria-pressed", "false");
    el.setAttribute("aria-label", food.name);

    const art = document.createElement("div");
    art.className = "card-art";
    art.appendChild(buildArt(food.emoji));

    const name = document.createElement("div");
    name.className = "card-name";
    name.textContent = food.name;

    el.append(art, name);

    if (food.image) {
      el.classList.add("has-img");
      const img = new Image();
      img.className = "card-img";
      img.alt = "";
      img.draggable = false;
      img.addEventListener("error", () => { img.remove(); el.classList.remove("has-img"); });
      img.src = food.image;
      el.prepend(img);
    }

    el.addEventListener("pointerdown", (e) => onCardPointerDown(e, card));
    el.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") { e.preventDefault(); toggleSelect(card); }
    });
    el.addEventListener("contextmenu", (e) => e.preventDefault());
    el.addEventListener("dragstart", (e) => e.preventDefault());
    return el;
  }

  function setSelected(card) {
    state.selected = card;
    state.cards.forEach((c) => {
      if (c.placed || !c.el) return;
      const on = c === card;
      c.el.classList.toggle("selected", on);
      c.el.setAttribute("aria-pressed", on ? "true" : "false");
    });
    document.body.classList.toggle("has-selection", !!card);
  }

  function toggleSelect(card) {
    if (state.finished || card.placed) return;
    if (state.selected === card) {
      setSelected(null);
      setMessage(text("instruction"), "info");
    } else {
      setSelected(card);
      setMessage(text("selected", { food: card.food.name }), "info");
    }
  }

  function boothAt(x, y) {
    const el = document.elementFromPoint(x, y);
    return el ? el.closest(".booth") : null;
  }

  function onBoothActivate(boothEl) {
    if (state.finished) return;
    if (!state.selected) {
      setMessage(text("pickCardFirst"), "info");
      return;
    }
    const card = state.selected;
    if (!tryPlace(card, boothEl)) setSelected(null);
  }

  function onCardPointerDown(e, card) {
    if (state.finished || card.placed) return;
    if (e.pointerType === "mouse" && e.button !== 0) return;

    const el = card.el;
    const pointerId = e.pointerId;
    const startX = e.clientX;
    const startY = e.clientY;
    const rect = el.getBoundingClientRect();
    const offsetX = startX - rect.left;
    const offsetY = startY - rect.top;

    const isTouch = e.pointerType === "touch";
    let armed = !isTouch;
    let dragging = false;
    let ghost = null;
    let hoverBooth = null;
    let holdTimer = null;

    try { el.setPointerCapture(pointerId); } catch (err) { /* ignore */ }

    if (isTouch) {
      holdTimer = setTimeout(() => {
        armed = true;
        state.touchDragArmed = true;
        el.classList.add("armed");
        if (navigator.vibrate) navigator.vibrate(10);
      }, 160);
    }

    function moveGhost(ev) {
      ghost.style.transform =
        "translate(" + (ev.clientX - offsetX) + "px," + (ev.clientY - offsetY) + "px) rotate(4deg) scale(1.08)";
    }

    function startDrag(ev) {
      dragging = true;
      ghost = el.cloneNode(true);
      ghost.classList.remove("selected", "armed", "dragging");
      ghost.classList.add("ghost");
      ghost.removeAttribute("tabindex");
      ghost.style.width = rect.width + "px";
      document.body.appendChild(ghost);
      el.classList.add("dragging");
      setSelected(card);
      moveGhost(ev);
    }

    function updateHover(ev) {
      let b = boothAt(ev.clientX, ev.clientY);
      if (b && b.classList.contains("complete")) b = null;
      if (b !== hoverBooth) {
        if (hoverBooth) hoverBooth.classList.remove("drop-ready");
        hoverBooth = b;
        if (hoverBooth) hoverBooth.classList.add("drop-ready");
      }
    }

    function cleanup(flyBack) {
      clearTimeout(holdTimer);
      state.touchDragArmed = false;
      el.removeEventListener("pointermove", onMove);
      el.removeEventListener("pointerup", onUp);
      el.removeEventListener("pointercancel", onCancel);
      try { el.releasePointerCapture(pointerId); } catch (err) {}
      if (hoverBooth) hoverBooth.classList.remove("drop-ready");
      el.classList.remove("armed");

      if (ghost && flyBack) {
        const g = ghost;
        const r = el.getBoundingClientRect();
        g.style.transition = "transform 0.28s cubic-bezier(0.3, 1.3, 0.5, 1)";
        g.style.transform = "translate(" + r.left + "px," + r.top + "px)";
        setTimeout(() => { g.remove(); el.classList.remove("dragging"); }, 300);
      } else {
        if (ghost) ghost.remove();
        el.classList.remove("dragging");
      }
    }

    function onMove(ev) {
      if (ev.pointerId !== pointerId) return;
      if (!dragging) {
        const dist = Math.hypot(ev.clientX - startX, ev.clientY - startY);
        if (!armed) {
          if (dist > 10) cleanup(false);
          return;
        }
        if (dist < 6) return;
        startDrag(ev);
      }
      moveGhost(ev);
      updateHover(ev);
    }

    function onUp(ev) {
      if (ev.pointerId !== pointerId) return;
      if (!dragging) {
        cleanup(false);
        toggleSelect(card);
        return;
      }
      const target = boothAt(ev.clientX, ev.clientY);
      const placedOk = target ? tryPlace(card, target) : false;
      if (placedOk) {
        cleanup(false);
      } else {
        cleanup(true);
        setSelected(null);
      }
    }

    function onCancel(ev) {
      if (ev.pointerId !== pointerId) return;
      cleanup(false);
    }

    el.addEventListener("pointermove", onMove);
    el.addEventListener("pointerup", onUp);
    el.addEventListener("pointercancel", onCancel);
  }

  document.addEventListener("touchmove", (e) => {
    if (state.touchDragArmed && e.cancelable) e.preventDefault();
  }, { passive: false });

  function tryPlace(card, boothEl) {
    if (state.finished || card.placed) return false;
    const dept = deptById[boothEl.dataset.dept];
    if (!dept) return false;

    if (state.filled[dept.id]) {
      flash(boothEl, "wrong");
      setMessage(text("boothFull", { dept: groupName(dept.id) }), "info");
      return false;
    }

    if (groupOf(card.food.departmentId) === groupOf(dept.id)) {
      onCorrect(card, boothEl, dept);
      return true;
    }
    onWrong(card, boothEl, dept);
    return false;
  }

  function onCorrect(card, boothEl, dept) {
    card.placed = true;
    state.correct += 1;
    state.filled[dept.id] = card;
    delete state.wrongStreak[card.food.id];
    setSelected(null);
    const copy = card.el.cloneNode(true);
    copy.classList.remove("selected", "dragging", "armed", "shake", "leaving", "ghost");
    copy.classList.add("placed");
    ["tabindex", "role", "aria-pressed", "data-id"].forEach((a) => copy.removeAttribute(a));
    $(".dropzone", boothEl).appendChild(copy);
    const leaving = card.el;
    leaving.classList.add("leaving");
    setTimeout(() => leaving.remove(), 300);
    boothEl.classList.add("complete");
    boothEl.setAttribute("aria-disabled", "true");
    const check = document.createElement("div");
    check.className = "booth-check";
    check.textContent = "✓";
    check.setAttribute("aria-label", "OK");
    boothEl.appendChild(check);
    flash(boothEl, "correct");
    sparkle(boothEl);
    setMessage(text("correct", { food: card.food.name, dept: groupName(dept.id) }), "ok");
    updateProgress(true);

    if (state.correct >= state.total) {
      state.finished = true;
      setTimeout(showCongrats, 800);
    }
  }

  function onWrong(card, boothEl, dept) {
    state.wrongStreak[card.food.id] = (state.wrongStreak[card.food.id] || 0) + 1;
    flash(boothEl, "wrong");
    flash(card.el, "shake");
    setMessage(text("wrong", { food: card.food.name, dept: groupName(dept.id) }), "ng");

    const limit = SETTINGS.hintAfterMistakes;
    if (limit > 0 && state.wrongStreak[card.food.id] >= limit) {
      const rightGroup = groupOf(card.food.departmentId);
      const rightBooths = Array.from(document.querySelectorAll(".booth"))
        .filter((b) => groupOf(b.dataset.dept) === rightGroup && !b.classList.contains("complete"));
      if (rightBooths.length) {
        setTimeout(() => {
          rightBooths.forEach((b) => flash(b, "hint", 2800));
          setMessage(text("hint"), "info");
        }, 900);
      }
    }
  }

  function flash(el, className, ms) {
    if (!el) return;
    el.classList.remove(className);
    void el.offsetWidth;
    el.classList.add(className);
    setTimeout(() => el.classList.remove(className), ms || 700);
  }

  function updateProgress(bump) {
    const counter = $("#progress-text");
    counter.textContent = state.correct + " / " + state.total;
    if (bump) flash(counter, "bump", 400);

    const pct = state.total ? Math.round((state.correct / state.total) * 100) : 0;
    $("#progress-fill").style.width = pct + "%";
    $("#progress-bar").setAttribute("aria-valuenow", String(pct));
  }

  function setMessage(msg, kind) {
    const m = $("#message");
    m.textContent = msg;
    m.className = "message " + (kind || "info");
    void m.offsetWidth;
    m.classList.add("pop");
  }

  function showCongrats() {
    const box = $("#congrats");
    $("#congrats-title").textContent = text("congratsTitle");
    $("#congrats-text").textContent = text("congratsText");
    $("#congrats-hint").textContent = text("congratsHint");
    const back = $("#congrats-back");
    if (SETTINGS.cardBack) {
      back.onerror = () => { back.hidden = true; };
      back.src = SETTINGS.cardBack;
      back.hidden = false;
    }
    box.hidden = false;
    confetti(110);
    setMessage(text("allDone"), "ok");

    box.addEventListener("click", hideCongrats, { once: true });
    state.congratsTimer = setTimeout(hideCongrats, 9000);
  }

  function hideCongrats() {
    clearTimeout(state.congratsTimer);
    $("#congrats").hidden = true;
  }

  function confetti(count) {
    const fx = $("#fx");
    const n = prefersReducedMotion() ? 15 : count;
    const colors = ["#E4392B", "#FFC83D", "#2F6FDE", "#2E9E5B", "#D63C86", "#FFF8E8", "#7B4FD0"];
    for (let i = 0; i < n; i++) {
      const piece = document.createElement("i");
      piece.className = "confetti";
      piece.style.setProperty("--x", Math.random() * 100 + "vw");
      piece.style.setProperty("--dx", (Math.random() * 160 - 80) + "px");
      piece.style.setProperty("--rot", (360 + Math.random() * 720) + "deg");
      piece.style.setProperty("--dur", (2.6 + Math.random() * 1.8) + "s");
      piece.style.setProperty("--delay", (Math.random() * 0.8) + "s");
      piece.style.setProperty("--c", colors[i % colors.length]);
      fx.appendChild(piece);
      setTimeout(() => piece.remove(), 5500);
    }
  }

  function sparkle(el) {
    const fx = $("#fx");
    const r = el.getBoundingClientRect();
    const cx = r.left + r.width / 2;
    const cy = r.top + r.height / 2;
    const n = prefersReducedMotion() ? 4 : 12;
    for (let i = 0; i < n; i++) {
      const s = document.createElement("span");
      s.className = "sparkle";
      s.textContent = "✦";
      const angle = (Math.PI * 2 * i) / n;
      const dist = 50 + Math.random() * 50;
      s.style.left = cx + "px";
      s.style.top = cy + "px";
      s.style.setProperty("--dx", Math.cos(angle) * dist + "px");
      s.style.setProperty("--dy", Math.sin(angle) * dist + "px");
      fx.appendChild(s);
      setTimeout(() => s.remove(), 900);
    }
  }

  function init() {
    if (!departments.length || !foods.length) {
      document.body.insertAdjacentHTML("afterbegin",
        "<p style='position:relative;z-index:9;padding:1em;background:#fff;color:#900'>data.js: no departments or foods found.</p>");
      return;
    }

    if (SETTINGS.title) {
      $("#title").textContent = SETTINGS.title;
      document.title = SETTINGS.title;
    }
    $("#progress-label").textContent = text("progressLabel");

    let pool = foods;
    if (SETTINGS.onlyFeatured) {
      const picked = foods.filter((f) => f.featured);
      if (picked.length) pool = picked;
    }
    let deck = shuffle(pool);
    const n = SETTINGS.cardsPerGame;
    if (Number.isInteger(n) && n > 0 && n < deck.length) deck = deck.slice(0, n);

    state.cards = deck.map((food) => ({ food, placed: false, el: null }));

    renderBooths(deck);
    state.total = $("#booths").children.length;
    layoutBooths();
    window.addEventListener("resize", layoutBooths);
    renderTray();
    updateProgress(false);
    setMessage(text("instruction"), "info");
  }

  init();
})();