const root=panel.root.querySelector("#shop"),view=panel.view,cards=view.cards||[],zh=panel.lang==="zh";
const make=(t,c,x)=>{const n=document.createElement(t);if(c)n.className=c;if(x!=null)n.textContent=x;return n};
const top=make("div","top"),balance=make("div","balance",view.header||view.title);top.append(balance);if(view.message)top.append(make("div","notice",view.message));root.append(top);
const groups=[];for(const c of cards){const key=c.groupKey||c.group||"default";if(!groups.some(g=>g.key===key))groups.push({key,label:c.group||key})}
const stateKey="well404.shop.player.group";let saved="";try{saved=sessionStorage.getItem(stateKey)||""}catch{}
let active=groups.some(g=>g.key===saved)?saved:groups[0]?.key;const tabs=make("div","tabs"),bar=make("div","bar"),grid=make("div","grid");root.append(tabs,bar,grid);
function button(card,b){const n=make("button",b.style||"",b.label);n.onclick=()=>panel.invoke(card,b,n);return n}
function paint(){
  tabs.replaceChildren();bar.replaceChildren();grid.replaceChildren();
  for(const g of groups){const n=make("button",g.key===active?"active":"",g.label);n.onclick=()=>{active=g.key;try{sessionStorage.setItem(stateKey,active)}catch{}paint()};tabs.append(n)}
  const visible=cards.filter(c=>(c.groupKey||c.group||"default")===active);
  for(const c of visible.filter(c=>c.placement==="group-header"))for(const b of c.buttons||[])bar.append(button(c,b));
  for(const c of visible.filter(c=>c.placement!=="group-header")){
    const item=make("article","item"),name=make("div","name");if(c.badge)name.append(make("span","badge",c.badge));name.append(document.createTextNode(c.label));item.append(name);
    if(c.lines?.length)item.append(make("div","lines",c.lines.join(" · ")));
    if(c.tags?.length){const tags=make("div","tags");c.tags.forEach(x=>tags.append(make("span","tag",x)));item.append(tags)}
    const actions=make("div","actions");for(const b of c.buttons||[])actions.append(button(c,b));item.append(actions);grid.append(item)
  }
  if(!grid.children.length)grid.append(make("div","notice",zh?"该分组暂无商品":"No products in this group"));
}
paint();
