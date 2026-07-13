const zh=panel.lang==="zh";
const labels=zh
  ?{title:"实用工具",subtitle:"管理传送、队伍、礼包与世界时间规则。",teleport:"传送设置",rules:"其他规则",warps:"传送点",gifts:"礼包",search:"检索游戏物品",save:"保存设置"}
  :{title:"Essentials",subtitle:"Manage teleport, party, gifts and world-time rules.",teleport:"Teleport",rules:"Rules",warps:"Warps",gifts:"Gifts",search:"Item search",save:"Save settings"};
panel.root.querySelector("#title").textContent=labels.title;
panel.root.querySelector("#subtitle").textContent=labels.subtitle;
for(const button of panel.root.querySelectorAll("[data-tab]"))button.textContent=labels[button.dataset.tab];
const entries=[
  panel.mountAction("teleport",panel.root.querySelector("#teleport")),
  panel.mountAction("rules",panel.root.querySelector("#rules"))
];
panel.mountAction("warps",panel.root.querySelector("#warps"));
panel.mountAction("gifts",panel.root.querySelector("#gifts"));
panel.mountAction("search",panel.root.querySelector("#search"));
const save=panel.root.querySelector("#save");
save.textContent=labels.save;
save.onclick=()=>panel.saveSettings(entries,save);
let active="teleport";
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
