const root = panel.root;
const zh = panel.lang === "zh";
const labels = zh
  ? {
      title:"实用工具", subtitle:"管理传送、队伍、礼包与世界时间规则。", teleport:"传送设置", rules:"其他规则", warps:"传送点", gifts:"礼包", search:"检索游戏物品", save:"保存设置",
      warpsTitle:"传送点目录", warpsHelp:"GPS 与 Chart 只叠加当前地图且坐标有效的传送点；列表保留所有地图数据。标签筛选同时作用于三种视图。", addWarp:"＋ 新增传送点", manageTags:"管理标签", all:"全部", edit:"编辑", remove:"删除", cancel:"取消", saveWarp:"保存传送点",
      name:"名称", map:"地图（留空使用当前地图）", currentMap:"当前地图", gpsView:"GPS", chartView:"Chart", listView:"列表", zoomReset:"复位", tags:"标签", x:"X", y:"Y", z:"Z", yaw:"朝向",
      confirmDelete:"确定删除传送点 {name}？", empty:"当前标签下没有传送点。", noMapPoints:"当前筛选结果没有坐标有效的传送点。", requestFailed:"请求失败。",
      tagLibrary:"传送点标签库", tagHelp:"预设与自定义标签分别保存到 config.yaml；每项包含稳定 ID、中英文名称和 Emoji。", addCustomTag:"＋ 新增自定义标签", preset:"预设", custom:"自定义",
      tagId:"稳定 ID", kind:"类型", nameEn:"英文名称", nameZh:"中文名称", emoji:"Emoji", saveTag:"保存标签", selectTags:"选择标签", customTagIds:"添加自定义 Tag ID", add:"添加",
      tagInUse:"正在使用的标签必须先从传送点移除。", confirmDeleteTag:"确定删除标签 {id}？"
    }
  : {
      title:"Essentials", subtitle:"Manage teleport, party, gifts and world-time rules.", teleport:"Teleport", rules:"Rules", warps:"Warps", gifts:"Gifts", search:"Item search", save:"Save settings",
      warpsTitle:"Warp directory", warpsHelp:"GPS and Chart overlay valid warps from the current map. The list retains every map. Tag filters apply to all three views.", addWarp:"＋ Add warp", manageTags:"Manage tags", all:"All", edit:"Edit", remove:"Delete", cancel:"Cancel", saveWarp:"Save warp",
      name:"Name", map:"Map (empty = current map)", currentMap:"Current map", gpsView:"GPS", chartView:"Chart", listView:"List", zoomReset:"Reset", tags:"Tags", x:"X", y:"Y", z:"Z", yaw:"Yaw",
      confirmDelete:"Delete warp {name}?", empty:"No warps under this label.", noMapPoints:"No filtered warp has a valid map position.", requestFailed:"Request failed.",
      tagLibrary:"Warp tag library", tagHelp:"Preset and custom tags are stored separately in config.yaml; each has a stable ID, English/Chinese names, and Emoji.", addCustomTag:"＋ Add custom tag", preset:"Preset", custom:"Custom",
      tagId:"Stable ID", kind:"Kind", nameEn:"English name", nameZh:"Chinese name", emoji:"Emoji", saveTag:"Save tag", selectTags:"Select tags", customTagIds:"Add custom tag ID", add:"Add",
      tagInUse:"A used tag must be removed from warps first.", confirmDeleteTag:"Delete tag {id}?"
    };

root.querySelector("#title").textContent = labels.title;
root.querySelector("#subtitle").textContent = labels.subtitle;
root.querySelector("#warps-title").textContent = labels.warpsTitle;
root.querySelector("#warps-help").textContent = labels.warpsHelp;
root.querySelector("#add-warp").textContent = labels.addWarp;
root.querySelector("#manage-tags").textContent = labels.manageTags;
for (const button of root.querySelectorAll("[data-tab]")) button.textContent = labels[button.dataset.tab];

const entries = [
  panel.mountAction("teleport", root.querySelector("#teleport")),
  panel.mountAction("rules", root.querySelector("#rules"))
];
panel.mountAction("gifts", root.querySelector("#gifts"));
panel.mountAction("search", root.querySelector("#search"));
const save = root.querySelector("#save");
save.textContent = labels.save;
save.onclick = () => panel.saveSettings(entries, save);
let active = "teleport";
function select(id) {
  active = id;
  for (const button of root.querySelectorAll("[data-tab]")) {
    const selected = button.dataset.tab === active;
    button.classList.toggle("active", selected);
    button.setAttribute("aria-selected", selected ? "true" : "false");
  }
  for (const section of root.querySelectorAll("[data-panel]")) section.classList.toggle("active", section.dataset.panel === active);
  save.hidden = active !== "teleport" && active !== "rules";
}
for (const button of root.querySelectorAll("[data-tab]")) button.onclick = () => select(button.dataset.tab);
select(active);

const $ = selector => root.querySelector(selector);
const make = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
};
const bodyOf = values => {
  const body = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) body.set(key, String(value ?? ""));
  return body;
};
const modulePath = id => `api/modules/${panel.encode(panel.module.id)}/${panel.encode(id)}`;
const allWarps = "__all__";
const viewKey = "well404.essentials.admin.warp-view";
const viewportKey = "well404.essentials.admin.warp-viewport";
const warpState = { records:[], tagRecords:[], active:allWarps, dragKey:"", loading:false, info:{}, view:"gps" };
try {
  const savedView = sessionStorage.getItem(viewKey);
  if (savedView === "map") warpState.view = "chart";
  else if (savedView === "gps" || savedView === "chart" || savedView === "list") warpState.view = savedView;
} catch {}

const splitTags = value => [...new Set(String(value || "").split(/[\s,]+/).map(tag => tag.trim().toLowerCase()).filter(Boolean))];
const tagsOf = record => {
  const values = record.values || {};
  const tags = splitTags(values.tags);
  if (tags.length) return tags;
  return record.tags?.length ? splitTags(record.tags.join(" ")) : ["default"];
};
const tagById = () => new Map(warpState.tagRecords.map(record => [String(record.values?.id || record.key).toLowerCase(), record]));
function tagDisplay(id) {
  const record = tagById().get(String(id).toLowerCase());
  if (!record) return id;
  const values = record.values || {};
  const name = zh ? (values.nameZh || values.nameEn || id) : (values.nameEn || values.nameZh || id);
  return (values.emoji ? values.emoji + " " : "") + name;
}
function tagEmoji(ids) {
  const definitions = tagById();
  for (const id of ids) {
    const emoji = definitions.get(String(id).toLowerCase())?.values?.emoji;
    if (emoji) return emoji;
  }
  return "📍";
}
function warpMessage(text, kind = "info") {
  const node = $("#warps-message");
  node.textContent = text || "";
  node.className = text ? `msg ${kind}` : "msg";
}

const viewBar = make("div", "warp-viewbar");
const mapName = make("span", "map-name");
const switcher = make("div", "view-switch");
const gpsButton = make("button", "", labels.gpsView);
const chartButton = make("button", "", labels.chartView);
const listButton = make("button", "", labels.listView);
const mapHost = make("div", "warp-map-shell");
for (const button of [gpsButton, chartButton, listButton]) button.type = "button";
switcher.setAttribute("role", "group");
switcher.setAttribute("aria-label", zh ? "传送点视图" : "Warp view");
switcher.append(gpsButton, chartButton, listButton);
viewBar.append(mapName, switcher);
$("#warp-grid").before(viewBar, mapHost);
gpsButton.onclick = () => setView("gps");
chartButton.onclick = () => setView("chart");
listButton.onclick = () => setView("list");

function setView(next) {
  warpState.view = next === "gps" || next === "chart" ? next : "list";
  try { sessionStorage.setItem(viewKey, warpState.view); } catch {}
  renderWarps();
}
function mapAvailable(kind) {
  return warpState.info[kind + "Available"] === "true";
}
function mapReason(kind) {
  const reason = warpState.info[kind + "Reason"] || warpState.info.mapReason || "unavailable";
  const names = kind === "gps" ? ["GPS 地图", "GPS map"] : ["Chart 地图", "chart"];
  const texts = {
    disabled:["互动地图已关闭。", "The interactive map is disabled."],
    locked:[`${names[0]}不可用。`, `The ${names[1]} is unavailable.`],
    missing:[`当前地图没有 ${kind === "gps" ? "Map.png" : "Chart.png"}。`, `The current map has no ${kind === "gps" ? "Map.png" : "Chart.png"}.`],
    "too-large":[`${names[0]}图片超过服务器发送上限。`, `The ${names[1]} image exceeds the server limit.`],
    unavailable:["当前地图尚未加载。", "The current map is not loaded."]
  };
  return (texts[reason] || texts.unavailable)[zh ? 0 : 1];
}
function mapAssetId(kind) {
  return warpState.info[kind + "AssetId"] || kind;
}
function readViewport() {
  try {
    const value = JSON.parse(sessionStorage.getItem(viewportKey) || "null");
    if (value && Number.isFinite(value.scale) && Number.isFinite(value.x) && Number.isFinite(value.y)) return value;
  } catch {}
  return { scale:1, x:0, y:0 };
}
function installPanZoom(stage, canvas, markers, controls) {
  const saved = readViewport();
  let scale = Math.max(1, Math.min(5, saved.scale));
  let xRatio = saved.x;
  let yRatio = saved.y;
  let x = xRatio * stage.clientWidth;
  let y = yRatio * stage.clientHeight;
  let dragging = false;
  let lastX = 0;
  let lastY = 0;
  const clamp = () => {
    x = Math.max(stage.clientWidth * (1 - scale), Math.min(0, x));
    y = Math.max(stage.clientHeight * (1 - scale), Math.min(0, y));
  };
  const render = () => {
    clamp();
    canvas.style.transform = `translate(${x}px,${y}px) scale(${scale})`;
    for (const marker of markers.querySelectorAll(".map-marker")) {
      marker.style.left = `${x + Number(marker.dataset.mapX) * stage.clientWidth * scale}px`;
      marker.style.top = `${y + Number(marker.dataset.mapY) * stage.clientHeight * scale}px`;
    }
  };
  const persist = () => {
    xRatio = x / (stage.clientWidth || 1);
    yRatio = y / (stage.clientHeight || 1);
    try { sessionStorage.setItem(viewportKey, JSON.stringify({ scale, x:xRatio, y:yRatio })); } catch {}
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
  controls.zoomIn.onclick = () => zoom(1.3);
  controls.zoomOut.onclick = () => zoom(1 / 1.3);
  controls.reset.onclick = () => { scale = 1; x = 0; y = 0; render(); persist(); };
  stage.addEventListener("wheel", event => {
    event.preventDefault();
    const rect = stage.getBoundingClientRect();
    zoom(event.deltaY < 0 ? 1.18 : 1 / 1.18, event.clientX - rect.left, event.clientY - rect.top);
  }, { passive:false });
  stage.addEventListener("pointerdown", event => {
    if (event.target.closest("button") || scale <= 1) return;
    dragging = true;
    lastX = event.clientX;
    lastY = event.clientY;
    stage.classList.add("dragging");
    stage.setPointerCapture(event.pointerId);
  });
  stage.addEventListener("pointermove", event => {
    if (!dragging) return;
    x += event.clientX - lastX;
    y += event.clientY - lastY;
    lastX = event.clientX;
    lastY = event.clientY;
    render();
  });
  const endDrag = () => { if (dragging) persist(); dragging = false; stage.classList.remove("dragging"); };
  stage.addEventListener("pointerup", endDrag);
  stage.addEventListener("pointercancel", endDrag);
  render();
  if (typeof ResizeObserver !== "undefined") {
    new ResizeObserver(() => {
      x = xRatio * stage.clientWidth;
      y = yRatio * stage.clientHeight;
      render();
    }).observe(stage);
  }
}

async function refreshWarps() {
  if (warpState.loading || $("#modal-root").children.length) return;
  warpState.loading = true;
  try {
    const [recordsResult, infoResult, tagsResult] = await Promise.all([
      panel.records("warps"),
      panel.values("warp-map-info"),
      panel.records("warp-tags")
    ]);
    warpState.records = recordsResult.records || [];
    warpState.info = infoResult.values || {};
    warpState.tagRecords = tagsResult.records || [];
    const groups = [...new Set(warpState.records.flatMap(tagsOf))];
    if (warpState.active !== allWarps && !groups.includes(warpState.active)) warpState.active = allWarps;
    renderWarps();
  } catch (error) {
    warpMessage(error.message || labels.requestFailed, "err");
  } finally {
    warpState.loading = false;
  }
}

function renderWarps() {
  const tabs = $("#warp-tabs");
  const grid = $("#warp-grid");
  const groups = [...new Set(warpState.records.flatMap(tagsOf))];
  tabs.replaceChildren();
  for (const [key, label] of [[allWarps, labels.all], ...groups.map(group => [group, tagDisplay(group)])]) {
    const amount = key === allWarps ? warpState.records.length : warpState.records.filter(record => tagsOf(record).includes(key)).length;
    const button = make("button", warpState.active === key ? "active" : "", `${label} · ${amount}`);
    button.type = "button";
    button.setAttribute("aria-pressed", warpState.active === key ? "true" : "false");
    button.onclick = () => { warpState.active = key; renderWarps(); };
    tabs.append(button);
  }
  const visible = warpState.records.filter(record => warpState.active === allWarps || tagsOf(record).includes(warpState.active));
  mapName.textContent = warpState.info.mapName ? `${labels.currentMap}: ${warpState.info.mapName}` : `${labels.currentMap}: —`;
  gpsButton.title = mapAvailable("gps") ? "" : mapReason("gps");
  chartButton.title = mapAvailable("chart") ? "" : mapReason("chart");
  for (const [button, id] of [[gpsButton, "gps"], [chartButton, "chart"], [listButton, "list"]]) {
    button.classList.toggle("active", warpState.view === id);
    button.setAttribute("aria-pressed", warpState.view === id ? "true" : "false");
  }
  grid.hidden = warpState.view !== "list";
  mapHost.hidden = warpState.view === "list";
  if (warpState.view === "list") renderList(visible);
  else renderMap(warpState.view, visible);
}

function renderList(visible) {
  const grid = $("#warp-grid");
  grid.replaceChildren();
  if (!visible.length) {
    grid.append(make("div", "empty", labels.empty));
    return;
  }
  for (const record of visible) grid.append(warpCard(record, visible));
}

function renderMap(kind, visible) {
  mapHost.replaceChildren();
  if (!mapAvailable(kind)) {
    mapHost.append(make("div", "map-empty", mapReason(kind)));
    return;
  }
  const stage = make("div", "warp-map-stage");
  const canvas = make("div", "warp-map-canvas");
  const image = document.createElement("img");
  const markers = make("div", "warp-map-markers");
  const controlsNode = make("div", "map-controls");
  const zoomOut = make("button", "", "−");
  const reset = make("button", "", labels.zoomReset);
  const zoomIn = make("button", "", "+");
  image.alt = warpState.info.mapName ? `${warpState.info.mapName} ${kind === "gps" ? "GPS" : "Chart"}` : "Current map";
  image.draggable = false;
  image.src = panel.assetUrl(mapAssetId(kind));
  image.onerror = () => mapHost.replaceChildren(make("div", "map-empty", zh ? "地图图片加载失败，请切换视图。" : "The map image failed to load. Switch views."));
  let count = 0;
  for (const record of visible) {
    const values = record.values || {};
    const mapX = Number(values.mapX);
    const mapY = Number(values.mapY);
    if (!Number.isFinite(mapX) || !Number.isFinite(mapY)) continue;
    count++;
    const marker = make("button", "map-marker");
    const pin = make("span", "map-marker-pin");
    pin.append(make("span", "map-marker-glyph", tagEmoji(tagsOf(record))));
    marker.type = "button";
    marker.dataset.mapX = String(mapX);
    marker.dataset.mapY = String(mapY);
    marker.setAttribute("aria-label", (zh ? "编辑传送点 " : "Edit warp ") + (record.label || record.key));
    marker.append(pin, make("span", "map-marker-label", record.label || record.key));
    marker.onclick = event => { event.stopPropagation(); openWarpModal(record); };
    markers.append(marker);
  }
  canvas.append(image);
  controlsNode.append(zoomOut, reset, zoomIn);
  stage.append(canvas, markers, controlsNode);
  mapHost.append(stage);
  if (!count) mapHost.append(make("div", "empty", labels.noMapPoints));
  installPanZoom(stage, canvas, markers, { zoomIn, zoomOut, reset });
}

function warpCard(record, visible) {
  const values = record.values || {};
  const ids = tagsOf(record);
  const card = make("article", "item warp-card");
  const title = make("div", "title", `${tagEmoji(ids)} ${record.label || record.key}`);
  const coords = make("div", "lines", `${values.map || "—"} · (${values.x || 0}, ${values.y || 0}, ${values.z || 0})`);
  const pills = make("div", "pills");
  const actions = make("div", "actions");
  const edit = make("button", "primary", labels.edit);
  const remove = make("button", "danger", labels.remove);
  for (const tag of ids) pills.append(make("span", "pill", tagDisplay(tag)));
  edit.type = remove.type = "button";
  edit.onclick = () => openWarpModal(record);
  remove.onclick = () => deleteWarp(record);
  actions.append(edit, remove);
  card.append(title, coords, pills, actions);
  card.draggable = true;
  card.addEventListener("dragstart", event => {
    warpState.dragKey = record.key;
    card.classList.add("dragging");
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", record.key);
  });
  card.addEventListener("dragend", () => { warpState.dragKey = ""; card.classList.remove("dragging"); });
  card.addEventListener("dragover", event => event.preventDefault());
  card.addEventListener("drop", event => {
    event.preventDefault();
    reorderWarps(warpState.dragKey || event.dataTransfer.getData("text/plain"), record.key, visible);
  });
  return card;
}

async function reorderWarps(fromKey, toKey, visible) {
  const from = visible.findIndex(item => item.key === fromKey);
  const to = visible.findIndex(item => item.key === toKey);
  if (from < 0 || to < 0 || from === to) return;
  const ordered = visible.slice();
  ordered.splice(to, 0, ordered.splice(from, 1)[0]);
  try {
    const result = await panel.api(`${modulePath("warps")}/reorder`, {
      method:"POST",
      headers:{ "Content-Type":"application/x-www-form-urlencoded" },
      body:bodyOf({ tag:warpState.active, keys:ordered.map(item => item.key).join("\n") }).toString()
    });
    if (!result.success) throw new Error(result.message || labels.requestFailed);
    await refreshWarps();
    warpMessage(result.message || "", "ok");
  } catch (error) {
    warpMessage(error.message || labels.requestFailed, "err");
  }
}

function warpField(form, label, name, value, type = "number", disabled = false) {
  const wrap = make("label", "field");
  const caption = make("span", "", label);
  const input = document.createElement("input");
  input.type = type;
  input.name = name;
  input.value = value ?? "";
  input.disabled = disabled;
  if (type === "number") input.step = "any";
  wrap.append(caption, input);
  form.append(wrap);
  return input;
}

function warpSelect(form, label, name, value, options) {
  const wrap = make("label", "field");
  wrap.append(make("span", "", label));
  const select = document.createElement("select");
  select.name = name;
  for (const [id, text] of options) {
    const option = document.createElement("option");
    option.value = id;
    option.textContent = text;
    select.append(option);
  }
  select.value = value;
  wrap.append(select);
  form.append(wrap);
  return select;
}

function createTagPicker(form, initial) {
  const selected = new Set(splitTags(initial));
  const known = new Set(warpState.tagRecords.map(record => String(record.values?.id || record.key).toLowerCase()));
  const wrap = make("label", "field tag-picker-field");
  wrap.append(make("span", "", labels.tags));
  const details = make("details", "tag-picker");
  const summary = make("summary", "tag-picker-summary");
  const menu = make("div", "tag-picker-menu");
  const choices = make("div", "tag-choices");
  const customRow = make("div", "tag-custom-row");
  const customInput = document.createElement("input");
  const add = make("button", "ghost", labels.add);
  const customPills = make("div", "pills tag-custom-pills");
  customInput.type = "text";
  customInput.placeholder = labels.customTagIds;
  add.type = "button";

  const updateSummary = () => {
    const values = [...selected];
    summary.textContent = values.length ? values.map(tagDisplay).join(" · ") : labels.selectTags;
  };
  const renderCustom = () => {
    customPills.replaceChildren();
    for (const id of [...selected].filter(value => !known.has(value))) {
      const pill = make("button", "pill removable", id + " ×");
      pill.type = "button";
      pill.onclick = () => { selected.delete(id); renderCustom(); updateSummary(); };
      customPills.append(pill);
    }
  };
  for (const record of warpState.tagRecords) {
    const id = String(record.values?.id || record.key).toLowerCase();
    const option = make("label", "tag-choice");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = selected.has(id);
    checkbox.onchange = () => {
      if (checkbox.checked) selected.add(id); else selected.delete(id);
      updateSummary();
    };
    option.append(checkbox, make("span", "", tagDisplay(id)));
    choices.append(option);
  }
  const addCustom = () => {
    for (const id of splitTags(customInput.value)) selected.add(id);
    customInput.value = "";
    renderCustom();
    updateSummary();
  };
  add.onclick = addCustom;
  customInput.addEventListener("keydown", event => {
    if (event.key === "Enter") { event.preventDefault(); addCustom(); }
  });
  customRow.append(customInput, add);
  menu.append(choices, customRow, customPills);
  details.append(summary, menu);
  wrap.append(details);
  form.append(wrap);
  renderCustom();
  updateSummary();
  return { value: () => [...selected].join(" ") };
}

function openWarpModal(record = null) {
  const values = record?.values || {};
  const host = $("#modal-root");
  const overlay = make("div", "modal-overlay");
  const modal = make("div", "modal");
  const head = make("div", "modal-head");
  const dismiss = () => host.replaceChildren();
  const close = make("button", "modal-close", "×");
  close.type = "button";
  close.onclick = dismiss;
  head.append(make("h3", "", record ? `${labels.edit}: ${record.label}` : labels.addWarp), close);
  modal.append(head);
  const form = make("div", "form-grid");
  const name = warpField(form, labels.name, "name", values.name || "", "text", !!record);
  const map = warpField(form, labels.map, "map", values.map || "", "text");
  const initialTags = values.tags || (warpState.active === allWarps ? "default" : warpState.active);
  const tags = createTagPicker(form, initialTags);
  const x = warpField(form, labels.x, "x", values.x || "0");
  const y = warpField(form, labels.y, "y", values.y || "0");
  const z = warpField(form, labels.z, "z", values.z || "0");
  const yaw = warpField(form, labels.yaw, "yaw", values.yaw || "0");
  modal.append(form);
  const status = make("div", "msg");
  const actions = make("div", "modal-actions");
  const cancel = make("button", "ghost", labels.cancel);
  const submit = make("button", "primary", labels.saveWarp);
  cancel.type = submit.type = "button";
  cancel.onclick = dismiss;
  submit.onclick = async () => {
    submit.disabled = true;
    try {
      const tagValue = tags.value();
      const result = await panel.invoke("warps", bodyOf({ name:name.value, map:map.value, tags:tagValue, x:x.value, y:y.value, z:z.value, yaw:yaw.value, recordKey:record?.key || "" }));
      if (!result.success) throw new Error(result.message || labels.requestFailed);
      dismiss();
      const savedTags = splitTags(tagValue);
      if (warpState.active !== allWarps && !savedTags.includes(warpState.active)) warpState.active = savedTags[0] || "default";
      await refreshWarps();
      warpMessage(result.message || "", "ok");
    } catch (error) {
      status.textContent = error.message || labels.requestFailed;
      status.className = "msg err";
      submit.disabled = false;
    }
  };
  actions.append(cancel, submit);
  modal.append(status, actions);
  overlay.append(modal);
  host.replaceChildren(overlay);
  bindOverlayDismiss(overlay, dismiss);
  name.focus();
}

function bindOverlayDismiss(overlay, dismiss) {
  let pressedOutside = false;
  overlay.addEventListener("pointerdown", event => { pressedOutside = event.target === overlay; });
  overlay.addEventListener("pointerup", event => {
    const closeNow = pressedOutside && event.target === overlay;
    pressedOutside = false;
    if (closeNow) dismiss();
  });
  overlay.addEventListener("pointercancel", () => { pressedOutside = false; });
}

async function deleteWarp(record) {
  if (!confirm(labels.confirmDelete.replace("{name}", record.label || record.key))) return;
  try {
    const result = await panel.api(`${modulePath("warps")}/delete`, {
      method:"POST",
      headers:{ "Content-Type":"application/x-www-form-urlencoded" },
      body:bodyOf({ key:record.key }).toString()
    });
    if (!result.success) throw new Error(result.message || labels.requestFailed);
    await refreshWarps();
    warpMessage(result.message || "", "ok");
  } catch (error) {
    warpMessage(error.message || labels.requestFailed, "err");
  }
}

function openTagManager() {
  const host = $("#modal-root");
  const overlay = make("div", "modal-overlay");
  const modal = make("div", "modal tag-library-modal");
  const head = make("div", "modal-head");
  const close = make("button", "modal-close", "×");
  const content = make("div", "tag-library-content");
  const dismiss = () => host.replaceChildren();
  close.type = "button";
  close.onclick = dismiss;
  head.append(make("div", "", ""), close);
  head.firstChild.append(make("h3", "", labels.tagLibrary), make("p", "desc", labels.tagHelp));
  modal.append(head, content);
  overlay.append(modal);
  host.replaceChildren(overlay);
  bindOverlayDismiss(overlay, dismiss);

  const render = (editing = null) => {
    content.replaceChildren();
    const toolbar = make("div", "tag-library-toolbar");
    const add = make("button", "primary", labels.addCustomTag);
    add.type = "button";
    add.onclick = () => renderEditor(null);
    toolbar.append(add);
    content.append(toolbar);
    for (const kind of ["preset", "custom"]) {
      const section = make("section", "tag-kind-section");
      section.append(make("h4", "", kind === "preset" ? labels.preset : labels.custom));
      const list = make("div", "tag-definition-list");
      const records = warpState.tagRecords.filter(record => (record.values?.kind || "custom") === kind);
      for (const record of records) {
        const values = record.values || {};
        const row = make("article", "tag-definition-row");
        const identity = make("div", "tag-definition-identity");
        identity.append(make("span", "tag-emoji", values.emoji || "—"), make("strong", "", zh ? values.nameZh : values.nameEn), make("code", "", values.id || record.key));
        const actions = make("div", "tag-definition-actions");
        const edit = make("button", "ghost", labels.edit);
        const remove = make("button", "danger", labels.remove);
        edit.type = remove.type = "button";
        edit.onclick = () => renderEditor(record);
        remove.onclick = () => deleteTag(record);
        actions.append(edit, remove);
        row.append(identity, actions);
        list.append(row);
      }
      if (!records.length) list.append(make("div", "empty", "—"));
      section.append(list);
      content.append(section);
    }
    if (editing) renderEditor(editing);
  };

  const reloadTags = async () => {
    const result = await panel.records("warp-tags");
    warpState.tagRecords = result.records || [];
  };

  const renderEditor = record => {
    const existing = content.querySelector(".tag-editor");
    if (existing) existing.remove();
    const values = record?.values || {};
    const editor = make("section", "tag-editor");
    const title = make("h4", "", record ? `${labels.edit}: ${values.id}` : labels.addCustomTag);
    const form = make("div", "form-grid");
    const id = warpField(form, labels.tagId, "id", values.id || "", "text", !!record);
    const kind = warpSelect(form, labels.kind, "kind", values.kind || "custom", [["preset", labels.preset], ["custom", labels.custom]]);
    const nameEn = warpField(form, labels.nameEn, "nameEn", values.nameEn || "", "text");
    const nameZh = warpField(form, labels.nameZh, "nameZh", values.nameZh || "", "text");
    const emoji = warpField(form, labels.emoji, "emoji", values.emoji || "", "text");
    const status = make("div", "msg");
    const actions = make("div", "modal-actions");
    const cancel = make("button", "ghost", labels.cancel);
    const submit = make("button", "primary", labels.saveTag);
    cancel.type = submit.type = "button";
    cancel.onclick = () => editor.remove();
    submit.onclick = async () => {
      submit.disabled = true;
      try {
        const result = await panel.invoke("warp-tags", bodyOf({
          id:id.value, kind:kind.value, nameEn:nameEn.value, nameZh:nameZh.value,
          emoji:emoji.value, recordKey:record?.key || ""
        }));
        if (!result.success) throw new Error(result.message || labels.requestFailed);
        await reloadTags();
        render();
      } catch (error) {
        status.textContent = error.message || labels.requestFailed;
        status.className = "msg err";
        submit.disabled = false;
      }
    };
    actions.append(cancel, submit);
    editor.append(title, form, status, actions);
    content.append(editor);
    id.focus();
  };

  const deleteTag = async record => {
    if (!confirm(labels.confirmDeleteTag.replace("{id}", record.key))) return;
    try {
      const result = await panel.api(`${modulePath("warp-tags")}/delete`, {
        method:"POST",
        headers:{ "Content-Type":"application/x-www-form-urlencoded" },
        body:bodyOf({ key:record.key }).toString()
      });
      if (!result.success) throw new Error(result.message || labels.tagInUse);
      await reloadTags();
      render();
    } catch (error) {
      const status = content.querySelector(".tag-library-status") || make("div", "tag-library-status msg err");
      status.textContent = error.message || labels.requestFailed;
      status.className = "tag-library-status msg err";
      content.prepend(status);
    }
  };

  render();
}

$("#add-warp").onclick = () => openWarpModal();
$("#manage-tags").onclick = openTagManager;
refreshWarps();
panel.onRefresh(refreshWarps);
