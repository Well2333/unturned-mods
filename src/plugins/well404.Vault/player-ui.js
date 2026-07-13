const root=panel.root.querySelector("#vault"),view=panel.view,cards=view.cards||[],zh=panel.lang==="zh";
const make=(t,c,x)=>{const n=document.createElement(t);if(c)n.className=c;if(x!=null)n.textContent=x;return n};
const top=make("div","top");top.append(make("div","capacity",view.header||view.title));if(view.message)top.append(make("div","notice",view.message));root.append(top);
const groups=[];for(const c of cards)if(!groups.includes(c.group||"Vault"))groups.push(c.group||"Vault");
const stateKey="well404.vault.player.group";let saved="";try{saved=sessionStorage.getItem(stateKey)||""}catch{}
let active=groups.includes(saved)?saved:groups[0];
const tabs=make("div","tabs"),grid=make("div","grid"),modalHost=panel.root.querySelector("#modal-root");root.append(tabs,grid);
function action(card,b){const n=make("button",b.style||"",b.label);n.onclick=event=>{event.stopPropagation();panel.invoke(card,b,n)};return n}
function item(card,openable=true){
 const n=make("article","item"),name=make("div","name");if(card.badge)name.append(make("span","badge",card.badge));name.append(document.createTextNode(card.label));n.append(name);
 if(card.tags?.length){const tags=make("div","tags");card.tags.forEach(x=>tags.append(make("span","tag",x)));n.append(tags)}
 const actions=make("div","actions");for(const b of card.buttons||[])actions.append(action(card,b));n.append(actions);
 if(openable&&card.children?.length){
   n.classList.add("folded");n.tabIndex=0;n.setAttribute("role","button");n.setAttribute("aria-label",zh?`查看 ${card.label} 的不同状态`:`View variants of ${card.label}`);
   name.append(make("span","open-mark","›"));
   n.onclick=()=>openVariants(card);
   n.onkeydown=event=>{if(event.target===n&&(event.key==="Enter"||event.key===" ")){event.preventDefault();openVariants(card)}};
 }
 return n
}
function openVariants(card){
 const overlay=make("div","variant-overlay"),modal=make("section","variant-modal"),head=make("div","variant-head"),heading=make("div","variant-heading"),title=make("div","variant-title"),close=make("button","variant-close","×");
 modal.setAttribute("role","dialog");modal.setAttribute("aria-modal","true");
 if(card.badge)title.append(make("span","badge",card.badge));title.append(document.createTextNode(card.label));
 heading.append(title,make("div","variant-help",zh?"选择一个具体状态；点击“数量”会继续打开数量输入窗口。":"Choose a specific variant. Amount opens a second quantity dialog."));
 close.type="button";close.setAttribute("aria-label",zh?"关闭":"Close");head.append(heading,close);
 const variants=make("div","variant-grid");for(const child of card.children||[])variants.append(item(child,false));
 modal.append(head,variants);overlay.append(modal);modalHost.replaceChildren(overlay);
 const dismiss=()=>modalHost.replaceChildren();
 const onKey=event=>{if(event.key==="Escape")dismiss()};close.onclick=dismiss;
 let pressedOutside=false;
 overlay.addEventListener("pointerdown",event=>{pressedOutside=event.target===overlay});
 overlay.addEventListener("pointerup",event=>{const shouldClose=pressedOutside&&event.target===overlay;pressedOutside=false;if(shouldClose)dismiss()});
 overlay.addEventListener("pointercancel",()=>{pressedOutside=false});
 overlay.addEventListener("keydown",onKey);close.focus();
}
function paint(){tabs.replaceChildren();grid.replaceChildren();for(const g of groups){const b=make("button",g===active?"active":"",g);b.onclick=()=>{active=g;try{sessionStorage.setItem(stateKey,active)}catch{}paint()};tabs.append(b)}
 modalHost.replaceChildren();const visible=cards.filter(c=>(c.group||"Vault")===active);for(const c of visible)grid.append(item(c));if(!visible.length)grid.append(make("div","notice",zh?"暂无物品":"No items"))}
paint();
