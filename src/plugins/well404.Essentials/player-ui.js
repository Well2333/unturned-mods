const app=panel.root.querySelector("#app"), view=panel.view, zh=panel.lang==="zh";
const sections=[
  ["travel",zh?"传送与位置":"Travel & locations",c=>c.key==="home"||c.key==="back"||c.key==="warps"||c.key.startsWith("warp:")],
  ["requests",zh?"待处理请求":"Pending requests",c=>c.key.startsWith("tpreq:")||c.key.startsWith("pinv:")],
  ["party",zh?"队伍":"Party",c=>c.key==="party"||c.key.startsWith("pmember:")],
  ["players",zh?"在线玩家":"Online players",c=>c.key==="noplayers"||c.key.startsWith("p:")],
  ["gifts",zh?"礼包":"Gifts",c=>c.key==="gifts"||c.key.startsWith("gift:")],
  ["sleep",zh?"世界时间":"World time",c=>c.key==="sleep"]
];
const make=(tag,cls,text)=>{const n=document.createElement(tag);if(cls)n.className=cls;if(text!=null)n.textContent=text;return n};
const hero=make("div","hero");const copy=make("div");copy.append(make("h2","",view.title),make("p","",view.header||""));hero.append(copy);app.append(hero);
if(view.message)app.append(make("div","notice",view.message));
const tabs=make("nav","tabs"),content=make("div","content");app.append(tabs,content);
function cardNode(card){
  const n=make("article","item"), title=make("div","title",card.label);n.append(title);
  if(card.lines?.length)n.append(make("div","lines",card.lines.join(" · ")));
  if(card.buttons?.length){const actions=make("div","actions");for(const b of card.buttons){const btn=make("button",b.style||"",b.label);btn.onclick=()=>panel.invoke(card,b,btn);actions.append(btn)}n.append(actions)}
  return n;
}
const models=sections.map(([id,label,match])=>({id,label,cards:(view.cards||[]).filter(match)}));
const stateKey="well404.essentials.player.tab";
let saved="";try{saved=sessionStorage.getItem(stateKey)||""}catch{}
let active=models.some(model=>model.id===saved)?saved:(models.find(model=>model.cards.length)?.id||models[0].id);
function paint(){
  tabs.replaceChildren();
  for(const model of models){
    const button=make("button",model.id===active?"active":"",model.label);
    button.type="button";
    button.setAttribute("aria-selected",model.id===active?"true":"false");
    button.onclick=()=>{active=model.id;try{sessionStorage.setItem(stateKey,active)}catch{}paint()};
    tabs.append(button);
  }
  const model=models.find(item=>item.id===active)||models[0];
  const panelNode=make("section","panel");
  const head=make("div","panel-head");
  head.append(make("h3","",model.label),make("span","count",String(model.cards.length)));
  const items=make("div","items");
  if(model.cards.length)model.cards.forEach(card=>items.append(cardNode(card)));
  else items.append(make("div","empty",zh?"暂无内容":"Nothing here"));
  panelNode.append(head,items);
  content.replaceChildren(panelNode);
}
paint();
