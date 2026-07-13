const zh=panel.lang==="zh";
const labels=zh
  ?{title:"个人仓库",subtitle:"管理基础容量、权限容量与玩家单独覆盖。",settings:"容量设置",overrides:"玩家覆盖",save:"保存容量设置"}
  :{title:"Personal vault",subtitle:"Manage base capacity, permission tiers and per-player overrides.",settings:"Capacity",overrides:"Player overrides",save:"Save capacity settings"};
panel.root.querySelector("#title").textContent=labels.title;
panel.root.querySelector("#subtitle").textContent=labels.subtitle;
for(const button of panel.root.querySelectorAll("[data-tab]"))button.textContent=labels[button.dataset.tab];
const entries=[panel.mountAction("settings",panel.root.querySelector("#settings"))];
panel.mountAction("overrides",panel.root.querySelector("#overrides"));
const save=panel.root.querySelector("#save");
save.textContent=labels.save;
save.onclick=()=>panel.saveSettings(entries,save);
let active="settings";
function select(id){
  active=id;
  for(const button of panel.root.querySelectorAll("[data-tab]")){
    const selected=button.dataset.tab===active;
    button.classList.toggle("active",selected);
    button.setAttribute("aria-selected",selected?"true":"false");
  }
  for(const section of panel.root.querySelectorAll("[data-panel]"))section.classList.toggle("active",section.dataset.panel===active);
}
for(const button of panel.root.querySelectorAll("[data-tab]"))button.onclick=()=>select(button.dataset.tab);
select(active);
