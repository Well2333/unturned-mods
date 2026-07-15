const root=panel.root,zh=panel.lang==="zh";
const L=zh?{
  title:"商店管理",subtitle:"按照玩家看到的分组目录管理商品、排序、价格与折扣。",catalog:"商品目录",discount:"折扣",saveDiscount:"保存折扣设置",
  catalogTitle:"玩家商店目录",catalogHelp:"目录与玩家面板完全同步；在当前分组内拖动商品卡片即可排序。",addGroup:"＋ 添加分组",editGroup:"编辑分组",
  addTitle:"添加物品",addHelp:"输入游戏物品名称或数字 ID，选择结果后设置价格、分组与备注。",searchLabel:"物品名称或 ID",searchPlaceholder:"输入关键词或数字 ID…",
  noProducts:"当前分组暂无商品。",searchHint:"输入名称或 ID 后，这里会显示匹配物品及其 ID。",noResults:"没有匹配的游戏物品。",add:"添加",edit:"编辑",remove:"删除",cancel:"取消",save:"保存",
  addGroupTitle:"添加分组",editGroupTitle:"编辑分组",groupId:"分组 ID",groupName:"分组名称",groupNameHint:"留空时使用分组 ID",deleteGroup:"删除分组",deleteGroupConfirm:"删除该分组？其中商品会移动到 default。",
  editProduct:"编辑商品",addItemTitle:"添加商品",itemId:"物品 ID",group:"分组",note:"备注",buyPrice:"买价",sellPrice:"卖价",deleteProductConfirm:"确定删除该商品？",
  buy:"买价",sell:"卖价",saved:"已保存。",deleted:"已删除。",orderSaved:"排序已保存。",loading:"加载中…",requestFailed:"请求失败。",defaultGroupLocked:"default 分组不能删除。"
}:{
  title:"Shop management",subtitle:"Manage products, ordering, prices and discounts in the same grouped catalog players see.",catalog:"Catalog",discount:"Discounts",saveDiscount:"Save discount settings",
  catalogTitle:"Player shop catalog",catalogHelp:"This catalog exactly matches the player panel. Drag cards inside the current group to reorder them.",addGroup:"＋ Add group",editGroup:"Edit group",
  addTitle:"Add item",addHelp:"Search by game item name or numeric ID, then set its prices, group and note.",searchLabel:"Item name or ID",searchPlaceholder:"Type a keyword or numeric ID…",
  noProducts:"No products in this group.",searchHint:"Enter a name or ID to show matching game items and their IDs here.",noResults:"No matching game items.",add:"Add",edit:"Edit",remove:"Delete",cancel:"Cancel",save:"Save",
  addGroupTitle:"Add group",editGroupTitle:"Edit group",groupId:"Group ID",groupName:"Group name",groupNameHint:"Empty uses the group ID",deleteGroup:"Delete group",deleteGroupConfirm:"Delete this group? Its products will move to default.",
  editProduct:"Edit product",addItemTitle:"Add item",itemId:"Item ID",group:"Group",note:"Note",buyPrice:"Buy price",sellPrice:"Sell price",deleteProductConfirm:"Delete this product?",
  buy:"Buy",sell:"Sell",saved:"Saved.",deleted:"Deleted.",orderSaved:"Order saved.",loading:"Loading…",requestFailed:"Request failed.",defaultGroupLocked:"The default group cannot be deleted."
};
const make=(tag,className,text)=>{const node=document.createElement(tag);if(className)node.className=className;if(text!=null)node.textContent=text;return node};
const localizedName=text=>{const lines=String(text||"").split("\n").filter(Boolean),wrap=make("span","name-copy");const primary=!zh&&lines.length>1?lines[lines.length-1]:(lines[0]||"");wrap.append(make("span","name-primary",primary));if(zh&&lines.length>1)wrap.append(make("span","name-secondary",lines.slice(1).join(" ")));return wrap};
const $=selector=>root.querySelector(selector);
const modulePath=id=>`api/modules/${panel.encode(panel.module.id)}/${panel.encode(id)}`;
const state={groups:[],records:[],activeGroup:"",dragKey:"",loading:false};
const groupStateKey="well404.shop.admin.group",tabStateKey="well404.shop.admin.tab";
let searchTimer=null,searchSerial=0;

$("#title").textContent=L.title;
$("#subtitle").textContent=L.subtitle;
$("#catalog-title").textContent=L.catalogTitle;
$("#catalog-help").textContent=L.catalogHelp;
$("#add-group").textContent=L.addGroup;
$("#edit-group").textContent=L.editGroup;
$("#add-title").textContent=L.addTitle;
$("#add-help").textContent=L.addHelp;
$("#search-label").textContent=L.searchLabel;
$("#item-search").placeholder=L.searchPlaceholder;
$("#save").textContent=L.saveDiscount;
for(const button of root.querySelectorAll("[data-tab]"))button.textContent=button.dataset.tab==="catalog"?L.catalog:L.discount;

const discountEntry=panel.mountAction("discount",$("#settings"));
$("#save").onclick=()=>panel.saveSettings([discountEntry],$("#save"));

function selectTopTab(id){
  for(const button of root.querySelectorAll("[data-tab]"))button.classList.toggle("active",button.dataset.tab===id);
  for(const section of root.querySelectorAll("[data-panel]"))section.classList.toggle("active",section.dataset.panel===id);
  try{sessionStorage.setItem(tabStateKey,id)}catch{}
}
let initialTab="catalog";
try{const saved=sessionStorage.getItem(tabStateKey);if(saved==="discount")initialTab=saved}catch{}
for(const button of root.querySelectorAll("[data-tab]"))button.onclick=()=>selectTopTab(button.dataset.tab);
selectTopTab(initialTab);

function showMessage(target,text,kind="info"){
  target.textContent=text||"";
  target.className=text?`msg ${kind}`:"msg";
}
function bodyOf(values){
  const body=new URLSearchParams();
  for(const [key,value] of Object.entries(values))body.set(key,value==null?"":String(value));
  return body;
}
async function postEndpoint(action,suffix,values){
  const body=bodyOf(values).toString()||"_=1";
  return panel.api(`${modulePath(action)}/${suffix}`,{
    method:"POST",headers:{"Content-Type":"application/x-www-form-urlencoded"},body
  });
}
function currentGroup(){return state.groups.find(group=>group.key===state.activeGroup)||state.groups[0]||null}
function ensureActiveGroup(){
  if(state.groups.some(group=>group.key===state.activeGroup))return;
  let saved="";
  try{saved=sessionStorage.getItem(groupStateKey)||""}catch{}
  state.activeGroup=state.groups.some(group=>group.key===saved)?saved:(state.groups[0]?.key||"");
}
function setActiveGroup(id){
  state.activeGroup=id;
  try{sessionStorage.setItem(groupStateKey,id)}catch{}
  renderCatalog();
}

async function refresh(){
  if(state.loading||$("#modal-root").children.length)return;
  state.loading=true;
  try{
    const [groups,catalog]=await Promise.all([panel.records("groups"),panel.records("catalog")]);
    state.groups=groups.records||[];
    state.records=catalog.records||[];
    ensureActiveGroup();
    renderCatalog();
  }catch(error){
    showMessage($("#catalog-message"),error.message||L.requestFailed,"err");
  }finally{
    state.loading=false;
  }
}

function renderCatalog(){
  const tabs=$("#group-tabs"),grid=$("#catalog-grid");
  tabs.replaceChildren();
  grid.replaceChildren();
  for(const group of state.groups){
    const button=make("button",group.key===state.activeGroup?"active":"",group.label||group.key);
    button.type="button";
    button.onclick=()=>setActiveGroup(group.key);
    tabs.append(button);
  }
  const visible=state.records.filter(record=>
    record.placement!=="group-header"&&(record.groupKey||record.group||"default")===state.activeGroup);
  if(!visible.length){
    grid.append(make("div","empty",L.noProducts));
    return;
  }
  for(const record of visible)grid.append(productCard(record,visible));
}

function productCard(record,visible){
  const card=make("article","product-card"),title=make("div","product-name");
  const values=record.values||{};
  const badge=`#${values.itemId||record.key.slice(5)}`;
  title.append(make("span","badge",badge),localizedName(record.label||badge));
  card.append(title,make("div","product-note",values.note||""));
  const meta=make("div","meta");
  for(const tag of record.tags||[]){
    if(tag!==badge)meta.append(make("span","pill",tag));
  }
  meta.append(
    make("span","pill price",`${L.buy}: ${values.buyPrice||0}`),
    make("span","pill price",`${L.sell}: ${values.sellPrice||0}`)
  );
  card.append(meta);
  const actions=make("div","product-actions"),edit=make("button","ghost",L.edit),remove=make("button","ghost",L.remove);
  edit.type="button";
  remove.type="button";
  edit.onclick=()=>openProductModal(record);
  remove.onclick=()=>deleteProduct(record);
  actions.append(edit,remove);
  card.append(actions);
  card.draggable=true;
  card.addEventListener("dragstart",event=>{
    state.dragKey=record.key;
    card.classList.add("dragging");
    event.dataTransfer.effectAllowed="move";
    event.dataTransfer.setData("text/plain",record.key);
  });
  card.addEventListener("dragend",()=>{
    state.dragKey="";
    card.classList.remove("dragging");
    for(const item of root.querySelectorAll(".drop-target"))item.classList.remove("drop-target");
  });
  card.addEventListener("dragover",event=>{event.preventDefault();card.classList.add("drop-target")});
  card.addEventListener("dragleave",()=>card.classList.remove("drop-target"));
  card.addEventListener("drop",event=>{
    event.preventDefault();
    card.classList.remove("drop-target");
    reorder(state.dragKey||event.dataTransfer.getData("text/plain"),record.key,visible);
  });
  return card;
}

async function reorder(fromKey,toKey,visible){
  const from=visible.findIndex(item=>item.key===fromKey),to=visible.findIndex(item=>item.key===toKey);
  if(from<0||to<0||from===to)return;
  const ordered=visible.slice();
  ordered.splice(to,0,ordered.splice(from,1)[0]);
  try{
    const result=await postEndpoint("catalog","reorder",{
      group:state.activeGroup,keys:ordered.map(item=>item.key).join("\n")
    });
    if(!result.success)throw new Error(result.message||L.requestFailed);
    showMessage($("#catalog-message"),L.orderSaved,"ok");
    await refresh();
  }catch(error){
    showMessage($("#catalog-message"),error.message||L.requestFailed,"err");
  }
}

function field(form,label,name,type="text",value="",options=null,full=false,disabled=false,placeholder=""){
  const wrap=make("label",`field${full?" full":""}`),caption=make("span","",label);
  let input;
  if(options){
    input=document.createElement("select");
    for(const option of options){
      const node=document.createElement("option");
      node.value=option.value;
      node.textContent=option.label;
      input.append(node);
    }
  }else if(type==="textarea"){
    input=document.createElement("textarea");
  }else{
    input=document.createElement("input");
    input.type=type;
    if(type==="number")input.step="any";
  }
  input.name=name;
  input.value=value??"";
  input.disabled=disabled;
  if(placeholder)input.placeholder=placeholder;
  wrap.append(caption,input);
  form.append(wrap);
  return input;
}
function groupOptions(){return state.groups.map(group=>({value:group.key,label:group.label||group.key}))}
function openModal(title,build){
  const host=$("#modal-root"),overlay=make("div","modal-overlay"),modal=make("div","modal");
  modal.setAttribute("role","dialog");modal.setAttribute("aria-modal","true");
  const head=make("div","modal-head"),heading=make("h3","",title),close=make("button","modal-close","×");
  close.type="button";
  const dismiss=()=>host.replaceChildren();
  close.onclick=dismiss;
  head.append(heading,close);
  modal.append(head);
  overlay.append(modal);
  host.replaceChildren(overlay);
  let pressedOutside=false;
  overlay.addEventListener("pointerdown",event=>{pressedOutside=event.target===overlay});
  overlay.addEventListener("pointerup",event=>{
    const releasedOutside=event.target===overlay;
    const shouldDismiss=pressedOutside&&releasedOutside;
    pressedOutside=false;
    if(shouldDismiss)dismiss();
  });
  overlay.addEventListener("pointercancel",()=>{pressedOutside=false});
  build(modal,dismiss);
}
function modalActions(modal,onSave,onDelete=null){
  const message=make("div","msg"),actions=make("div","modal-actions");
  if(onDelete){
    const del=make("button","danger",onDelete.label);
    del.type="button";
    del.onclick=()=>onDelete.run(del,message);
    actions.append(del);
  }
  const cancel=make("button","ghost",L.cancel),save=make("button","",L.save);
  cancel.type="button";
  save.type="button";
  cancel.onclick=()=>$("#modal-root").replaceChildren();
  save.onclick=()=>onSave(save,message);
  actions.append(cancel,save);
  modal.append(message,actions);
}

function openGroupModal(record=null){
  openModal(record?L.editGroupTitle:L.addGroupTitle,(modal,dismiss)=>{
    const form=make("div","form-grid");
    const id=field(form,L.groupId,"id","text",record?.values?.id||"",null,false,!!record);
    const name=field(form,L.groupName,"name","text",record?.values?.name||"",null,false,false,L.groupNameHint);
    modal.append(form);
    modalActions(modal,async(button,message)=>{
      button.disabled=true;
      try{
        const result=await panel.invoke("groups",bodyOf({id:id.value,name:name.value}));
        if(!result.success)throw new Error(result.message||L.requestFailed);
        dismiss();
        await refresh();
        showMessage($("#catalog-message"),L.saved,"ok");
      }catch(error){
        showMessage(message,error.message||L.requestFailed,"err");
        button.disabled=false;
      }
    },record&&record.key!=="default"?{
      label:L.deleteGroup,
      run:async(button,message)=>{
        if(!confirm(L.deleteGroupConfirm))return;
        button.disabled=true;
        try{
          const result=await postEndpoint("groups","delete",{key:record.key});
          if(!result.success)throw new Error(result.message||L.requestFailed);
          dismiss();
          await refresh();
          showMessage($("#catalog-message"),L.deleted,"ok");
        }catch(error){
          showMessage(message,error.message||L.requestFailed,"err");
          button.disabled=false;
        }
      }
    }:null);
    id.focus();
  });
}

function openProductModal(record=null,preset=null){
  const values=record?.values||preset||{};
  openModal(record?L.editProduct:L.addItemTitle,(modal,dismiss)=>{
    const form=make("div","form-grid");
    field(form,L.itemId,"itemId","number",values.itemId||"",null,false,!!record);
    const group=field(form,L.group,"group","text",values.group||state.activeGroup,groupOptions());
    group.value=values.group||state.activeGroup;
    field(form,L.note,"note","text",values.note||"",null,true);
    field(form,L.buyPrice,"buyPrice","number",values.buyPrice||"0");
    field(form,L.sellPrice,"sellPrice","number",values.sellPrice||"0");
    modal.append(form);
    modalActions(modal,async(button,message)=>{
      button.disabled=true;
      const data={
        itemId:form.querySelector('[name="itemId"]').value,
        group:form.querySelector('[name="group"]').value,
        note:form.querySelector('[name="note"]').value,
        buyPrice:form.querySelector('[name="buyPrice"]').value,
        sellPrice:form.querySelector('[name="sellPrice"]').value
      };
      if(record)data.recordKey=record.key;
      try{
        const result=await panel.invoke("catalog",bodyOf(data));
        if(!result.success)throw new Error(result.message||L.requestFailed);
        state.activeGroup=data.group;
        dismiss();
        await refresh();
        showMessage($("#catalog-message"),L.saved,"ok");
      }catch(error){
        showMessage(message,error.message||L.requestFailed,"err");
        button.disabled=false;
      }
    },record?{
      label:L.remove,
      run:async(button,message)=>{
        if(!confirm(L.deleteProductConfirm))return;
        button.disabled=true;
        try{
          const result=await postEndpoint("catalog","delete",{key:record.key});
          if(!result.success)throw new Error(result.message||L.requestFailed);
          dismiss();
          await refresh();
          showMessage($("#catalog-message"),L.deleted,"ok");
        }catch(error){
          showMessage(message,error.message||L.requestFailed,"err");
          button.disabled=false;
        }
      }
    }:null);
  });
}

async function deleteProduct(record){
  if(!confirm(L.deleteProductConfirm))return;
  try{
    const result=await postEndpoint("catalog","delete",{key:record.key});
    if(!result.success)throw new Error(result.message||L.requestFailed);
    await refresh();
    showMessage($("#catalog-message"),L.deleted,"ok");
  }catch(error){
    showMessage($("#catalog-message"),error.message||L.requestFailed,"err");
  }
}

async function searchItems(){
  const query=$("#item-search").value.trim(),serial=++searchSerial,results=$("#search-results");
  if(!query){
    results.replaceChildren();
    showMessage($("#search-message"),L.searchHint,"info");
    return;
  }
  showMessage($("#search-message"),L.loading,"info");
  try{
    const response=await panel.invoke("search",bodyOf({query}));
    if(serial!==searchSerial)return;
    results.replaceChildren();
    const rows=response.rows||[];
    if(!rows.length){
      showMessage($("#search-message"),response.message||L.noResults,"info");
      return;
    }
    showMessage($("#search-message"),response.message||"","info");
    for(const row of rows){
      const card=make("div","search-result"),id=String(row[0]||""),name=String(row[1]||id);
      const add=make("button","",L.add);
      add.type="button";
      add.onclick=()=>openProductModal(null,{
        itemId:id,group:state.activeGroup,note:"",buyPrice:"0",sellPrice:"0",searchName:name
      });
      const displayName=localizedName(name);displayName.classList.add("search-name");
      card.append(make("span","search-id","#"+id),displayName,add);
      results.append(card);
    }
  }catch(error){
    if(serial===searchSerial){
      results.replaceChildren();
      showMessage($("#search-message"),error.message||L.requestFailed,"err");
    }
  }
}

$("#add-group").onclick=()=>openGroupModal();
$("#edit-group").onclick=()=>{const group=currentGroup();if(group)openGroupModal(group)};
$("#item-search").addEventListener("input",()=>{
  clearTimeout(searchTimer);
  searchTimer=setTimeout(searchItems,250);
});
showMessage($("#search-message"),L.searchHint,"info");
panel.onRefresh(refresh);
refresh();
