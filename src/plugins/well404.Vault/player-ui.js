const root=panel.root.querySelector("#vault"),view=panel.view,cards=view.cards||[],zh=panel.lang==="zh";
const make=(tag,className,text)=>{const node=document.createElement(tag);if(className)node.className=className;if(text!=null)node.textContent=text;return node};
const copy={
 all:zh?"全部":"All",empty:zh?"当前筛选下暂无物品":"No items match this filter",
 filter:zh?"物品类别":"Item category",sort:zh?"排序":"Sort",ascending:zh?"升序":"Ascending",descending:zh?"降序":"Descending",
 slots:zh?"总占用格数":"Total occupied slots",id:zh?"物品 ID":"Item ID",count:zh?"物品数量":"Item quantity",rarity:zh?"物品稀有度":"Item rarity",name:zh?"名称":"Name",
 categories:{all:zh?"全部":"All",ammunition:zh?"弹药":"Ammunition",food:zh?"食物与饮品":"Food & drink",medical:zh?"药品":"Medical",weapons:zh?"武器":"Weapons",materials:zh?"材料":"Materials",tools:zh?"工具":"Tools",clothing:zh?"服装":"Clothing",attachments:zh?"配件":"Attachments",building:zh?"建筑":"Building",vehicles:zh?"载具用品":"Vehicle items",other:zh?"其他":"Other"},
 rarities:{common:zh?"普通":"Common",uncommon:zh?"罕见":"Uncommon",rare:zh?"稀有":"Rare",epic:zh?"史诗":"Epic",legendary:zh?"传说":"Legendary",mythical:zh?"神话":"Mythical"}
};
const categories=["ammunition","food","medical","weapons","materials","tools","clothing","attachments","building","vehicles","other"];
const rarityKeys=["common","uncommon","rare","epic","legendary","mythical"];
const meta=card=>card&&card.metadata&&typeof card.metadata==="object"?card.metadata:{};
const number=(card,key)=>{const value=Number(meta(card)[key]);return Number.isFinite(value)?value:0};
const category=card=>categories.includes(meta(card).category)?meta(card).category:"other";
const rarity=card=>rarityKeys.includes(meta(card).rarity)?meta(card).rarity:"common";
const localizedName=text=>{const lines=String(text||"").split("\n").filter(Boolean),wrap=make("span","name-copy");const primary=!zh&&lines.length>1?lines[lines.length-1]:(lines[0]||"");wrap.append(make("span","name-primary",primary));if(zh&&lines.length>1)wrap.append(make("span","name-secondary",lines.slice(1).join(" ")));return wrap};
const top=make("div","top");top.append(make("div","capacity",view.header||view.title));if(view.message)top.append(make("div","notice",view.message));
const groups=[];for(const card of cards)if(!groups.includes(card.group||"Vault"))groups.push(card.group||"Vault");
const storage={group:"well404.vault.player.group",sort:"well404.vault.player.sort",direction:"well404.vault.player.direction",filter:"well404.vault.player.filter"};
const getStored=(key,fallback)=>{try{return sessionStorage.getItem(key)||fallback}catch{return fallback}};
const setStored=(key,value)=>{try{sessionStorage.setItem(key,value)}catch{}};
let active=getStored(storage.group,""),sortMode=getStored(storage.sort,"slots"),direction=getStored(storage.direction,""),filter=getStored(storage.filter,"all");
if(!groups.includes(active))active=groups[0];
if(!["slots","id","count","rarity","name"].includes(sortMode))sortMode="slots";
const defaultDirection=mode=>mode==="id"||mode==="name"?"asc":"desc";
if(direction!=="asc"&&direction!=="desc")direction=defaultDirection(sortMode);
if(filter!=="all"&&!categories.includes(filter))filter="all";
const tabs=make("div","tabs"),controls=make("div","controls"),filterRow=make("div","filter-row"),grid=make("div","grid"),modalHost=panel.root.querySelector("#modal-root");
const sortLabel=make("label","sort-control"),sortCaption=make("span","control-label",copy.sort),sortSelect=make("select","sort-select"),directionButton=make("button","direction-button");
for(const [value,label] of [["slots",copy.slots],["id",copy.id],["count",copy.count],["rarity",copy.rarity],["name",copy.name]]){const option=make("option","",label);option.value=value;sortSelect.append(option)}
sortSelect.value=sortMode;sortLabel.append(sortCaption,sortSelect);controls.append(sortLabel,directionButton);root.append(top,tabs,controls,filterRow,grid);
function updateDirectionButton(){const ascending=direction==="asc";directionButton.textContent=ascending?"↑":"↓";directionButton.title=ascending?copy.ascending:copy.descending;directionButton.setAttribute("aria-label",directionButton.title)}
sortSelect.onchange=()=>{sortMode=sortSelect.value;direction=defaultDirection(sortMode);setStored(storage.sort,sortMode);setStored(storage.direction,direction);updateDirectionButton();paint()};
directionButton.onclick=()=>{direction=direction==="asc"?"desc":"asc";setStored(storage.direction,direction);updateDirectionButton();paint()};
function action(card,button){const node=make("button",button.style||"",button.label);node.onclick=event=>{event.stopPropagation();panel.invoke(card,button,node)};return node}
function item(card,openable=true){
 const itemRarity=rarity(card),node=make("article","item rarity-"+itemRarity),name=make("div","name");
 node.dataset.rarity=itemRarity;node.dataset.category=category(card);node.title=(zh?"稀有度：":"Rarity: ")+copy.rarities[itemRarity];
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
 tabs.replaceChildren();grid.replaceChildren();filterRow.replaceChildren();modalHost.replaceChildren();
 for(const group of groups){const button=make("button",group===active?"active":"",group);button.onclick=()=>{active=group;setStored(storage.group,active);paint()};tabs.append(button)}
 const inGroup=cards.filter(card=>(card.group||"Vault")===active),counts={};for(const card of inGroup)counts[category(card)]=(counts[category(card)]||0)+number(card,"count");
 if(filter!=="all"&&!counts[filter])filter="all";
 const filterTitle=make("span","control-label",copy.filter);filterRow.append(filterTitle);
 for(const key of ["all",...categories]){const count=key==="all"?inGroup.reduce((sum,card)=>sum+number(card,"count"),0):(counts[key]||0);if(key!=="all"&&!count)continue;const button=make("button",key===filter?"active":"",copy.categories[key]+" · "+count);button.onclick=()=>{filter=key;setStored(storage.filter,filter);paint()};filterRow.append(button)}
 const visible=inGroup.filter(card=>filter==="all"||category(card)===filter).sort(compareCards);
 for(const card of visible)grid.append(item(card));if(!visible.length)grid.append(make("div","notice empty",copy.empty));
}
updateDirectionButton();paint();
