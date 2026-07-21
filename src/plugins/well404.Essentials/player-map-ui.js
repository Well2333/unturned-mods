const app = panel.root.querySelector("#app");
const view = panel.view;
const zh = panel.lang === "zh";
const sections = [
  ["travel", zh ? "传送与位置" : "Travel & locations", card => card.key === "home" || card.key === "back" || card.key === "warps" || card.key === "warp-map" || card.key.startsWith("warp:")],
  ["players-party", zh ? "玩家与队伍" : "Players & party", card => card.key === "noplayers" || card.key.startsWith("p:") || card.key.startsWith("tpreq:") || card.key.startsWith("pinv:") || card.key === "party" || card.key.startsWith("pmember:")],
  ["gifts", zh ? "礼包" : "Gifts", card => card.key === "gifts" || card.key.startsWith("gift:")],
  ["sleep", zh ? "世界时间" : "World time", card => card.key === "sleep"]
];

const make = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
};

function appendActions(card, host) {
  for (const action of card.buttons || []) {
    const button = make("button", action.style || "", action.label);
    button.type = "button";
    button.onclick = () => panel.invoke(card, action, button);
    host.append(button);
  }
}

function cardNode(card) {
  const node = make("article", "item");
  node.append(make("div", "title", card.label));
  if (card.tags?.length) {
    const pills = make("div", "pills");
    for (const tag of card.tags) pills.append(make("span", "pill", tag));
    node.append(pills);
  }
  if (card.lines?.length) node.append(make("div", "lines", card.lines.join(" · ")));
  if (card.buttons?.length) {
    const actions = make("div", "actions");
    appendActions(card, actions);
    node.append(actions);
  }
  return node;
}

function quickActionNode(card) {
  const node = make("article", "quick-item");
  const copy = make("div", "quick-copy");
  copy.append(make("span", "quick-title", card.label));
  if (card.lines?.length) copy.append(make("span", "quick-status", zh ? "未设置" : "Not set"));
  node.append(copy);
  if (card.buttons?.length) {
    const actions = make("div", "quick-buttons");
    appendActions(card, actions);
    node.append(actions);
  }
  return node;
}

const hero = make("div", "hero");
const heroCopy = make("div");
heroCopy.append(make("h2", "", view.title), make("p", "", view.header || ""));
hero.append(heroCopy);
app.append(hero);
if (view.message) app.append(make("div", "notice", view.message));
const tabs = make("nav", "tabs");
const content = make("div", "content");
app.append(tabs, content);

const models = sections.map(([id, label, match]) => ({
  id,
  label,
  cards: (view.cards || []).filter(match)
}));
const stateKey = "well404.essentials.player.tab";
const warpKey = "well404.essentials.player.warp-filter";
const warpViewKey = "well404.essentials.player.warp-view";
const viewportKey = "well404.essentials.player.warp-viewport";
let savedTab = "";
try { savedTab = sessionStorage.getItem(stateKey) || ""; } catch {}
let active = models.some(model => model.id === savedTab)
  ? savedTab
  : (models.find(model => model.cards.length)?.id || models[0].id);
let disposeActiveMap = () => {};
function clearActiveMap() {
  disposeActiveMap();
  disposeActiveMap = () => {};
}
const extensionLifetime = typeof MutationObserver === "undefined" ? null : new MutationObserver(() => {
  if (panel.root.host.isConnected) return;
  clearActiveMap();
  extensionLifetime.disconnect();
});
extensionLifetime?.observe(document.documentElement, { childList: true, subtree: true });

function subsection(parent, title, cards) {
  const section = make("section", "subsection");
  const head = make("div", "subsection-head");
  head.append(make("h4", "", title), make("span", "count", String(cards.length)));
  section.append(head);
  const items = make("div", "items");
  if (cards.length) cards.forEach(card => items.append(cardNode(card)));
  else items.append(make("div", "empty", zh ? "暂无内容" : "Nothing here"));
  section.append(items);
  parent.append(section);
}

function warpTags(card) {
  const tags = String(card.metadata?.warpTags || "").split(/\n+/).map(value => value.trim()).filter(Boolean);
  return tags.length ? tags : ["default"];
}

function warpTagLabels(card) {
  const labels = String(card.metadata?.warpTagLabels || "").split(/\n+/).map(value => value.trim()).filter(Boolean);
  const ids = warpTags(card);
  return new Map(ids.map((id, index) => [id, labels[index] || id]));
}

function mapReason(meta, kind) {
  const reason = meta?.[kind + "Reason"] || meta?.mapReason || "unavailable";
  const names = kind === "gps" ? ["GPS 地图", "GPS map"] : ["Chart 地图", "chart"];
  const text = {
    disabled: ["互动地图已由服务器关闭。", "The interactive map is disabled by the server."],
    locked: kind === "gps"
      ? ["当前角色没有 GPS 权限；需要 GPS 物品或服务器开放卫星地图。", "Your character needs a GPS item or server satellite-map access."]
      : ["当前角色没有 Chart 权限；需要地图物品或服务器开放 Chart。", "Your character needs a chart item or server chart access."],
    missing: [`当前地图没有提供 ${names[0]}图片。`, `The current map does not provide a ${names[1]} image.`],
    "too-large": [`${names[0]}图片过大，服务器未发送。`, `The ${names[1]} image is too large to send.`],
    unavailable: ["当前地图尚未加载完成。", "The current map is not ready."]
  };
  return (text[reason] || text.unavailable)[zh ? 0 : 1];
}

function mapAvailable(meta, kind) {
  return meta?.[kind + "Available"] === "true";
}

function mapAssetId(meta, kind) {
  return meta?.[kind + "AssetId"] || kind;
}

function normalizedMapCoordinate(value) {
  if (value == null || String(value).trim() === "") return null;
  const coordinate = Number(value);
  return Number.isFinite(coordinate) && coordinate >= 0 && coordinate <= 1 ? coordinate : null;
}

function readViewport() {
  try {
    const value = JSON.parse(sessionStorage.getItem(viewportKey) || "null");
    if (value && Number.isFinite(value.scale) && Number.isFinite(value.x) && Number.isFinite(value.y)) return value;
  } catch {}
  return { scale: 1, x: 0, y: 0 };
}

function installPanZoom(stage, canvas, markers, controls, sizeMode) {
  const saved = readViewport();
  let scale = Math.max(1, Math.min(5, saved.scale));
  let xRatio = saved.x;
  let yRatio = saved.y;
  let x = 0;
  let y = 0;
  let dragging = false;
  let lastX = 0;
  let lastY = 0;
  let observer;
  const sizeLimits = { compact: 1040, large: 1280 };

  const fitStage = () => {
    const shell = stage.parentElement;
    const shellWidth = Math.floor(shell?.getBoundingClientRect().width || shell?.clientWidth || sizeLimits.compact);
    const viewportHeight = window.visualViewport?.height || window.innerHeight;
    const fitWidth = Math.max(280, Math.floor(viewportHeight * 0.72));
    let width;
    if (sizeMode === "large") {
      // Large is width-led and may extend well below the fold.
      width = Math.min(shellWidth, sizeLimits.large);
    } else {
      // Compact is the responsive default: it uses the whole content viewport rather than only
      // the space below the stage, and intentionally allows a short page scroll.
      width = Math.min(shellWidth, fitWidth, sizeLimits.compact);
    }
    stage.style.width = `${Math.max(1, Math.floor(width))}px`;
  };
  const clamp = () => {
    const width = stage.clientWidth;
    const height = stage.clientHeight;
    x = Math.max(width * (1 - scale), Math.min(0, x));
    y = Math.max(height * (1 - scale), Math.min(0, y));
  };
  const render = () => {
    const width = stage.clientWidth;
    const height = stage.clientHeight;
    clamp();
    canvas.style.transform = `translate(${x}px,${y}px) scale(${scale})`;
    for (const marker of markers.querySelectorAll(".map-marker")) {
      marker.style.left = `${x + Number(marker.dataset.mapX) * width * scale}px`;
      marker.style.top = `${y + Number(marker.dataset.mapY) * height * scale}px`;
    }
  };
  const persist = () => {
    xRatio = x / (stage.clientWidth || 1);
    yRatio = y / (stage.clientHeight || 1);
    try { sessionStorage.setItem(viewportKey, JSON.stringify({ scale, x: xRatio, y: yRatio })); } catch {}
  };
  const resize = () => {
    fitStage();
    x = xRatio * stage.clientWidth;
    y = yRatio * stage.clientHeight;
    render();
  };
  const zoom = (factor, centerX = stage.clientWidth / 2, centerY = stage.clientHeight / 2) => {
    const next = Math.max(1, Math.min(5, scale * factor));
    if (next === scale) return;
    x = centerX - (centerX - x) * (next / scale);
    y = centerY - (centerY - y) * (next / scale);
    scale = next;
    render();
    persist();
  };

  fitStage();
  x = xRatio * stage.clientWidth;
  y = yRatio * stage.clientHeight;
  render();
  const zoomIn = () => zoom(1.3);
  const zoomOut = () => zoom(1 / 1.3);
  const reset = () => {
    scale = 1;
    x = 0;
    y = 0;
    render();
    persist();
  };
  const wheel = event => {
    event.preventDefault();
    const rect = stage.getBoundingClientRect();
    zoom(event.deltaY < 0 ? 1.18 : 1 / 1.18, event.clientX - rect.left, event.clientY - rect.top);
  };
  const pointerDown = event => {
    if (event.target.closest("button") || scale <= 1) return;
    dragging = true;
    lastX = event.clientX;
    lastY = event.clientY;
    stage.classList.add("dragging");
    stage.setPointerCapture(event.pointerId);
  };
  const pointerMove = event => {
    if (!dragging) return;
    x += event.clientX - lastX;
    y += event.clientY - lastY;
    lastX = event.clientX;
    lastY = event.clientY;
    render();
  };
  const endDrag = () => {
    if (dragging) persist();
    dragging = false;
    stage.classList.remove("dragging");
  };
  controls.zoomIn.onclick = zoomIn;
  controls.zoomOut.onclick = zoomOut;
  controls.reset.onclick = reset;
  stage.addEventListener("wheel", wheel, { passive: false });
  stage.addEventListener("pointerdown", pointerDown);
  stage.addEventListener("pointermove", pointerMove);
  stage.addEventListener("pointerup", endDrag);
  stage.addEventListener("pointercancel", endDrag);
  window.addEventListener("resize", resize);
  window.visualViewport?.addEventListener("resize", resize);
  if (typeof ResizeObserver !== "undefined") {
    observer = new ResizeObserver(resize);
    observer.observe(stage.parentElement || stage);
  }
  return () => {
    observer?.disconnect();
    window.removeEventListener("resize", resize);
    window.visualViewport?.removeEventListener("resize", resize);
    controls.zoomIn.onclick = null;
    controls.zoomOut.onclick = null;
    controls.reset.onclick = null;
    stage.removeEventListener("wheel", wheel);
    stage.removeEventListener("pointerdown", pointerDown);
    stage.removeEventListener("pointermove", pointerMove);
    stage.removeEventListener("pointerup", endDrag);
    stage.removeEventListener("pointercancel", endDrag);
    endDrag();
  };
}

function travelContent(model, parent) {
  const allWarps = "__all__";
  const quick = model.cards.filter(card => card.key === "home" || card.key === "back");
  const mapCard = model.cards.find(card => card.key === "warp-map");
  const meta = mapCard?.metadata || {};
  const emptyWarp = model.cards.filter(card => card.key === "warps");
  const warps = model.cards.filter(card => card.key.startsWith("warp:"));

  const quickSection = make("section", "subsection quick-travel");
  const quickHead = make("div", "subsection-head");
  const quickActions = make("div", "quick-actions");
  quickHead.append(make("h4", "", zh ? "快捷操作" : "Quick actions"));
  quick.forEach(card => quickActions.append(quickActionNode(card)));
  quickSection.append(quickHead, quickActions);
  parent.append(quickSection);

  const tagLabels = new Map();
  for (const card of warps) for (const [id, label] of warpTagLabels(card)) if (!tagLabels.has(id)) tagLabels.set(id, label);
  const tags = [...new Set(warps.flatMap(warpTags))];
  let selected = allWarps;
  try {
    const value = sessionStorage.getItem(warpKey) || allWarps;
    if (value === "all") sessionStorage.setItem(warpKey, allWarps);
    else if (value === allWarps || tags.includes(value)) selected = value;
  } catch {}

  const section = make("section", "subsection warp-section");
  const head = make("div", "subsection-head");
  const count = make("span", "count");
  const filters = make("nav", "filter-tabs");
  const viewBar = make("div", "warp-viewbar");
  const mapName = make("span", "map-name", meta.mapName
    ? (zh ? `当前地图：${meta.mapName}` : `Current map: ${meta.mapName}`)
    : (zh ? "当前地图不可用" : "Current map unavailable"));
  const viewTools = make("div", "warp-view-tools");
  const refreshNote = make("span", "refresh-paused", zh ? "地图交互期间自动刷新已暂停" : "Auto-refresh paused while using the map");
  const sizeSwitcher = make("div", "size-switch");
  const switcher = make("div", "view-switch");
  const gpsButton = make("button", "", "GPS");
  const chartButton = make("button", "", "Chart");
  const listButton = make("button", "", zh ? "列表" : "List");
  const body = make("div", "warp-body");
  head.append(make("h4", "", zh ? "传送点" : "Warps"), count);
  switcher.setAttribute("role", "group");
  switcher.setAttribute("aria-label", zh ? "传送点视图" : "Warp view");
  switcher.append(gpsButton, chartButton, listButton);
  sizeSwitcher.setAttribute("role", "group");
  sizeSwitcher.setAttribute("aria-label", zh ? "地图框尺寸" : "Map frame size");
  viewTools.append(refreshNote, sizeSwitcher, switcher);
  viewBar.append(mapName, viewTools);
  section.append(head, filters, viewBar, body);

  let mode = mapAvailable(meta, "gps") ? "gps" : (mapAvailable(meta, "chart") ? "chart" : "list");
  let mapSize = meta.mapSize === "large" ? "large" : "compact";
  try {
    const stored = sessionStorage.getItem(warpViewKey);
    if (stored === "map") mode = "chart";
    else if (stored === "gps" || stored === "chart" || stored === "list") mode = stored;
  } catch {}

  const sizeButtons = new Map();
  for (const action of mapCard?.buttons || []) {
    if (!action.actionId?.startsWith("mapsize-")) continue;
    const id = action.actionId.slice("mapsize-".length);
    const button = make("button", "", action.label);
    button.type = "button";
    button.onclick = () => {
      mapSize = id;
      draw();
      panel.invoke(mapCard, action, button);
    };
    sizeButtons.set(id, button);
    sizeSwitcher.append(button);
  }

  function setMode(next) {
    mode = next === "gps" || next === "chart" ? next : "list";
    try { sessionStorage.setItem(warpViewKey, mode); } catch {}
    draw();
  }
  gpsButton.onclick = () => setMode("gps");
  chartButton.onclick = () => setMode("chart");
  listButton.onclick = () => setMode("list");
  if (!mapAvailable(meta, "gps")) gpsButton.title = mapReason(meta, "gps");
  if (!mapAvailable(meta, "chart")) chartButton.title = mapReason(meta, "chart");

  function createMap(kind, visible) {
    const shell = make("div", "warp-map-shell");
    if (!mapAvailable(meta, kind)) {
      shell.append(make("div", "map-empty", mapReason(meta, kind)));
      return { node: shell, mount: () => {} };
    }

    const stage = make("div", "warp-map-stage");
    const canvas = make("div", "warp-map-canvas");
    const image = document.createElement("img");
    const markers = make("div", "warp-map-markers");
    const controlsNode = make("div", "map-controls");
    const zoomOut = make("button", "", "−");
    const reset = make("button", "", zh ? "复位" : "Reset");
    const zoomIn = make("button", "", "+");
    image.alt = meta.mapName ? `${meta.mapName} ${kind === "gps" ? "GPS" : "Chart"}` : "Current map";
    image.draggable = false;
    image.src = panel.assetUrl(mapAssetId(meta, kind));
    image.onerror = () => shell.replaceChildren(make("div", "map-empty", zh
      ? `${kind === "gps" ? "GPS" : "Chart"} 图片加载失败，请切换视图。`
      : `The ${kind} image failed to load. Switch views.`));

    const appendMapMarker = (card, markerKind, emoji) => {
      const mapX = normalizedMapCoordinate(card?.metadata?.mapX);
      const mapY = normalizedMapCoordinate(card?.metadata?.mapY);
      // The server omits projected coordinates outside the native chart rectangle. Keep this
      // client-side check as a defensive boundary: special-area warps remain available in List.
      if (mapX == null || mapY == null) return;
      const rawLabel = card.label || "";
      const label = emoji && rawLabel.startsWith(emoji) ? rawLabel.slice(emoji.length).trim() : rawLabel;
      const locationMarker = markerKind === "home" || markerKind === "death";
      const marker = make("button", locationMarker
        ? `map-marker map-marker-location map-marker-${markerKind}`
        : "map-marker map-marker-warp");
      marker.type = "button";
      marker.dataset.mapX = String(mapX);
      marker.dataset.mapY = String(mapY);
      marker.setAttribute("aria-label", locationMarker
        ? (markerKind === "home" ? (zh ? "回家" : "Go home") : (zh ? "返回死亡点" : "Return to death point"))
        : (zh ? "传送到 " : "Teleport to ") + label);
      if (locationMarker) {
        marker.append(
          make("span", "map-location-glyph", emoji),
          make("span", "map-marker-label", label)
        );
      } else {
        const pin = make("span", "map-marker-pin");
        pin.append(make("span", "map-marker-glyph", emoji));
        marker.append(pin, make("span", "map-marker-label", label));
      }
      const action = (card.buttons || []).find(item => item.actionId === "go");
      marker.disabled = !action;
      marker.onclick = event => {
        event.stopPropagation();
        if (action) panel.invoke(card, action, marker);
      };
      markers.append(marker);
    };

    for (const card of quick) {
      const markerKind = card.metadata?.mapMarkerKind;
      if (markerKind === "home" || markerKind === "death") {
        appendMapMarker(card, markerKind, card.metadata?.mapMarkerEmoji || (markerKind === "home" ? "🏠" : "💀"));
      }
    }
    for (const card of visible) {
      appendMapMarker(card, "warp", card.metadata?.warpEmoji || "📍");
    }

    canvas.append(image);
    controlsNode.append(zoomOut, reset, zoomIn);
    stage.append(canvas, markers, controlsNode);
    shell.append(stage);
    if (!markers.children.length) shell.append(make("div", "empty", zh
      ? "当前筛选结果没有坐标有效的传送点。"
      : "No visible warp has a valid map position."));
    return {
      node: shell,
      mount: () => installPanZoom(stage, canvas, markers, { zoomIn, zoomOut, reset }, mapSize)
    };
  }

  function draw() {
    clearActiveMap();
    const mapMode = mode === "gps" || mode === "chart";
    panel.setAutoRefreshPaused?.(mapMode);
    quickSection.hidden = mapMode;
    refreshNote.hidden = !mapMode;
    sizeSwitcher.hidden = !mapMode;
    filters.replaceChildren();
    const choices = [[allWarps, zh ? "全部" : "All"], ...tags.map(tag => [tag, tagLabels.get(tag) || tag])];
    for (const [id, label] of choices) {
      const amount = id === allWarps ? warps.length : warps.filter(card => warpTags(card).includes(id)).length;
      const button = make("button", id === selected ? "active" : "", `${label} · ${amount}`);
      button.type = "button";
      button.setAttribute("aria-pressed", id === selected ? "true" : "false");
      button.onclick = () => {
        selected = id;
        try { sessionStorage.setItem(warpKey, id); } catch {}
        draw();
      };
      filters.append(button);
    }

    const visible = warps.filter(card => selected === allWarps || warpTags(card).includes(selected));
    count.textContent = String(visible.length);
    for (const [button, id] of [[gpsButton, "gps"], [chartButton, "chart"], [listButton, "list"]]) {
      button.classList.toggle("active", mode === id);
      button.setAttribute("aria-pressed", mode === id ? "true" : "false");
    }
    for (const [id, button] of sizeButtons) {
      button.classList.toggle("active", mapSize === id);
      button.setAttribute("aria-pressed", mapSize === id ? "true" : "false");
    }
    body.replaceChildren();
    if (mapMode) {
      const map = createMap(mode, visible);
      body.append(map.node);
      disposeActiveMap = map.mount() || (() => {});
    } else {
      const grid = make("div", "items");
      if (visible.length) visible.forEach(card => grid.append(cardNode(card)));
      else if (emptyWarp.length && selected === allWarps) emptyWarp.forEach(card => grid.append(cardNode(card)));
      else grid.append(make("div", "empty", zh ? "此标签下暂无传送点" : "No warps with this tag"));
      body.append(grid);
    }
  }

  draw();
  parent.append(section);
}

function playersPartyContent(model, parent) {
  const online = model.cards.filter(card => card.key === "noplayers" || card.key.startsWith("p:"));
  const pending = model.cards.filter(card => card.key.startsWith("tpreq:") || card.key.startsWith("pinv:"));
  const party = model.cards.filter(card => card.key === "party" || card.key.startsWith("pmember:"));
  subsection(parent, zh ? "在线玩家" : "Online players", online);
  if (pending.length) subsection(parent, zh ? "待处理请求" : "Pending requests", pending);
  subsection(parent, zh ? "队伍" : "Party", party);
}

function paint() {
  clearActiveMap();
  tabs.replaceChildren();
  for (const model of models) {
    const button = make("button", model.id === active ? "active" : "", model.label);
    button.type = "button";
    button.setAttribute("aria-selected", model.id === active ? "true" : "false");
    button.onclick = () => {
      active = model.id;
      try { sessionStorage.setItem(stateKey, active); } catch {}
      paint();
    };
    tabs.append(button);
  }

  const model = models.find(item => item.id === active) || models[0];
  if (model.id !== "travel") panel.setAutoRefreshPaused?.(false);
  const panelNode = make("section", "panel");
  const head = make("div", "panel-head");
  head.append(make("h3", "", model.label), make("span", "count", String(model.cards.length)));
  panelNode.append(head);
  if (model.id === "travel") travelContent(model, panelNode);
  else if (model.id === "players-party") playersPartyContent(model, panelNode);
  else {
    const items = make("div", "items");
    if (model.cards.length) model.cards.forEach(card => items.append(cardNode(card)));
    else items.append(make("div", "empty", zh ? "暂无内容" : "Nothing here"));
    panelNode.append(items);
  }
  content.replaceChildren(panelNode);
}

paint();
