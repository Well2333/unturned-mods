const root=panel.root.querySelector("#vault"),view=panel.view,cards=view.cards||[],zh=panel.lang==="zh";
const make=(tag,className,text)=>{const node=document.createElement(tag);if(className)node.className=className;if(text!=null)node.textContent=text;return node};
const copy={
 all:zh?"全部":"All",empty:zh?"当前筛选下暂无物品":"No items match this filter",
 personal:zh?"个人仓库":"Personal vault",team:zh?"小队仓库":"Team vault",backpack:zh?"背包":"Backpack",vault:zh?"仓库":"Vault",
 filter:zh?"物品大类":"Item category",typeFilter:zh?"原生类型":"Native item type",allTypes:zh?"全部类型":"All types",containerFilter:zh?"身上容器":"Carried container",sort:zh?"排序":"Sort",ascending:zh?"升序":"Ascending",descending:zh?"降序":"Descending",
 slots:zh?"总占用格数":"Total occupied slots",id:zh?"物品 ID":"Item ID",count:zh?"物品数量":"Item quantity",rarity:zh?"物品稀有度":"Item rarity",name:zh?"名称":"Name",
 containers:{all:zh?"全部容器":"All containers",hands:zh?"手部":"Hands",backpack:zh?"背包":"Backpack",vest:zh?"胸挂":"Vest",shirt:zh?"上衣":"Shirt",pants:zh?"裤子":"Pants"},
 categories:{all:zh?"全部":"All",ammunition:zh?"弹药":"Ammunition",food:zh?"食物与饮品":"Food & drink",medical:zh?"药品":"Medical",weapons:zh?"武器":"Weapons",materials:zh?"材料":"Materials",tools:zh?"工具":"Tools",clothing:zh?"服装":"Clothing",attachments:zh?"配件":"Attachments",building:zh?"建筑":"Building",vehicles:zh?"载具用品":"Vehicle items",other:zh?"其他":"Other"},
 itemTypes:{HAT:zh?"帽子":"Hat",PANTS:zh?"裤子":"Pants",SHIRT:zh?"上衣":"Shirt",MASK:zh?"面罩":"Mask",BACKPACK:zh?"背包":"Backpack",VEST:zh?"胸挂":"Vest",GLASSES:zh?"眼镜":"Glasses",GUN:zh?"枪械":"Gun",SIGHT:zh?"瞄准镜":"Sight",TACTICAL:zh?"战术配件":"Tactical",GRIP:zh?"握把":"Grip",BARREL:zh?"枪口配件":"Barrel",MAGAZINE:zh?"弹匣":"Magazine",FOOD:zh?"食物":"Food",WATER:zh?"饮品":"Water",MEDICAL:zh?"医疗用品":"Medical",MELEE:zh?"近战武器":"Melee",FUEL:zh?"燃料":"Fuel",TOOL:zh?"工具":"Tool",BARRICADE:zh?"路障":"Barricade",STORAGE:zh?"储物设施":"Storage",BEACON:zh?"信标":"Beacon",TRAP:zh?"陷阱":"Trap",STRUCTURE:zh?"建筑结构":"Structure",SUPPLY:zh?"补给品":"Supply",THROWABLE:zh?"投掷物":"Throwable",OPTIC:zh?"光学配件":"Optic",FISHER:zh?"钓鱼工具":"Fisher",MAP:zh?"地图":"Map",KEY:zh?"钥匙":"Key",BOX:zh?"箱子":"Box",GENERATOR:zh?"发电机":"Generator",DETONATOR:zh?"引爆器":"Detonator",CHARGE:zh?"炸药":"Charge",FILTER:zh?"过滤器":"Filter",SENTRY:zh?"哨戒设施":"Sentry",VEHICLE_REPAIR_TOOL:zh?"载具维修工具":"Vehicle repair tool",TIRE:zh?"轮胎":"Tire",COMPASS:zh?"指南针":"Compass",OIL_PUMP:zh?"油泵":"Oil pump",VEHICLE_PAINT_TOOL:zh?"载具喷漆工具":"Vehicle paint tool",VEHICLE_LOCKPICK_TOOL:zh?"载具开锁工具":"Vehicle lockpick tool"},
 capacity:zh?"容量":"Capacity",buyCapacity:zh?"购买容量":"Buy capacity",targetBackpack:zh?"转移目标：背包":"Target: Backpack",targetTeam:zh?"转移目标：小队仓库":"Target: Team vault",targetPersonal:zh?"转移目标：个人仓库":"Target: Personal vault",
 moveToTeamConfirm:zh?"确定将所选物品转移到小队仓库吗？离开小队后可能无法再取回。":"Move the selected items to the team vault? You may lose access after leaving the team.",moveToPersonalConfirm:zh?"确定将所选物品转移到个人仓库吗？":"Move the selected items to your personal vault?",
 rarities:{common:zh?"普通":"Common",uncommon:zh?"罕见":"Uncommon",rare:zh?"稀有":"Rare",epic:zh?"史诗":"Epic",legendary:zh?"传说":"Legendary",mythical:zh?"神话":"Mythical"}
};
const categories=["ammunition","food","medical","weapons","materials","tools","clothing","attachments","building","vehicles","other"];
const nativeItemTypes=["HAT","PANTS","SHIRT","MASK","BACKPACK","VEST","GLASSES","GUN","SIGHT","TACTICAL","GRIP","BARREL","MAGAZINE","FOOD","WATER","MEDICAL","MELEE","FUEL","TOOL","BARRICADE","STORAGE","BEACON","FARM","TRAP","STRUCTURE","SUPPLY","THROWABLE","GROWER","OPTIC","REFILL","FISHER","CLOUD","MAP","KEY","BOX","ARREST_START","ARREST_END","TANK","GENERATOR","DETONATOR","CHARGE","LIBRARY","FILTER","SENTRY","VEHICLE_REPAIR_TOOL","TIRE","COMPASS","OIL_PUMP","VEHICLE_PAINT_TOOL","VEHICLE_LOCKPICK_TOOL"];
const containerKeys=["hands","backpack","vest","shirt","pants"];
const rarityKeys=["common","uncommon","rare","epic","legendary","mythical"];
const meta=card=>card&&card.metadata&&typeof card.metadata==="object"?card.metadata:{};
const scope=card=>meta(card).scope==="team"?"team":"personal";
const section=card=>meta(card).section==="backpack"?"backpack":"vault";
const inventoryContainer=card=>containerKeys.includes(meta(card).inventoryContainer)?meta(card).inventoryContainer:"backpack";
const number=(card,key)=>{const value=Number(meta(card)[key]);return Number.isFinite(value)?value:0};
const control=card=>String(meta(card).control||"");
const canMoveForScope=key=>cards.some(card=>scope(card)===key&&control(card)==="move_target"&&meta(card).enabled==="true");
const category=card=>categories.includes(meta(card).category)?meta(card).category:"other";
const itemType=card=>String(meta(card).itemType||"").trim().toUpperCase()||"UNKNOWN";
const itemTypeLabel=key=>zh&&copy.itemTypes[key]?copy.itemTypes[key]+" · "+key:(copy.itemTypes[key]||key);
const rarity=card=>rarityKeys.includes(meta(card).rarity)?meta(card).rarity:"common";
const localizedName=text=>{const lines=String(text||"").split("\n").filter(Boolean),wrap=make("span","name-copy");const primary=!zh&&lines.length>1?lines[lines.length-1]:(lines[0]||"");wrap.append(make("span","name-primary",primary));if(zh&&lines.length>1)wrap.append(make("span","name-secondary",lines.slice(1).join(" ")));return wrap};
const notice=view.message?make("div","notice",view.message):null;
const scopes=[];for(const card of cards)if(!scopes.includes(scope(card)))scopes.push(scope(card));if(!scopes.length)scopes.push("personal");const hasBothScopes=scopes.includes("personal")&&scopes.includes("team");
const storage={scope:"well404.vault.player.scope",section:"well404.vault.player.section",sort:"well404.vault.player.sort",direction:"well404.vault.player.direction",filter:"well404.vault.player.filter",typeFilter:"well404.vault.player.type-filter",target:"well404.vault.player.target",hiddenContainers:"well404.vault.player.hidden-containers"};
const getStored=(key,fallback)=>{try{return sessionStorage.getItem(key)||fallback}catch{return fallback}};
const setStored=(key,value)=>{try{sessionStorage.setItem(key,value)}catch{}};
let activeScope=getStored(storage.scope,""),activeSection=getStored(storage.section,"vault"),sortMode=getStored(storage.sort,"slots"),direction=getStored(storage.direction,""),filter=getStored(storage.filter,"all"),typeFilter=getStored(storage.typeFilter,"all"),target=getStored(storage.target,"backpack");
const hiddenContainers=new Set(getStored(storage.hiddenContainers,"").split(",").filter(key=>containerKeys.includes(key)));
const persistHiddenContainers=()=>setStored(storage.hiddenContainers,[...hiddenContainers].join(","));
if(!scopes.includes(activeScope))activeScope=scopes[0];
if(!["backpack","vault"].includes(activeSection))activeSection="vault";
if(!["slots","id","count","rarity","name"].includes(sortMode))sortMode="slots";
const defaultDirection=mode=>mode==="id"||mode==="name"?"asc":"desc";
if(direction!=="asc"&&direction!=="desc")direction=defaultDirection(sortMode);
if(filter!=="all"&&!categories.includes(filter))filter="all";if(!typeFilter)typeFilter="all";if((target!=="backpack"&&target!=="other")||!hasBothScopes||!canMoveForScope(activeScope)){target="backpack";setStored(storage.target,target)}
const resetItemFilters=()=>{filter="all";typeFilter="all";setStored(storage.filter,filter);setStored(storage.typeFilter,typeFilter)};
const scopeTabs=make("div","scope-tabs"),sectionBar=make("div","section-bar"),tabs=make("div","tabs"),capacityText=make("span","capacity-inline"),upgradeHost=make("div","upgrade-host"),targetHost=make("div","target-host"),controls=make("div","controls"),containerRow=make("div","filter-row container-filter-row"),filterRow=make("div","filter-row category-filter-row"),typeFilterRow=make("div","filter-row type-filter-row"),grid=make("div","grid"),modalHost=panel.root.querySelector("#modal-root");
const sortLabel=make("label","sort-control"),sortCaption=make("span","control-label",copy.sort),sortSelect=make("select","sort-select"),directionButton=make("button","direction-button");
for(const [value,label] of [["slots",copy.slots],["id",copy.id],["count",copy.count],["rarity",copy.rarity],["name",copy.name]]){const option=make("option","",label);option.value=value;sortSelect.append(option)}
sortSelect.value=sortMode;sortLabel.append(sortCaption,sortSelect);controls.append(sortLabel,directionButton);sectionBar.append(tabs,capacityText,upgradeHost,targetHost);root.append(scopeTabs,sectionBar,controls,containerRow,filterRow,typeFilterRow,grid);if(notice)root.insertBefore(notice,controls);
function updateDirectionButton(){const ascending=direction==="asc";directionButton.textContent=ascending?"↑":"↓";directionButton.title=ascending?copy.ascending:copy.descending;directionButton.setAttribute("aria-label",directionButton.title)}
sortSelect.onchange=()=>{sortMode=sortSelect.value;direction=defaultDirection(sortMode);setStored(storage.sort,sortMode);setStored(storage.direction,direction);updateDirectionButton();paint()};
directionButton.onclick=()=>{direction=direction==="asc"?"desc":"asc";setStored(storage.direction,direction);updateDirectionButton();paint()};
function action(card,button){const node=make("button",button.style||"",button.label);node.onclick=event=>{event.stopPropagation();let routed=button;if(activeSection==="vault"&&target==="other"&&hasBothScopes&&canMoveForScope(activeScope)&&(button.actionId||"").includes("_")){const op=button.actionId.slice(button.actionId.lastIndexOf("_")+1),mode=activeScope==="personal"?"movetoteam":"movetopersonal";routed={...button,actionId:mode+"_"+op,confirmation:activeScope==="personal"?copy.moveToTeamConfirm:copy.moveToPersonalConfirm}}panel.invoke(card,routed,node)};return node}
function item(card,openable=true){
 const itemRarity=rarity(card),node=make("article","item rarity-"+itemRarity),name=make("div","name");
 node.dataset.rarity=itemRarity;node.dataset.category=category(card);node.dataset.itemType=itemType(card);node.title=(zh?"稀有度：":"Rarity: ")+copy.rarities[itemRarity];
 if(card.badge)name.append(make("span","badge",card.badge));name.append(localizedName(card.label));node.append(name);
 const tags=make("div","tags"),rarityTag=make("span","tag rarity-label rarity-"+itemRarity,copy.rarities[itemRarity]);tags.append(rarityTag);
 for(const value of card.tags||[])tags.append(make("span","tag",value));node.append(tags);
 const actions=make("div","actions");for(const button of card.buttons||[])actions.append(action(card,button));node.append(actions);
 if(openable&&card.children?.length){
   node.classList.add("folded");node.tabIndex=0;node.setAttribute("role","button");node.setAttribute("aria-label",(zh?"查看不同状态：":"View variants: ")+card.label);
   name.append(make("span","open-mark","›"));node.onclick=()=>openVariants(card);
   node.onkeydown=event=>{if(event.target===node&&(event.key==="Enter"||event.key===" ")){event.preventDefault();openVariants(card)}};
 }
 return node
}
function openVariants(card){
 const overlay=make("div","variant-overlay"),modal=make("section","variant-modal"),head=make("div","variant-head"),heading=make("div","variant-heading"),title=make("div","variant-title"),close=make("button","variant-close","×");
 modal.setAttribute("role","dialog");modal.setAttribute("aria-modal","true");
 if(card.badge)title.append(make("span","badge",card.badge));title.append(localizedName(card.label));
 heading.append(title,make("div","variant-help",zh?"选择一个具体状态；点击“数量”会继续打开数量输入窗口。":"Choose a specific variant. Amount opens a second quantity dialog."));
 close.type="button";close.setAttribute("aria-label",zh?"关闭":"Close");head.append(heading,close);
 const variants=make("div","variant-grid");for(const child of card.children||[])variants.append(item(child,false));
 modal.append(head,variants);overlay.append(modal);modalHost.replaceChildren(overlay);
 const dismiss=()=>modalHost.replaceChildren();close.onclick=dismiss;
 let pressedOutside=false;
 overlay.addEventListener("pointerdown",event=>{pressedOutside=event.target===overlay});
 overlay.addEventListener("pointerup",event=>{const shouldClose=pressedOutside&&event.target===overlay;pressedOutside=false;if(shouldClose)dismiss()});
 overlay.addEventListener("pointercancel",()=>{pressedOutside=false});
 overlay.addEventListener("keydown",event=>{if(event.key==="Escape")dismiss()});close.focus();
}
function compareCards(left,right){
 let result=0;
 if(sortMode==="slots")result=number(left,"totalSlots")-number(right,"totalSlots");
 else if(sortMode==="id")result=number(left,"itemId")-number(right,"itemId");
 else if(sortMode==="count")result=number(left,"count")-number(right,"count");
 else if(sortMode==="rarity")result=number(left,"rarityRank")-number(right,"rarityRank");
 else result=String(left.label||"").localeCompare(String(right.label||""),panel.lang||"en",{sensitivity:"base",numeric:true});
 if(result!==0)return direction==="asc"?result:-result;
 return number(left,"itemId")-number(right,"itemId");
}
function paint(){
 scopeTabs.replaceChildren();tabs.replaceChildren();upgradeHost.replaceChildren();targetHost.replaceChildren();grid.replaceChildren();containerRow.replaceChildren();filterRow.replaceChildren();typeFilterRow.replaceChildren();modalHost.replaceChildren();
 for(const key of scopes){const button=make("button",key===activeScope?"active":"",copy[key]);button.onclick=()=>{activeScope=key;setStored(storage.scope,key);resetItemFilters();if(!canMoveForScope(key)){target="backpack";setStored(storage.target,target)}paint()};scopeTabs.append(button)}
 const availableSections=["backpack","vault"].filter(key=>cards.some(card=>scope(card)===activeScope&&section(card)===key));if(!availableSections.includes(activeSection))activeSection=availableSections[0]||"vault";
 for(const key of availableSections){const button=make("button",key===activeSection?"active":"",copy[key]);button.onclick=()=>{activeSection=key;setStored(storage.section,key);resetItemFilters();paint()};tabs.append(button)}
 const marker=cards.find(card=>scope(card)===activeScope&&section(card)===activeSection&&control(card)==="scope_marker"),markerMeta=meta(marker);capacityText.textContent=copy.capacity+" "+(markerMeta.usedSlots||"0")+" / "+(markerMeta.maxSlots||"0");
 const tool=cards.find(card=>scope(card)===activeScope&&section(card)===activeSection&&control(card)==="upgrade");if(tool&&tool.buttons?.length){const button=make("button","upgrade-button",copy.buyCapacity+" +"+(meta(tool).upgradeSlots||"")+" · "+(meta(tool).upgradePrice||""));button.onclick=()=>panel.invoke(tool,tool.buttons[0],button);upgradeHost.append(button)}
 if(activeSection==="vault"&&hasBothScopes&&canMoveForScope(activeScope)){const button=make("button","target-button",target==="backpack"?copy.targetBackpack:(activeScope==="personal"?copy.targetTeam:copy.targetPersonal));button.onclick=()=>{target=target==="backpack"?"other":"backpack";setStored(storage.target,target);paint()};targetHost.append(button)}
 const inGroup=cards.filter(card=>scope(card)===activeScope&&section(card)===activeSection&&!control(card));
 if(activeSection==="backpack"){
   const counts={};for(const card of inGroup)counts[inventoryContainer(card)]=(counts[inventoryContainer(card)]||0)+number(card,"count");
   const total=inGroup.reduce((sum,card)=>sum+number(card,"count"),0),allButton=make("button",hiddenContainers.size?"":"active",copy.containers.all+" · "+total);allButton.type="button";allButton.setAttribute("aria-pressed",hiddenContainers.size?"false":"true");allButton.onclick=()=>{hiddenContainers.clear();persistHiddenContainers();paint()};containerRow.append(make("span","control-label",copy.containerFilter),allButton);
   for(const key of containerKeys){const hidden=hiddenContainers.has(key),button=make("button",hidden?"":"active",copy.containers[key]+" · "+(counts[key]||0));button.type="button";button.setAttribute("aria-pressed",hidden?"false":"true");button.onclick=()=>{hidden?hiddenContainers.delete(key):hiddenContainers.add(key);persistHiddenContainers();paint()};containerRow.append(button)}
 }
 const sourceGroup=activeSection==="backpack"?inGroup.filter(card=>!hiddenContainers.has(inventoryContainer(card))):inGroup,counts={};for(const card of sourceGroup)counts[category(card)]=(counts[category(card)]||0)+number(card,"count");
 if(filter!=="all"&&!counts[filter]){filter="all";typeFilter="all"}
 const filterTitle=make("span","control-label",copy.filter);filterRow.append(filterTitle);
 for(const key of ["all",...categories]){const count=key==="all"?sourceGroup.reduce((sum,card)=>sum+number(card,"count"),0):(counts[key]||0);if(key!=="all"&&!count)continue;const button=make("button",key===filter?"active":"",copy.categories[key]+" · "+count);button.onclick=()=>{filter=key;typeFilter="all";setStored(storage.filter,filter);setStored(storage.typeFilter,typeFilter);paint()};filterRow.append(button)}
 const categoryGroup=filter==="all"?sourceGroup:sourceGroup.filter(card=>category(card)===filter),typeCounts={};for(const card of categoryGroup){const key=itemType(card);typeCounts[key]=(typeCounts[key]||0)+number(card,"count")}
 if(typeFilter!=="all"&&!typeCounts[typeFilter])typeFilter="all";
 typeFilterRow.append(make("span","control-label",copy.typeFilter));
 const knownTypes=nativeItemTypes.filter(key=>typeCounts[key]),unknownTypes=Object.keys(typeCounts).filter(key=>!nativeItemTypes.includes(key)).sort(),availableTypes=[...knownTypes,...unknownTypes];
 for(const key of ["all",...availableTypes]){const count=key==="all"?categoryGroup.reduce((sum,card)=>sum+number(card,"count"),0):typeCounts[key];const button=make("button",key===typeFilter?"active":"",(key==="all"?copy.allTypes:itemTypeLabel(key))+" · "+count);button.onclick=()=>{typeFilter=key;setStored(storage.typeFilter,typeFilter);paint()};typeFilterRow.append(button)}
 const visible=categoryGroup.filter(card=>typeFilter==="all"||itemType(card)===typeFilter).sort(compareCards);
 for(const card of visible)grid.append(item(card));if(!visible.length)grid.append(make("div","notice empty",copy.empty));
}
updateDirectionButton();paint();
